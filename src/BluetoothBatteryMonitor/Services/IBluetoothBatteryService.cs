using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Common interface for Bluetooth battery monitoring services.
/// </summary>
public interface IBluetoothBatteryService
{
    /// <summary>
    /// Triggered when a device's battery or connection status is updated.
    /// </summary>
    event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    /// <summary>
    /// Triggered when a device is removed from the system.
    /// </summary>
    event EventHandler<string>? DeviceRemoved;

    /// <summary>
    /// List of all currently monitored devices.
    /// </summary>
    IReadOnlyCollection<BluetoothDeviceModel> Devices { get; }

    /// <summary>
    /// Starts the battery monitoring engine in the background.
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the battery monitoring engine and releases resources.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Forcefully queries all providers to instantly refresh the device list.
    /// </summary>
    Task RefreshDevicesAsync();
}
