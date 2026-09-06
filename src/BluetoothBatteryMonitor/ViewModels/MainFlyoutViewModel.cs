using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothBatteryMonitor.Models;
using BluetoothBatteryMonitor.Services;

namespace BluetoothBatteryMonitor.ViewModels;

/// <summary>
/// ViewModel managing the flyout window and main system tray state.
/// </summary>
public partial class MainFlyoutViewModel : ObservableObject
{
    private readonly IBluetoothBatteryService _batteryService;

    [ObservableProperty]
    private ObservableCollection<DeviceCardViewModel> _devices = new();

    [ObservableProperty]
    private bool _hasDevices;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private int? _lowestBatteryLevel;

    [ObservableProperty]
    private bool _isLowestCharging;

    [ObservableProperty]
    private bool _isRunAtStartup;

    [ObservableProperty]
    private string _lastRefreshedTime = string.Empty;

    /// <summary>
    /// Triggered when the tray icon needs to be updated: (Lowest battery, Is charging).
    /// </summary>
    public event Action<int?, bool>? TrayIconUpdateRequested;

    public MainFlyoutViewModel(IBluetoothBatteryService batteryService)
    {
        _batteryService = batteryService;
        _isRunAtStartup = StartupManager.IsRunAtStartup();

        _batteryService.DeviceUpdated += OnDeviceUpdated;
        _batteryService.DeviceRemoved += OnDeviceRemoved;

        UpdateSummary();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;
        try
        {
            await _batteryService.RefreshDevicesAsync();
            LastRefreshedTime = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainFlyoutViewModel] Yenileme hatası: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
            UpdateSummary();
        }
    }

    [RelayCommand]
    public void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainFlyoutViewModel] Bluetooth ayarları açılamadı: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ToggleStartup()
    {
        bool newState = !IsRunAtStartup;
        if (StartupManager.SetRunAtStartup(newState))
        {
            IsRunAtStartup = newState;
        }
    }

    [RelayCommand]
    public void ExitApp()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    private void OnDeviceUpdated(object? sender, BluetoothDeviceModel model)
    {
        RunOnDispatcher(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == model.Id || d.Model.Matches(model));
            if (existing != null)
            {
                existing.UpdateFromModel();
            }
            else
            {
                Devices.Add(new DeviceCardViewModel(model));
            }

            UpdateSummary();
        });
    }

    private void OnDeviceRemoved(object? sender, string deviceId)
    {
        RunOnDispatcher(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (existing != null)
            {
                Devices.Remove(existing);
            }
            UpdateSummary();
        });
    }

    public void UpdateSummary()
    {
        HasDevices = Devices.Count > 0;

        // Filter devices that are connected and have a valid battery level (including 0%)
        var activeWithBattery = Devices
            .Where(d => d.IsConnected && d.HasBattery)
            .OrderBy(d => d.BatteryLevel)
            .ToList();

        if (activeWithBattery.Count > 0)
        {
            var lowest = activeWithBattery.First();
            LowestBatteryLevel = lowest.BatteryLevel;
            IsLowestCharging = lowest.IsCharging;
        }
        else
        {
            // Fallback to any device with known battery level even if not currently connected
            var anyWithBattery = Devices
                .Where(d => d.HasBattery)
                .OrderBy(d => d.BatteryLevel)
                .ToList();

            if (anyWithBattery.Count > 0)
            {
                LowestBatteryLevel = anyWithBattery.First().BatteryLevel;
                IsLowestCharging = anyWithBattery.First().IsCharging;
            }
            else
            {
                LowestBatteryLevel = null;
                IsLowestCharging = false;
            }
        }

        TrayIconUpdateRequested?.Invoke(LowestBatteryLevel, IsLowestCharging);
    }

    private static void RunOnDispatcher(Action action)
    {
        if (Application.Current == null)
        {
            action();
            return;
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else if (!Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.InvokeAsync(action);
        }
    }
}
