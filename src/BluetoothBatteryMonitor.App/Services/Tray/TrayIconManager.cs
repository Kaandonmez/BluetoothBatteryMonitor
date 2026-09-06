using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.ViewModels;
using BluetoothBatteryMonitor.App.Views;

namespace BluetoothBatteryMonitor.App.Services.Tray;

public class TrayIconManager : IDisposable
{
    private readonly MainViewModel _mainViewModel;
    private readonly FlyoutWindow _flyoutWindow;
    private TaskbarIcon? _taskbarIcon;
    private ContextMenu? _contextMenu;
    private Icon? _currentIcon;
    private DateTime _lastContextMenuClosedTime = DateTime.MinValue;
    private bool _flyoutWasOpenOnMouseDown;
    private IntPtr _contextMenuHwnd = IntPtr.Zero;

    public TrayIconManager(MainViewModel mainViewModel, FlyoutWindow flyoutWindow)
    {
        _mainViewModel = mainViewModel;
        _flyoutWindow = flyoutWindow;

        _mainViewModel.BatteryStateChanged += OnBatteryStateChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    public void Initialize()
    {
        _contextMenu = CreateContextMenu();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Bluetooth Pil Monitörü",
            ContextMenu = _contextMenu,
            NoLeftClickDelay = true,
            MenuActivation = PopupActivationMode.RightClick
        };

        // Sağ tık menüsü açılmadan önce açık olan Flyout'u derhal kapat
        _taskbarIcon.TrayRightMouseDown += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        _taskbarIcon.PreviewTrayContextMenuOpen += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        // Sol tık MouseDown: Tıklamanın başladığı anda Flyout'un açık olup olmadığını kaydet
        _taskbarIcon.TrayLeftMouseDown += (s, e) =>
        {
            _flyoutWasOpenOnMouseDown = _flyoutWindow.IsFlyoutOpen;
            if (_flyoutWasOpenOnMouseDown)
            {
                // Kullanıcı açık olan pencereyi kapatmak için tıkladı: Anında gizle
                _flyoutWindow.HideImmediately();
            }
        };

        // Sol tık ile Flyout açma/kapatma (MouseUp ile Windows shell odak çakışması engellenir)
        _taskbarIcon.TrayLeftMouseUp += (s, e) =>
        {
            // Eğer ContextMenu açıksa veya az önce kapandıysa, menüyü kapatıp Flyout'u göster
            if (_contextMenu != null && (_contextMenu.IsOpen || (DateTime.UtcNow - _lastContextMenuClosedTime).TotalMilliseconds < 250))
            {
                _contextMenu.IsOpen = false;
                _flyoutWindow.ShowFlyout();
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // Eğer fareye basıldığında Flyout zaten açıktıysa, kullanıcı pencereyi kapatmak için tıklamıştır (tekrar açma)
            if (_flyoutWasOpenOnMouseDown)
            {
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // Flyout kapalıydı, aç
            _flyoutWindow.ShowFlyout();
        };

        // Çift tık ile Flyout penceresini aç
        _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
        {
            _flyoutWindow.ShowFlyout();
        };

        UpdateTrayIcon();
        try
        {
            _taskbarIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[TrayIconManager] Tepsi simgesi ilk açılışta zorlanamadı: {ex.Message}");
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        menu.Opened += (s, e) =>
        {
            // Flyout açıksa derhal kapat
            _flyoutWindow.HideImmediately();

            // Win32 NotifyIcon kuralı: Dışarı tıklandığında menünün kapanabilmesi için ContextMenu'yu ön plana al
            if (PresentationSource.FromVisual(menu) is HwndSource hwndSource)
            {
                _contextMenuHwnd = hwndSource.Handle;
                NativeMethods.SetForegroundWindow(_contextMenuHwnd);
            }
        };

        menu.Closed += (s, e) =>
        {
            _lastContextMenuClosedTime = DateTime.UtcNow;
            if (_contextMenuHwnd != IntPtr.Zero)
            {
                NativeMethods.PostMessage(_contextMenuHwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
                _contextMenuHwnd = IntPtr.Zero;
            }
        };

        var openItem = new MenuItem { Header = "Aç / Gizle" };
        openItem.Click += (s, e) => _flyoutWindow.ToggleFlyout();
        menu.Items.Add(openItem);

        var refreshItem = new MenuItem { Header = "Aygıtları Yenile" };
        refreshItem.Click += async (s, e) => await _mainViewModel.RefreshDevicesAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var startupItem = new MenuItem
        {
            Header = "Windows ile Başlat",
            IsCheckable = true,
            IsChecked = StartupManager.IsStartupEnabled()
        };
        // Menü her açıldığında gerçek başlangıç durumunu yansıt
        menu.Opened += (s, e) =>
        {
            startupItem.IsChecked = StartupManager.IsStartupEnabled();
        };
        startupItem.Click += (s, e) =>
        {
            // WPF MenuItem IsCheckable=true olduğunda tıklamada IsChecked durumunu otomatik değiştirir
            bool newState = startupItem.IsChecked;
            StartupManager.SetStartup(newState);
            var settings = AppSettings.Load();
            settings.LaunchAtStartup = newState;
            settings.Save();
        };
        menu.Items.Add(startupItem);

        var settingsItem = new MenuItem { Header = "Ayarlar..." };
        settingsItem.Click += (s, e) => MainViewModel.OpenSettingsWindow();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = "Hakkında" };
        aboutItem.Click += (s, e) => MainViewModel.OpenAboutWindow();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Çıkış" };
        exitItem.Click += (s, e) => MainViewModel.ExitApp();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnBatteryStateChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.Invoke(UpdateTrayIcon);
    }

    private void OnThemeChanged(object? sender, bool isDark)
    {
        Application.Current?.Dispatcher?.Invoke(UpdateTrayIcon);
    }

    public void UpdateTrayIcon()
    {
        if (_taskbarIcon == null) return;

        bool isDark = ThemeManager.IsWindowsDarkTheme();
        int? lowest = _mainViewModel.LowestBatteryLevel;
        bool isCharging = _mainViewModel.IsLowestCharging;

        var newIcon = TrayIconGenerator.GenerateTrayIcon(lowest, isCharging, isDark);

        _taskbarIcon.Icon = newIcon;

        _currentIcon?.Dispose();
        _currentIcon = newIcon;

        if (!_taskbarIcon.IsCreated)
        {
            try
            {
                _taskbarIcon.ForceCreate();
            }
            catch { }
        }

        // Tooltip metnini güncelle
        if (_mainViewModel.Devices.Count > 0)
        {
            var summary = string.Join("\n", _mainViewModel.Devices.Take(4).Select(d => $"{d.Name}: {d.BatteryText}"));
            _taskbarIcon.ToolTipText = $"Bluetooth Pil Monitörü\n{summary}";
        }
        else
        {
            _taskbarIcon.ToolTipText = "Bluetooth Pil Monitörü (Bağlı cihaz yok)";
        }
    }

    public void Dispose()
    {
        _mainViewModel.BatteryStateChanged -= OnBatteryStateChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;

        _taskbarIcon?.Dispose();
        _currentIcon?.Dispose();
    }
}
