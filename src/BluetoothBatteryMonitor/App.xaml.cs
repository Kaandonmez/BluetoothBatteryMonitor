using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using BluetoothBatteryMonitor.Helpers;
using BluetoothBatteryMonitor.Services;
using BluetoothBatteryMonitor.ViewModels;
using BluetoothBatteryMonitor.Views;

namespace BluetoothBatteryMonitor;

/// <summary>
/// Uygulama giriş noktası ve sistem tepsisi (System Tray) yaşam döngüsü yöneticisi.
/// Tek örnek (Single Instance Mutex), dinamik tepsi simgesi ve sağ tık bağlam menüsünü yönetir.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\BluetoothBatteryMonitor_SingleInstance_Mutex";
    private const string ShowEventName = @"Local\BluetoothBatteryMonitor_ShowWindow_Event";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _eventWaitCts;

    private BluetoothBatteryAggregatorService? _batteryService;
    private MainFlyoutViewModel? _viewModel;
    private FlyoutWindow? _flyoutWindow;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _startupMenuItem;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Tek Örnek Koruması (Single Instance Mutex - Standart kullanıcılar için Local namespace)
        bool isFirstInstance;
        try
        {
            _mutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // İkinci kopya açıldıysa ilk kopyaya Flyout'u öne getirme sinyali ver ve kapan
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(ShowEventName);
                existingEvent.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        // İlk kopya için sinyal dinleyici oluştur
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _eventWaitCts = new CancellationTokenSource();
            _ = StartSingleInstanceSignalListenerAsync(_showEvent, _eventWaitCts.Token);
        }
        catch { }

        // 2. Servislerin ve ViewModel'in Başlatılması
        _batteryService = new BluetoothBatteryAggregatorService();
        _viewModel = new MainFlyoutViewModel(_batteryService);
        _flyoutWindow = new FlyoutWindow(_viewModel);

        // 3. Dinamik Sistem Tepsisi Simgesinin (TaskbarIcon) Kurulması
        InitializeTaskbarIcon();

        // 4. Pil Değişikliklerinde Tepsi İkonu Güncelleme Dinleyicisi
        _viewModel.TrayIconUpdateRequested += OnTrayIconUpdateRequested;

        // 5. Arka Plan Bluetooth İzleme Motorunu Başlat
        try
        {
            await _batteryService.StartMonitoringAsync();
            await _viewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Arka plan başlatma hatası: {ex.Message}");
        }
    }

    private void InitializeTaskbarIcon()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Bluetooth Pil Monitörü\nCihazlar taranıyor...",
            Icon = TrayIconRenderer.CreateTrayIcon(null, false)
        };

        // Sol Tık: Modern Flyout penceresini aç/kapat
        _taskbarIcon.TrayLeftMouseDown += async (_, _) =>
        {
            if (_flyoutWindow != null)
            {
                await _flyoutWindow.ToggleVisibilityAsync();
            }
        };

        // Çift Tık: Doğrudan Flyout aç
        _taskbarIcon.TrayMouseDoubleClick += async (_, _) =>
        {
            if (_flyoutWindow != null)
            {
                await _flyoutWindow.ShowWithAnimationAsync();
            }
        };

        // Sağ Tık: Pratik Bağlam Menüsü
        var contextMenu = new ContextMenu();

        var openItem = new MenuItem { Header = "Aç" };
        openItem.Click += async (_, _) =>
        {
            if (_flyoutWindow != null) await _flyoutWindow.ShowWithAnimationAsync();
        };

        var refreshItem = new MenuItem { Header = "Aygıtları Yenile" };
        refreshItem.Click += async (_, _) =>
        {
            if (_viewModel != null) await _viewModel.RefreshAsync();
        };

        _startupMenuItem = new MenuItem
        {
            Header = "Windows ile Başlat",
            IsCheckable = true,
            IsChecked = StartupManager.IsRunAtStartup()
        };
        _startupMenuItem.Click += (_, _) =>
        {
            bool newState = !StartupManager.IsRunAtStartup();
            if (StartupManager.SetRunAtStartup(newState))
            {
                _startupMenuItem.IsChecked = newState;
                if (_viewModel != null) _viewModel.IsRunAtStartup = newState;
            }
        };

        var settingsItem = new MenuItem { Header = "Windows Bluetooth Ayarları" };
        settingsItem.Click += (_, _) =>
        {
            _viewModel?.OpenBluetoothSettings();
        };

        var exitItem = new MenuItem { Header = "Çıkış" };
        exitItem.Click += (_, _) =>
        {
            ShutdownApp();
        };

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(_startupMenuItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _taskbarIcon.ContextMenu = contextMenu;
        _taskbarIcon.ForceCreate();
    }

    private void OnTrayIconUpdateRequested(int? lowestBattery, bool isCharging)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_taskbarIcon == null) return;

            try
            {
                // Dinamik ikon oluştur
                var oldIcon = _taskbarIcon.Icon;
                _taskbarIcon.Icon = TrayIconRenderer.CreateTrayIcon(lowestBattery, isCharging);
                oldIcon?.Dispose();

                // Detaylı araç ipucu (Tooltip) güncelle
                var sb = new StringBuilder();
                sb.AppendLine("Bluetooth Pil Monitörü");

                if (_viewModel != null && _viewModel.Devices.Count > 0)
                {
                    foreach (var dev in _viewModel.Devices)
                    {
                        if (dev.IsConnected)
                        {
                            sb.AppendLine($"• {dev.Name}: {dev.BatteryLevelText}{(dev.IsCharging ? " ⚡" : "")}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("Bağlı cihaz bulunamadı");
                }

                string tip = sb.ToString().TrimEnd();
                // Windows bildirim alanı tooltip uzunluk sınırını aşmamak için kırp
                if (tip.Length > 127)
                {
                    tip = tip[..124] + "...";
                }

                _taskbarIcon.ToolTipText = tip;

                // Başlangıç durumu menüsünü senkronize tut
                if (_startupMenuItem != null)
                {
                    _startupMenuItem.IsChecked = StartupManager.IsRunAtStartup();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] TrayIcon güncelleme hatası: {ex.Message}");
            }
        });
    }

    private async Task StartSingleInstanceSignalListenerAsync(EventWaitHandle showEvent, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() => showEvent.WaitOne(), token);
                if (token.IsCancellationRequested) break;

                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_flyoutWindow != null)
                    {
                        await _flyoutWindow.ShowWithAnimationAsync();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void ShutdownApp()
    {
        _eventWaitCts?.Cancel();
        _showEvent?.Dispose();

        _taskbarIcon?.Dispose();
        _flyoutWindow?.ClosePermanently();
        _batteryService?.Dispose();

        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownApp();
        base.OnExit(e);
    }
}
