using System;
using System.Linq;
using System.Threading;
using System.Windows;
using BluetoothBatteryMonitor.App.Services.Api;
using BluetoothBatteryMonitor.App.Services.Audio;
using BluetoothBatteryMonitor.App.Services.Bluetooth;
using BluetoothBatteryMonitor.App.Services.Notification;
using BluetoothBatteryMonitor.App.Services.System;
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
        // Ekran görüntülerini otomatik alma modu
        if (e.Args.Any(a => a.Equals("--capture-screenshots", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            ScreenshotCaptureService.RunAndExit();
            return;
        }

        // 1. Tek Örnek (Single Instance) Kontrolü
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
            // Halihazırda çalışan örneğe Flyout'u açması için izin ve sinyal gönder
            try
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                using var existingEvent = EventWaitHandle.OpenExisting(EventName);
                existingEvent.Set();
                Thread.Sleep(100);
            }
            catch
            {
                // Event açılamadıysa sessizce çık
            }

            Shutdown();
            return;
        }

        // İlk kopya: Diğer örneklerden gelecek sinyalleri dinlemek için event oluştur
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
            // EventWaitHandle oluşturulamazsa devam et
        }

        base.OnStartup(e);

        // Pencere kapatıldığında uygulamanın kapanmasını engelle (Tepside kalması için)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Global Hata Yakalayıcılar
        DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
            MessageBox.Show(
                $"Kritik bir hata oluştu ve uygulama kapatılacak:\n\n{args.Exception.Message}\n\nDetay:\n{args.Exception}",
                "Bluetooth Pil Monitörü - Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"Beklenmeyen kritik bir hata oluştu:\n\n{ex?.Message}\n\nDetay:\n{ex}",
                "Bluetooth Pil Monitörü - Kritik Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        // 2. Sistem Servisleri ve Tema Başlatma
        ThemeManager.Initialize();

        // 3. Ses Yöneticisi ve Akıllı Yönlendirici (Smart Audio Router)
        _audioEndpointManager = new AudioEndpointManager();
        _smartAudioRouter = new SmartAudioRouter(_audioEndpointManager);

        // 4. Pil Algılama Motoru ve Bildirimler
        _notificationService = new ToastNotificationService();
        _engine = new BluetoothBatteryEngine(_notificationService, null, _audioEndpointManager);
        _engine.DevicesUpdated += (s, devices) =>

        {
            _smartAudioRouter?.OnDevicesUpdated(devices, AppSettings.Load());
        };
        // 5. ViewModel ve Pencereler
        _mainViewModel = new MainViewModel(_engine, _audioEndpointManager);
        _flyoutWindow = new FlyoutWindow(_mainViewModel);
        MainWindow = _flyoutWindow;

        // 6. Sistem Tepsisi Yöneticisi
        _trayIconManager = new TrayIconManager(_mainViewModel, _flyoutWindow);
        _trayIconManager.Initialize();

        // 7. Yerel REST / JSON API Sunucusu (Varsayılan: http://127.0.0.1:23253/devices)
        var appSettings = AppSettings.Load();
        _apiServer = new LocalRestApiServer(() => _engine.CurrentDevices, _audioEndpointManager, appSettings.RestApiPort);
        if (appSettings.EnableRestApi)
        {
            _apiServer.Start();
        }

        AppSettings.SettingsChanged += (s, settings) =>
        {
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

        // 8. Motoru Çalıştır
        _engine.Start();

        // Eğer komut satırında --minimized belirtilmemişse, ilk açılışta flyout penceresini göster
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
