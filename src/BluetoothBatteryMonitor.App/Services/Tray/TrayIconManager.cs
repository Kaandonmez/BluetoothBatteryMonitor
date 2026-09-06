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
using BluetoothBatteryMonitor.App.Services.Localization;
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
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string lang)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _contextMenu = CreateContextMenu();
            if (_taskbarIcon != null)
            {
                _taskbarIcon.ContextMenu = _contextMenu;
            }
            UpdateTrayIcon();
        });
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

        // Close active Flyout immediately before displaying context menu
        _taskbarIcon.TrayRightMouseDown += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        _taskbarIcon.PreviewTrayContextMenuOpen += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        // Left mouse button down: Record whether the flyout was already open when clicked
        _taskbarIcon.TrayLeftMouseDown += (s, e) =>
        {
            _flyoutWasOpenOnMouseDown = _flyoutWindow.IsFlyoutOpen;
            if (_flyoutWasOpenOnMouseDown)
            {
                // User clicked to dismiss the currently open window: Hide immediately
                _flyoutWindow.HideImmediately();
            }
        };

        // Left mouse button up: Toggle flyout window (MouseUp avoids Windows shell focus race condition)
        _taskbarIcon.TrayLeftMouseUp += (s, e) =>
        {
            // If ContextMenu is open or was just closed, dismiss menu and display flyout
            if (_contextMenu != null && (_contextMenu.IsOpen || (DateTime.UtcNow - _lastContextMenuClosedTime).TotalMilliseconds < 250))
            {
                _contextMenu.IsOpen = false;
                _flyoutWindow.ShowFlyout();
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // If Flyout was open when clicked, the intent was dismissal (do not reopen)
            if (_flyoutWasOpenOnMouseDown)
            {
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // Flyout was closed, show it
            _flyoutWindow.ShowFlyout();
        };

        // Double click: Open flyout window
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
            global::System.Diagnostics.Debug.WriteLine($"[TrayIconManager] Failed to force create tray icon on initial launch: {ex.Message}");
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        menu.Opened += (s, e) =>
        {
            // Dismiss flyout immediately if open
            _flyoutWindow.HideImmediately();

            // Win32 NotifyIcon standard: Set foreground window to ensure context menu dismisses when clicking outside
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

        var openItem = new MenuItem { Header = LocalizationService.GetString("Tray_OpenHide") };
        openItem.Click += (s, e) => _flyoutWindow.ToggleFlyout();
        menu.Items.Add(openItem);

        var refreshItem = new MenuItem { Header = LocalizationService.GetString("Tray_Refresh") };
        refreshItem.Click += async (s, e) => await _mainViewModel.RefreshDevicesAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var startupItem = new MenuItem
        {
            Header = LocalizationService.GetString("Tray_LaunchAtStartup"),
            IsCheckable = true,
            IsChecked = StartupManager.IsStartupEnabled()
        };
        // Sync actual startup state each time menu opens
        menu.Opened += (s, e) =>
        {
            startupItem.IsChecked = StartupManager.IsStartupEnabled();
        };
        startupItem.Click += (s, e) =>
        {
            // WPF MenuItem IsCheckable=true automatically toggles IsChecked upon click
            bool newState = startupItem.IsChecked;
            StartupManager.SetStartup(newState);
            var settings = AppSettings.Load();
            settings.LaunchAtStartup = newState;
            settings.Save();
        };
        menu.Items.Add(startupItem);

        var settingsItem = new MenuItem { Header = LocalizationService.GetString("Tray_Settings") };
        settingsItem.Click += (s, e) => MainViewModel.OpenSettingsWindow();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = LocalizationService.GetString("Tray_About") };
        aboutItem.Click += (s, e) => MainViewModel.OpenAboutWindow();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = LocalizationService.GetString("Tray_Exit") };
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

        // Update tooltip text
        if (_mainViewModel.Devices.Count > 0)
        {
            var summary = string.Join("\n", _mainViewModel.Devices.Take(4).Select(d => $"{d.Name}: {d.BatteryText}"));
            _taskbarIcon.ToolTipText = $"{LocalizationService.GetString("App_Title")}\n{summary}";
        }
        else
        {
            _taskbarIcon.ToolTipText = LocalizationService.GetString("Tray_TooltipNoDevices");
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
