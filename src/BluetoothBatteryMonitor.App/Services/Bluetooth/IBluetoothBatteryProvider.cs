using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public interface IBluetoothBatteryProvider : IDisposable
{
    string Name { get; }
    bool IsSupported { get; }

    event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default);
    void StartMonitoring();
    void StopMonitoring();
}
