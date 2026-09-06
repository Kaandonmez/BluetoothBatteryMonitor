using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Bluetooth pil izleme servisleri için ortak arayüz.
/// </summary>
public interface IBluetoothBatteryService
{
    /// <summary>
    /// Bir cihazın pil veya bağlantı durumu güncellendiğinde tetiklenir.
    /// </summary>
    event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    /// <summary>
    /// Bir cihaz sistemden kaldırıldığında tetiklenir.
    /// </summary>
    event EventHandler<string>? DeviceRemoved;

    /// <summary>
    /// Şu anda takip edilen tüm cihazların listesi.
    /// </summary>
    IReadOnlyCollection<BluetoothDeviceModel> Devices { get; }

    /// <summary>
    /// Pil izleme motorunu arka planda başlatır.
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pil izleme motorunu durdurur ve kaynakları serbest bırakır.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Tüm sağlayıcıları zorla sorgulayarak cihaz listesini anlık yeniler.
    /// </summary>
    Task RefreshDevicesAsync();
}
