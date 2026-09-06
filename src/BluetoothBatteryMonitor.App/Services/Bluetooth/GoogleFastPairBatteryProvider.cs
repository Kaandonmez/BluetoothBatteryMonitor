using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public record FastPairBatteryInfo(
    int? LeftLevel,
    int? RightLevel,
    int? CaseLevel,
    int? SingleLevel,
    bool IsLeftCharging,
    bool IsRightCharging,
    bool IsCaseCharging,
    bool IsSingleCharging,
    bool IsTws);

/// <summary>
/// Provider for Google Fast Pair compliant devices (Pixel Buds, JBL, Nothing Ear, OnePlus, etc.)
/// that parses 0xFE2C BLE Advertisement Service Data packets to extract battery levels and charging states.
/// </summary>
public class GoogleFastPairBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Google Fast Pair (BLE 0xFE2C)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    // Google Fast Pair Service 16-bit UUID: 0xFE2C
    public static readonly Guid FastPairServiceUuid = new("0000FE2C-0000-1000-8000-00805F9B34FB");
    public const ushort FastPairServiceShortUuid = 0xFE2C;

    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _detectedDevices = new();
    private static readonly TimeSpan CacheTimeout = TimeSpan.FromSeconds(30);

    private bool _isMonitoring;
    private bool _isDisposed;

    public GoogleFastPairBatteryProvider()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        // Filter by 0xFE2C Service UUID
        _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(FastPairServiceUuid);
        _watcher.Received += OnAdvertisementReceived;
    }

    public Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        foreach (var kvp in _detectedDevices)
        {
            if (now - kvp.Value.LastUpdated > CacheTimeout)
            {
                _detectedDevices.TryRemove(kvp.Key, out _);
            }
        }

        var list = _detectedDevices.Values.ToList();
        return Task.FromResult<IReadOnlyList<BluetoothDeviceModel>>(list);
    }

    public void StartMonitoring()
    {
        if (_isMonitoring || _isDisposed) return;
        try
        {
            _watcher.Start();
            _isMonitoring = true;
        }
        catch
        {
            // Bluetooth radio might be turned off
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        try
        {
            _watcher.Stop();
            _isMonitoring = false;
        }
        catch { }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            var ad = args.Advertisement;

            // Search for 0xFE2C in Service Data and Manufacturer Data
            foreach (var section in ad.DataSections)
            {
                // DataType 0x16 = Service Data (16-bit UUID)
                if (section.DataType == 0x16)
                {
                    byte[] rawData = section.Data.ToArray();
                    if (rawData.Length < 3) continue;

                    // First 2 bytes: Service UUID (0x2C, 0xFE - Little Endian)
                    ushort uuid = (ushort)(rawData[0] | (rawData[1] << 8));
                    if (uuid != FastPairServiceShortUuid) continue;

                    // Remaining payload is Fast Pair battery telemetry data
                    byte[] payload = rawData.Skip(2).ToArray();
                    if (TryParseFastPairAdvertisement(payload, out var info) && info != null)
                    {
                        var model = CreateDeviceModel(args.BluetoothAddress, ad.LocalName, info);
                        _detectedDevices[model.Id] = model;
                        DeviceUpdated?.Invoke(this, model);
                    }
                }
            }
        }
        catch
        {
            // Suppress packet parsing error
        }
    }

    private static BluetoothDeviceModel CreateDeviceModel(ulong address, string? localName, FastPairBatteryInfo info)
    {
        string macString = address.ToString("X12");
        string formattedMac = string.Join(":", Enumerable.Range(0, 6).Select(i => macString.Substring(i * 2, 2)));

        string name = !string.IsNullOrWhiteSpace(localName) ? localName : "Google Fast Pair Device";
        var devType = info.IsTws ? DeviceType.Earbuds : DeviceType.Headphones;

        var model = new BluetoothDeviceModel
        {
            Id = $"FastPair_{formattedMac}",
            Name = name,
            ModelName = name,
            DeviceType = devType,
            BluetoothAddress = address,
            IsConnected = true,
            ProviderSource = "Google Fast Pair (BLE 0xFE2C)",
            LastUpdated = DateTime.Now
        };

        if (info.IsTws)
        {
            model.IsTws = true;
            model.LeftBatteryLevel = info.LeftLevel;
            model.IsLeftCharging = info.IsLeftCharging;
            model.RightBatteryLevel = info.RightLevel;
            model.IsRightCharging = info.IsRightCharging;
            model.CaseBatteryLevel = info.CaseLevel;
            model.IsCaseCharging = info.IsCaseCharging;
            model.IsCharging = info.IsLeftCharging || info.IsRightCharging || info.IsCaseCharging;
            model.BatteryLevel = model.EffectiveBatteryLevel;
        }
        else
        {
            model.BatteryLevel = info.SingleLevel;
            model.IsCharging = info.IsSingleCharging;
        }

        return model;
    }

    /// <summary>
    /// Parses Google Fast Pair Service Data payload.
    /// Bit 7 (0x80): Charging flag (1 = charging, 0 = discharging).
    /// Bit 0-6 (0x7F): Battery level (0-100). 0x7F (127) = Disconnected / Unknown.
    /// </summary>
    public static bool TryParseFastPairAdvertisement(byte[] serviceData, out FastPairBatteryInfo? info)
    {
        info = null;
        if (serviceData == null || serviceData.Length == 0)
        {
            return false;
        }

        // 1. Format: 3-byte TWS (Left, Right, Case)
        if (serviceData.Length == 3)
        {
            return TryParseTwsComponents(serviceData[0], serviceData[1], serviceData[2], out info);
        }

        // 2. Format: 4-byte TWS (Header byte + Left, Right, Case)
        if (serviceData.Length == 4)
        {
            return TryParseTwsComponents(serviceData[1], serviceData[2], serviceData[3], out info);
        }

        // 3. Format: 1-byte Single Headset
        if (serviceData.Length == 1)
        {
            return TryParseSingleComponent(serviceData[0], out info);
        }

        // 4. Format: 2-byte Single Headset (Header byte + Battery)
        if (serviceData.Length == 2)
        {
            return TryParseSingleComponent(serviceData[1], out info);
        }

        // 5. Extended formats (e.g. Model ID followed by 3 bytes battery data)
        if (serviceData.Length >= 5)
        {
            // Try last 3 bytes as TWS battery candidates
            int start = serviceData.Length - 3;
            if (TryParseTwsComponents(serviceData[start], serviceData[start + 1], serviceData[start + 2], out info))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseTwsComponents(byte bLeft, byte bRight, byte bCase, out FastPairBatteryInfo? info)
    {
        info = null;

        int? leftLevel = DecodeFastPairByte(bLeft, out bool leftCharging);
        int? rightLevel = DecodeFastPairByte(bRight, out bool rightCharging);
        int? caseLevel = DecodeFastPairByte(bCase, out bool caseCharging);

        if (!leftLevel.HasValue && !rightLevel.HasValue && !caseLevel.HasValue)
        {
            return false;
        }

        info = new FastPairBatteryInfo(
            leftLevel,
            rightLevel,
            caseLevel,
            null,
            leftCharging,
            rightCharging,
            caseCharging,
            false,
            true);

        return true;
    }

    private static bool TryParseSingleComponent(byte bSingle, out FastPairBatteryInfo? info)
    {
        info = null;

        int? level = DecodeFastPairByte(bSingle, out bool isCharging);
        if (!level.HasValue)
        {
            return false;
        }

        info = new FastPairBatteryInfo(
            null,
            null,
            null,
            level,
            false,
            false,
            false,
            isCharging,
            false);

        return true;
    }

    private static int? DecodeFastPairByte(byte raw, out bool isCharging)
    {
        isCharging = (raw & 0x80) != 0;
        int level = raw & 0x7F;

        // 0x7F (127) = Disconnected / Not Available
        if (level is 0x7F or > 100)
        {
            return null;
        }

        return level;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopMonitoring();
        _watcher.Received -= OnAdvertisementReceived;
        _detectedDevices.Clear();
    }
}
