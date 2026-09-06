using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Bluetooth;
using BluetoothBatteryMonitor.App.Views;
using BluetoothBatteryMonitor.App.Services.Audio;

using BluetoothBatteryMonitor.App.Services.Localization;

namespace BluetoothBatteryMonitor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static MainViewModel? _instance;
    private static SettingsWindow? _activeSettingsWindow;
    private static AboutWindow? _activeAboutWindow;
    private readonly BluetoothBatteryEngine _engine;
    private readonly IAudioEndpointManager? _audioManager;

    public MainViewModel(BluetoothBatteryEngine engine, IAudioEndpointManager? audioManager = null)
    {
        _instance = this;
        _engine = engine;
        _audioManager = audioManager;
        Devices = new ObservableCollection<DeviceItemViewModel>();

        _engine.DevicesUpdated += OnEngineDevicesUpdated;
        _engine.StatusChanged += OnEngineStatusChanged;
        LocalizationService.LanguageChanged += (_, _) => UpdateStatusMessage();
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = LocalizationService.GetString("Flyout_Status_UpToDate");

    [ObservableProperty]
    private int? _lowestBatteryLevel;

    [ObservableProperty]
    private bool _isLowestCharging;

    [ObservableProperty]
    private bool _hasDevices;

    public event EventHandler? BatteryStateChanged;

    public void RefreshAudioVolumes()
    {
        foreach (var dev in Devices)
        {
            dev.RefreshAudioVolume();
        }
    }

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = LocalizationService.GetString("Flyout_Status_Scanning");

        try
        {
            await _engine.RefreshAllDevicesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public static void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch error
        }
    }

    [RelayCommand]
    public static void OpenSettingsWindow()
    {
        OpenSettingsWindow(null);
    }

    public static void OpenSettingsWindow(IEnumerable<BluetoothDeviceModel>? devices = null)
    {
        try
        {
            if (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded)
            {
                _activeSettingsWindow.Activate();
                var helper = new WindowInteropHelper(_activeSettingsWindow);
                if (helper.Handle != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(helper.Handle);
                }
                return;
            }

            var devList = devices ?? _instance?._engine?.CurrentDevices;
            _activeSettingsWindow = new SettingsWindow(devList);
            _activeSettingsWindow.Closed += (s, e) => _activeSettingsWindow = null;
            _activeSettingsWindow.Show();
            _activeSettingsWindow.Activate();
            var handle = new WindowInteropHelper(_activeSettingsWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(handle);
            }
        }
        catch
        {
            // Ignore dialog show error
        }
    }

    [RelayCommand]
    public static void OpenAppSettings()
    {
        OpenSettingsWindow();
    }

    [RelayCommand]
    public static void OpenAboutWindow()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(OpenAboutWindow);
            return;
        }

        try
        {
            if (_activeAboutWindow != null && _activeAboutWindow.IsLoaded)
            {
                _activeAboutWindow.Activate();
                var helper = new WindowInteropHelper(_activeAboutWindow);
                if (helper.Handle != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(helper.Handle);
                }
                return;
            }

            _activeAboutWindow = new AboutWindow();
            _activeAboutWindow.Closed += (s, e) => _activeAboutWindow = null;
            _activeAboutWindow.Show();
            _activeAboutWindow.Activate();
            var handle = new WindowInteropHelper(_activeAboutWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(handle);
            }
        }
        catch
        {
            try
            {
                MessageBox.Show(
                    "Bluetooth Battery Monitor\nVersion 1.0.1\n\nBluetooth battery telemetry and audio management tool for Windows 10 & 11.",
                    "Bluetooth Battery Monitor - About",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                // Ignore message box error
            }
        }
    }

    [RelayCommand]
    public static void ExitApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void OnEngineDevicesUpdated(object? sender, IReadOnlyList<BluetoothDeviceModel> deviceModels)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Update UI collection
            var existingIds = Devices.Select(d => d.Id).ToHashSet();
            var incomingIds = deviceModels.Select(d => d.Id).ToHashSet();

            // Remove disconnected/removed devices
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (!incomingIds.Contains(Devices[i].Id))
                {
                    Devices.RemoveAt(i);
                }
            }

            // Add, update, and order items (connected devices stay on top)
            for (int targetIndex = 0; targetIndex < deviceModels.Count; targetIndex++)
            {
                var model = deviceModels[targetIndex];
                int currentIndex = -1;
                for (int j = 0; j < Devices.Count; j++)
                {
                    if (Devices[j].Id == model.Id)
                    {
                        currentIndex = j;
                        break;
                    }
                }

                if (currentIndex >= 0)
                {
                    Devices[currentIndex].UpdateFromModel(model);
                    if (currentIndex != targetIndex && targetIndex < Devices.Count)
                    {
                        Devices.Move(currentIndex, targetIndex);
                    }
                }
                else
                {
                    Devices.Insert(targetIndex, new DeviceItemViewModel(model, _audioManager));
                }
            }

            HasDevices = Devices.Count > 0;

            // Calculate lowest battery level (for system tray icon)
            CalculateLowestBattery();

            UpdateStatusMessage();
        });
    }

    private void UpdateStatusMessage()
    {
        int connectedCount = Devices.Count(d => d.IsConnected);
        StatusText = HasDevices
            ? (connectedCount > 0
                ? string.Format(LocalizationService.GetString("Flyout_Status_DevicesFound"), connectedCount)
                : LocalizationService.GetString("Flyout_NoDevicesTitle"))
            : LocalizationService.GetString("Flyout_NoDevicesTitle");
    }

    private void CalculateLowestBattery()
    {
        var connectedWithBattery = Devices
            .Where(d => d.IsConnected && d.HasBattery && d.BatteryProgress >= 0)
            .ToList();

        if (connectedWithBattery.Count == 0)
        {
            LowestBatteryLevel = null;
            IsLowestCharging = false;
        }
        else
        {
            var lowest = connectedWithBattery.OrderBy(d => d.BatteryProgress).First();
            LowestBatteryLevel = lowest.BatteryProgress;
            IsLowestCharging = lowest.IsCharging;
        }

        BatteryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEngineStatusChanged(object? sender, string status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = status;
        });
    }
}
