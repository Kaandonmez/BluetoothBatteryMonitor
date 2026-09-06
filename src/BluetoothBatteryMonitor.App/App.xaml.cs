using System;
using System.Linq;
using System.Threading;
using System.Windows;
using BluetoothBatteryMonitor.App.Services.Api;
using BluetoothBatteryMonitor.App.Services.Audio;
using BluetoothBatteryMonitor.App.Services.Bluetooth;
using BluetoothBatteryMonitor.App.Services.Notification;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.Services.Localization;
using BluetoothBatteryMonitor.App.Services.Tray;
using BluetoothBatteryMonitor.App.ViewModels;
using BluetoothBatteryMonitor.App.Views;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Services;

namespace BluetoothBatteryMonitor.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\BluetoothBatteryMonitor_AppMutex_77B1D0E";
    private const string EventName = @"Local\BluetoothBatteryMonitor_ShowFlyoutEvent_77B1D0E";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEventHandle;
    private Thread? _eventListenThread;

    private BluetoothBatteryEngine? _engine;
    private ToastNotificationService? _notificationService;
    private AudioEndpointManager? _audioEndpointManager;
    private SmartAudioRouter? _smartAudioRouter;
    private LocalRestApiServer? _apiServer;
    private MainViewModel? _mainViewModel;
    private FlyoutWindow? _flyoutWindow;
    private TrayIconManager? _trayIconManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Automated screenshot capture mode
        if (e.Args.Any(a => a.Equals("--capture-screenshots", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            ScreenshotCaptureService.RunAndExit();
            return;
        }

        // 1. Single Instance Check
        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isNewInstance);
        }
        catch
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            // Signal the already running instance to bring the Flyout window to foreground
            try
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                using var existingEvent = EventWaitHandle.OpenExisting(EventName);
                existingEvent.Set();
                Thread.Sleep(100);
            }
            catch
            {
                // Exit silently if event handle cannot be opened
            }

            Shutdown();
            return;
        }

        // Primary instance: create wait handle to listen for signals from secondary instances
        try
        {
            _showEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _eventListenThread = new Thread(ListenForShowSignals)
            {
                IsBackground = true
            };
            _eventListenThread.Start();
        }
        catch
        {
            // Continue if EventWaitHandle cannot be initialized
        }

        base.OnStartup(e);

        // Prevent app from exiting when windows are closed (runs in system tray)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Global Exception Handlers
        DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                $"A critical error occurred and the application will be closed:\n\n{args.Exception.Message}\n\nDetails:\n{args.Exception}",
                "Bluetooth Battery Monitor - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"An unexpected critical error occurred:\n\n{ex?.Message}\n\nDetails:\n{ex}",
                "Bluetooth Battery Monitor - Critical Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        // 2. Initialize System Services, Theme, and Localization
        var initialSettings = AppSettings.Load();
        ThemeManager.Initialize();
        LocalizationService.Initialize(initialSettings.Language);

        // 3. Audio Endpoint Manager & Smart Audio Router
        _audioEndpointManager = new AudioEndpointManager();
        _smartAudioRouter = new SmartAudioRouter(_audioEndpointManager);

        // 4. Battery Telemetry Engine & Notifications
        _notificationService = new ToastNotificationService();
        _engine = new BluetoothBatteryEngine(_notificationService, null, _audioEndpointManager);
        _engine.DevicesUpdated += (s, devices) =>
        {
            _smartAudioRouter?.OnDevicesUpdated(devices, AppSettings.Load());
        };
        // 5. ViewModel & Windows
        _mainViewModel = new MainViewModel(_engine, _audioEndpointManager);
        _flyoutWindow = new FlyoutWindow(_mainViewModel);
        MainWindow = _flyoutWindow;

        // 6. System Tray Manager
        _trayIconManager = new TrayIconManager(_mainViewModel, _flyoutWindow);
        _trayIconManager.Initialize();

        // 7. Local REST / JSON API Server (Default: http://127.0.0.1:23253/devices)
        var appSettings = initialSettings;
        _apiServer = new LocalRestApiServer(() => _engine.CurrentDevices, _audioEndpointManager, appSettings.RestApiPort);
        if (appSettings.EnableRestApi)
        {
            _apiServer.Start();
        }

        AppSettings.SettingsChanged += (s, settings) =>
        {
            LocalizationService.ApplyLanguage(settings.Language);
            _smartAudioRouter?.OnDevicesUpdated(_engine?.CurrentDevices ?? Array.Empty<BluetoothDeviceModel>(), settings);

            if (_apiServer != null)
            {
                if (!settings.EnableRestApi)
                {
                    if (_apiServer.IsRunning)
                    {
                        _apiServer.Stop();
                    }
                }
                else
                {
                    if (!_apiServer.IsRunning || _apiServer.Port != settings.RestApiPort)
                    {
                        _apiServer.Restart(settings.RestApiPort);
                    }
                }
            }
        };

        // 8. Start Engine
        _engine.Start();

        // Show flyout on initial launch unless --minimized is specified
        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                              a.Equals("-m", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            _flyoutWindow.ShowFlyout();
        }
    }

    private void ListenForShowSignals()
    {
        while (_showEventHandle != null)
        {
            try
            {
                _showEventHandle.WaitOne();
                Dispatcher.Invoke(() =>
                {
                    _flyoutWindow?.ShowFlyout();
                });
            }
            catch
            {
                break;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _apiServer?.Dispose();
        _smartAudioRouter?.Dispose();
        _audioEndpointManager?.Dispose();
        _trayIconManager?.Dispose();
        _engine?.Dispose();
        _singleInstanceMutex?.Dispose();
        _showEventHandle?.Dispose();

        base.OnExit(e);
    }
}
