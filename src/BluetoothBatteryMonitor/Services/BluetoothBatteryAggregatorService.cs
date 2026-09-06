using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Tüm pil sağlayıcılarını (BLE GATT, Windows PnP, Apple AirPods Beacon, Logitech HID++)
/// tek bir çatı altında toplayan, verileri tekilleştiren ve akıllı bildirim servisini besleyen ana motor.
/// </summary>
public class BluetoothBatteryAggregatorService : IBluetoothBatteryService, IDisposable
{
    private readonly WindowsPnpBatteryProvider _pnpProvider;
    private readonly BleGattBatteryProvider _bleProvider;
    private readonly AppleAirPodsBeaconProvider _airPodsProvider;
    private readonly LogitechHidBatteryProvider _logitechProvider;
    private readonly ToastNotificationService _toastService;

    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _periodicCts;
    private bool _isMonitoring;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;

    public IReadOnlyCollection<BluetoothDeviceModel> Devices
    {
        get
        {
            return _devices.Values
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Battery.EffectiveLowestLevel ?? 101)
                .ToList();
        }
    }

    public ToastNotificationService ToastService => _toastService;

    public BluetoothBatteryAggregatorService(ToastNotificationService? toastService = null)
    {
        _toastService = toastService ?? new ToastNotificationService();

        _pnpProvider = new WindowsPnpBatteryProvider();
        _bleProvider = new BleGattBatteryProvider();
        _airPodsProvider = new AppleAirPodsBeaconProvider();
        _logitechProvider = new LogitechHidBatteryProvider();

        _pnpProvider.DeviceUpdated += OnProviderDeviceUpdated;
        _pnpProvider.DeviceRemoved += OnProviderDeviceRemoved;

        _bleProvider.DeviceUpdated += OnProviderDeviceUpdated;
        _bleProvider.DeviceRemoved += OnProviderDeviceRemoved;

        _airPodsProvider.DeviceUpdated += OnProviderDeviceUpdated;
        _airPodsProvider.DeviceRemoved += OnProviderDeviceRemoved;

        _logitechProvider.DeviceUpdated += OnProviderDeviceUpdated;
        _logitechProvider.DeviceRemoved += OnProviderDeviceRemoved;
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring) return;
        _isMonitoring = true;

        try
        {
            // Tüm sağlayıcıları paralel başlat
            await Task.WhenAll(
                _pnpProvider.StartAsync(),
                _bleProvider.StartAsync(),
                _airPodsProvider.StartAsync(),
                _logitechProvider.StartAsync()
            );

            // Arka planda ultra düşük kaynak tüketimli periyodik hafif kontrol döngüsü (60 saniye)
            _periodicCts = new CancellationTokenSource();
            _ = RunPeriodicRefreshLoopAsync(_periodicCts.Token);

            Debug.WriteLine("[BluetoothBatteryAggregatorService] Tüm sağlayıcılar başarıyla başlatıldı.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BluetoothBatteryAggregatorService] Başlatma hatası: {ex.Message}");
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        _periodicCts?.Cancel();
        _periodicCts?.Dispose();
        _periodicCts = null;

        await Task.WhenAll(
            _pnpProvider.StopAsync(),
            _bleProvider.StopAsync(),
            _airPodsProvider.StopAsync(),
            _logitechProvider.StopAsync()
        );
    }

    public async Task RefreshDevicesAsync()
    {
        try
        {
            await Task.WhenAll(
                _pnpProvider.RefreshAsync(),
                _bleProvider.RefreshAsync(),
                _airPodsProvider.RefreshAsync(),
                _logitechProvider.RefreshAsync()
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BluetoothBatteryAggregatorService] RefreshDevicesAsync hatası: {ex.Message}");
        }
    }

    private void OnProviderDeviceUpdated(object? sender, BluetoothDeviceModel updated)
    {
        if (updated == null) return;

        // Mevcut cihazlar arasında eşleşen var mı kontrol et (MAC, Id veya İsim)
        BluetoothDeviceModel? target = null;

        foreach (var existing in _devices.Values)
        {
            if (existing.Matches(updated))
            {
                target = existing;
                break;
            }
        }

        if (target == null)
        {
            target = new BluetoothDeviceModel
            {
                Id = updated.Id,
                Name = updated.Name,
                Type = updated.Type,
                BluetoothAddress = updated.BluetoothAddress,
                ProviderSource = updated.ProviderSource
            };
            _devices[target.Id] = target;
        }

        // Bilgileri birleştir (Merge)
        target.IsConnected = updated.IsConnected;
        target.LastSeen = updated.LastSeen;

        if (target.Type == DeviceType.Unknown && updated.Type != DeviceType.Unknown)
        {
            target.Type = updated.Type;
        }

        if (string.IsNullOrWhiteSpace(target.Name) || target.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(updated.Name) && !updated.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                target.Name = updated.Name;
            }
        }

        // Pil verilerini güncelle (AirPods gibi zengin veriyi koru veya güncelle)
        if (updated.Battery.HasMultipleBatteries)
        {
            target.Battery.HasMultipleBatteries = true;
            target.Battery.LeftLevel = updated.Battery.LeftLevel ?? target.Battery.LeftLevel;
            target.Battery.IsLeftCharging = updated.Battery.IsLeftCharging;
            target.Battery.RightLevel = updated.Battery.RightLevel ?? target.Battery.RightLevel;
            target.Battery.IsRightCharging = updated.Battery.IsRightCharging;
            target.Battery.CaseLevel = updated.Battery.CaseLevel ?? target.Battery.CaseLevel;
            target.Battery.IsCaseCharging = updated.Battery.IsCaseCharging;
            target.Battery.Level = target.Battery.EffectiveLowestLevel;
            target.Battery.IsCharging = updated.Battery.IsCharging;
            target.Battery.LastUpdated = DateTime.Now;
        }
        else if (updated.Battery.Level.HasValue)
        {
            target.Battery.Level = updated.Battery.Level.Value;
            target.Battery.IsCharging = updated.Battery.IsCharging;
            target.Battery.LastUpdated = DateTime.Now;
        }

        target.ProviderSource = updated.ProviderSource;

        // Kritik pil denetimi ve Toast uyarısı
        _toastService.CheckAndNotify(target);

        // Dinleyicilere duyur
        DeviceUpdated?.Invoke(this, target);
    }

    private void OnProviderDeviceRemoved(object? sender, string deviceId)
    {
        if (_devices.TryRemove(deviceId, out _))
        {
            DeviceRemoved?.Invoke(this, deviceId);
        }
    }

    private async Task RunPeriodicRefreshLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(token);
                await RefreshDevicesAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BluetoothBatteryAggregatorService] Periyodik tarama döngüsü hatası: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _pnpProvider.DeviceUpdated -= OnProviderDeviceUpdated;
        _pnpProvider.DeviceRemoved -= OnProviderDeviceRemoved;

        _bleProvider.DeviceUpdated -= OnProviderDeviceUpdated;
        _bleProvider.DeviceRemoved -= OnProviderDeviceRemoved;

        _airPodsProvider.DeviceUpdated -= OnProviderDeviceUpdated;
        _airPodsProvider.DeviceRemoved -= OnProviderDeviceRemoved;

        _logitechProvider.DeviceUpdated -= OnProviderDeviceUpdated;
        _logitechProvider.DeviceRemoved -= OnProviderDeviceRemoved;

        _periodicCts?.Cancel();
        _periodicCts?.Dispose();
    }
}
