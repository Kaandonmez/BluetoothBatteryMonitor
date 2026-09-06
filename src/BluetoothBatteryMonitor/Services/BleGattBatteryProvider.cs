using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Provider listening to the standard Bluetooth Low Energy (BLE) GATT Battery Service (0x180F).
/// Reads instantly and subscribes to Notify via Characteristic 0x2A19 for live updates with 0% CPU.
/// </summary>
public class BleGattBatteryProvider
{
    private static readonly Guid BatteryServiceUuid = new("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelCharUuid = new("00002A19-0000-1000-8000-00805F9B34FB");

    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BluetoothLEDevice> _bleDeviceHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GattCharacteristic> _subscribedCharacteristics = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;

    public IReadOnlyCollection<BluetoothDeviceModel> Devices => _devices.Values.ToList();

    public async Task StartAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BleGattBatteryProvider] StartAsync hatası: {ex.Message}");
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            // Query BLE devices hosting GATT Battery Service or paired
            string selector = GattDeviceService.GetDeviceSelectorFromUuid(BatteryServiceUuid);
            var serviceDevices = await DeviceInformation.FindAllAsync(selector);

            foreach (var devInfo in serviceDevices)
            {
                await ConnectAndReadServiceAsync(devInfo.Id);
            }

            // Also scan battery service on all paired BLE devices
            string pairedBleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var pairedDevices = await DeviceInformation.FindAllAsync(pairedBleSelector);

            foreach (var paired in pairedDevices)
            {
                if (!_bleDeviceHandles.ContainsKey(paired.Id))
                {
                    _ = ConnectAndReadBleDeviceAsync(paired.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BleGattBatteryProvider] RefreshAsync hatası: {ex.Message}");
        }
    }

    private async Task ConnectAndReadServiceAsync(string serviceDeviceId)
    {
        try
        {
            var service = await GattDeviceService.FromIdAsync(serviceDeviceId);
            if (service == null) return;

            var bleDevice = service.Device;
            if (bleDevice == null) return;

            await HookupBleBatteryAsync(bleDevice, service);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BleGattBatteryProvider] ConnectAndReadServiceAsync hatası ({serviceDeviceId}): {ex.Message}");
        }
    }

    private async Task ConnectAndReadBleDeviceAsync(string deviceId)
    {
        try
        {
            var bleDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (bleDevice == null) return;

            var servicesResult = await bleDevice.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached);
            if (servicesResult.Status == GattCommunicationStatus.Success && servicesResult.Services.Count > 0)
            {
                foreach (var service in servicesResult.Services)
                {
                    await HookupBleBatteryAsync(bleDevice, service);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BleGattBatteryProvider] ConnectAndReadBleDeviceAsync ({deviceId}) hatası: {ex.Message}");
        }
    }

    private async Task HookupBleBatteryAsync(BluetoothLEDevice bleDevice, GattDeviceService service)
    {
        string deviceId = bleDevice.DeviceId;
        bool isNewDevice = _bleDeviceHandles.TryAdd(deviceId, bleDevice);

        var charResult = await service.GetCharacteristicsForUuidAsync(BatteryLevelCharUuid, BluetoothCacheMode.Uncached);
        if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
        {
            return;
        }

        var characteristic = charResult.Characteristics[0];

        // Read current battery value immediately
        var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        int? batteryLevel = null;
        if (readResult.Status == GattCommunicationStatus.Success && readResult.Value != null && readResult.Value.Length > 0)
        {
            using var reader = DataReader.FromBuffer(readResult.Value);
            byte rawVal = reader.ReadByte();
            if (rawVal <= 100)
            {
                batteryLevel = rawVal;
            }
        }

        var model = _devices.GetOrAdd(deviceId, id => new BluetoothDeviceModel
        {
            Id = id,
            Name = !string.IsNullOrWhiteSpace(bleDevice.Name) ? bleDevice.Name : "BLE Cihazı",
            Type = WindowsPnpBatteryProvider.DetectDeviceType(bleDevice.Name),
            BluetoothAddress = bleDevice.BluetoothAddress,
            ProviderSource = "BLE GATT"
        });

        model.IsConnected = bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
        if (batteryLevel.HasValue)
        {
            model.Battery.Level = batteryLevel.Value;
            model.Battery.LastUpdated = DateTime.Now;
        }
        model.LastSeen = DateTime.Now;

        // Subscribe to live change notifications (Notify)
        if (!_subscribedCharacteristics.ContainsKey(deviceId))
        {
            try
            {
                if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    var cccdResult = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    if (cccdResult == GattCommunicationStatus.Success)
                    {
                        characteristic.ValueChanged += (sender, args) =>
                        {
                            try
                            {
                                using var r = DataReader.FromBuffer(args.CharacteristicValue);
                                byte b = r.ReadByte();
                                if (b <= 100)
                                {
                                    model.Battery.Level = b;
                                    model.Battery.LastUpdated = DateTime.Now;
                                    model.IsConnected = true;
                                    DeviceUpdated?.Invoke(this, model);
                                }
                            }
                            catch { }
                        };
                        _subscribedCharacteristics[deviceId] = characteristic;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BleGattBatteryProvider] Notify aboneliği kurulamadı: {ex.Message}");
            }
        }

        if (isNewDevice)
        {
            bleDevice.ConnectionStatusChanged += (dev, _) =>
            {
                model.IsConnected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                DeviceUpdated?.Invoke(this, model);
            };
        }

        DeviceUpdated?.Invoke(this, model);
    }

    public Task StopAsync()
    {
        foreach (var handle in _bleDeviceHandles.Values)
        {
            try { handle.Dispose(); } catch { }
        }
        _bleDeviceHandles.Clear();
        _subscribedCharacteristics.Clear();
        _devices.Clear();

        return Task.CompletedTask;
    }
}
