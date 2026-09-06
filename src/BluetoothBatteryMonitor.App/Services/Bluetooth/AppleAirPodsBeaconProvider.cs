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

public class AppleAirPodsBeaconProvider : IBluetoothBatteryProvider
{
    public string Name => "Apple AirPods / Beats Beacon";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _detectedAirPods = new();
    private bool _isMonitoring;

    // Apple Company ID
    private const ushort AppleCompanyId = 0x004C;

    public AppleAirPodsBeaconProvider()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        // Apple Manufacturer verisine filtre koy
        var manufacturerFilter = new BluetoothLEManufacturerData
        {
            CompanyId = AppleCompanyId
        };
        _watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerFilter);

        _watcher.Received += OnAdvertisementReceived;
    }

    private static readonly TimeSpan BeaconTimeout = TimeSpan.FromSeconds(20);

    public Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        // Clean up stale AirPods beacons whose case closed or stopped advertising
        foreach (var kvp in _detectedAirPods)
        {
            if (now - kvp.Value.LastUpdated > BeaconTimeout)
            {
                _detectedAirPods.TryRemove(kvp.Key, out _);
            }
        }

        var list = _detectedAirPods.Values.ToList();
        return Task.FromResult<IReadOnlyList<BluetoothDeviceModel>>(list);
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        try
        {
            _watcher.Start();
            _isMonitoring = true;
        }
        catch
        {
            // Bluetooth radio is disabled or service unavailable
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
            foreach (var mData in args.Advertisement.ManufacturerData)
            {
                if (mData.CompanyId != AppleCompanyId) continue;

                byte[] data = mData.Data.ToArray();
                if (data.Length < 9) continue;

                // ONLY Proximity Pairing (0x07) messages carry AirPods battery telemetry.
                // Continuity messages such as 0x10 (Nearby Info) and 0x05 (AirDrop) do not carry battery info;
                // instead byte 5 carries random iOS activity flags (e.g. 0x13 -> 10% and 30%)
                // causing apparent false battery drops to 10% and 30%!
                byte packetType = data[0];
                if (packetType != 0x07) continue;

                string macString = args.BluetoothAddress.ToString("X12");
                string formattedMac = string.Join(":", Enumerable.Range(0, 6).Select(i => macString.Substring(i * 2, 2)));
                string candidateId = $"AirPods_{formattedMac}";
                _detectedAirPods.TryGetValue(candidateId, out var existing);

                // If MAC address rotated due to BLE RPA, use active AirPods seen within the last 60 seconds for smoothing
                if (existing == null && _detectedAirPods.Count > 0)
                {
                    existing = _detectedAirPods.Values
                        .Where(d => (DateTime.Now - d.LastUpdated).TotalSeconds < 60)
                        .OrderByDescending(d => d.LastUpdated)
                        .FirstOrDefault();
                }

                // Parse AirPods model and battery telemetry
                var parsedModel = ParseAirPodsData(args.BluetoothAddress, data, existing);
                if (parsedModel != null)
                {
                    _detectedAirPods[parsedModel.Id] = parsedModel;
                    DeviceUpdated?.Invoke(this, parsedModel);
                }
            }
        }
        catch
        {
            // Suppress packet parsing error
        }
    }

    public static BluetoothDeviceModel? ParseAirPodsData(ulong address, byte[] data, BluetoothDeviceModel? existing = null)
    {
        // Must be at least 9 bytes and Type 0x07 (Proximity Pairing)
        if (data.Length < 9 || data[0] != 0x07) return null;

        // Model detection:
        // In genuine Apple packets data[2] = 0x01 (prefix) and Model ID is at data[3] and data[4].
        // In some clone/test packets Model ID is at data[2] and data[3].
        ushort modelId = 0;
        if (data.Length > 4 && data[2] == 0x01)
        {
            ushort idBe = (ushort)((data[3] << 8) | data[4]);
            ushort idLe = (ushort)((data[4] << 8) | data[3]);
            if (IsKnownAirPodsModel(idBe)) modelId = idBe;
            else if (IsKnownAirPodsModel(idLe)) modelId = idLe;
            else modelId = idBe != 0 ? idBe : idLe;
        }

        if (modelId == 0 && data.Length > 3)
        {
            ushort idBe = (ushort)((data[2] << 8) | data[3]);
            ushort idLe = (ushort)((data[3] << 8) | data[2]);
            if (IsKnownAirPodsModel(idBe)) modelId = idBe;
            else if (IsKnownAirPodsModel(idLe)) modelId = idLe;
            else modelId = idBe != 0 ? idBe : idLe;
        }

        string modelName = GetModelName(modelId);

        // Standard AirPods Proximity offsets:
        // Type 0x07 (Proximity): offset = 6 (data[6]: Pod A / Pod B batteries)
        // data[7]: Charging flags and Case battery
        const int offset = 6;
        if (offset + 1 >= data.Length) return null;

        // data[offset]: Upper nibble = Pod A, Lower nibble = Pod B
        int nibble1 = (data[offset] >> 4) & 0x0F;
        int nibble2 = data[offset] & 0x0F;

        // data[offset + 1]: Upper nibble = Charging bits, Lower nibble = Case battery
        int chargingBits = (data[offset + 1] >> 4) & 0x0F;
        int caseNibble = data[offset + 1] & 0x0F;

        // data[5] Status byte (Bit 5 = 0x20: isFlipped) or data[8]
        bool isFlipped = (data[5] & 0x20) != 0 || (data.Length > 8 && (data[8] & 0x20) != 0);

        int? podA = ConvertNibbleToPercent(nibble1);
        int? podB = ConvertNibbleToPercent(nibble2);
        int? caseLevel = ConvertNibbleToPercent(caseNibble);

        bool podACharging = (chargingBits & 0x01) != 0;
        bool podBCharging = (chargingBits & 0x02) != 0;
        bool caseCharging = (chargingBits & 0x04) != 0;

        int? leftLevel = isFlipped ? podB : podA;
        bool isLeftCharging = isFlipped ? podBCharging : podACharging;

        int? rightLevel = isFlipped ? podA : podB;
        bool isRightCharging = isFlipped ? podACharging : podBCharging;

        var now = DateTime.Now;

        // Smoothing Cache:
        // When Apple momentarily reports 0x0F (15 = unknown / N/A / not in ear),
        // preserve last known valid levels within 60 seconds
        if (!leftLevel.HasValue && existing?.LeftBatteryLevel != null && (now - existing.LastUpdated).TotalSeconds < 60)
        {
            leftLevel = existing.LeftBatteryLevel;
            isLeftCharging = existing.IsLeftCharging;
        }

        if (!rightLevel.HasValue && existing?.RightBatteryLevel != null && (now - existing.LastUpdated).TotalSeconds < 60)
        {
            rightLevel = existing.RightBatteryLevel;
            isRightCharging = existing.IsRightCharging;
        }

        if (!caseLevel.HasValue && existing?.CaseBatteryLevel != null && (now - existing.LastUpdated).TotalSeconds < 60)
        {
            caseLevel = existing.CaseBatteryLevel;
            caseCharging = existing.IsCaseCharging;
        }

        // Anti-Spike / Anomaly Filter:
        // Physically a lithium battery cannot drop from 100% to 10% or 30% within 10 seconds.
        // If previous level >= 60 and new level abruptly drops to <= 35 (while earbud is not charging),
        // preserve previous stable level to filter transient packet glitches.
        if (existing != null && (now - existing.LastUpdated).TotalSeconds < 15)
        {
            if (existing.LeftBatteryLevel.HasValue && existing.LeftBatteryLevel.Value >= 60 && leftLevel.HasValue && leftLevel.Value <= 35 && !isLeftCharging)
            {
                leftLevel = existing.LeftBatteryLevel;
            }

            if (existing.RightBatteryLevel.HasValue && existing.RightBatteryLevel.Value >= 60 && rightLevel.HasValue && rightLevel.Value <= 35 && !isRightCharging)
            {
                rightLevel = existing.RightBatteryLevel;
            }
        }

        if (!leftLevel.HasValue && !rightLevel.HasValue && !caseLevel.HasValue)
        {
            return null;
        }

        // Use earbuds battery as main level (case battery must not override earbuds level)
        int? mainLevel = null;
        if (leftLevel.HasValue && rightLevel.HasValue)
        {
            mainLevel = Math.Min(leftLevel.Value, rightLevel.Value);
        }
        else
        {
            mainLevel = leftLevel ?? rightLevel ?? caseLevel;
        }

        string macString = address.ToString("X12");
        string formattedMac = string.Join(":", Enumerable.Range(0, 6).Select(i => macString.Substring(i * 2, 2)));

        return new BluetoothDeviceModel
        {
            Id = $"AirPods_{formattedMac}",
            Name = modelName,
            ModelName = modelName,
            DeviceType = modelName.Contains("Max") || modelName.Contains("Solo") || modelName.Contains("Studio") ? DeviceType.Headphones : DeviceType.Earbuds,
            BluetoothAddress = address,
            BatteryLevel = mainLevel,
            IsCharging = isLeftCharging || isRightCharging,
            IsConnected = false,
            IsTws = true,
            LeftBatteryLevel = leftLevel,
            IsLeftCharging = isLeftCharging,
            RightBatteryLevel = rightLevel,
            IsRightCharging = isRightCharging,
            CaseBatteryLevel = caseLevel,
            IsCaseCharging = caseCharging,
            ProviderSource = "Apple Beacon (BLE)",
            LastUpdated = DateTime.Now
        };
    }

    private static int? ConvertNibbleToPercent(int nibble)
    {
        if (nibble >= 0 && nibble <= 10)
        {
            return nibble * 10;
        }
        return null;
    }

    public static bool IsKnownAirPodsModel(ushort modelId) => modelId switch
    {
        0x0220 or 0x2002 or 0x0200 or 0x0002 => true,
        0x0F20 or 0x200F or 0x0F00 or 0x000F => true,
        0x1320 or 0x2013 or 0x1300 or 0x0013 => true,
        0x1B20 or 0x201B or 0x1B00 or 0x001B => true,
        0x0E20 or 0x200E or 0x0E00 or 0x000E => true,
        0x1420 or 0x2014 or 0x1400 or 0x0014 => true,
        0x2420 or 0x2024 or 0x2400 or 0x0024 => true,
        0x0A20 or 0x200A or 0x0A00 or 0x000A => true,
        0x0320 or 0x2003 => true,
        0x0520 or 0x2005 => true,
        0x0B20 or 0x200B => true,
        0x0C20 or 0x200C => true,
        0x1020 or 0x2010 => true,
        0x1120 or 0x2011 => true,
        0x1220 or 0x2012 => true,
        0x1720 or 0x2017 => true,
        _ => false
    };

    public static string GetModelName(ushort modelId) => modelId switch
    {
        0x0220 or 0x2002 or 0x0200 or 0x0002 => "AirPods (1. Nesil)",
        0x0F20 or 0x200F or 0x0F00 or 0x000F => "AirPods (2. Nesil)",
        0x1320 or 0x2013 or 0x1300 or 0x0013 => "AirPods (3. Nesil)",
        0x1B20 or 0x201B or 0x1B00 or 0x001B => "AirPods (4. Nesil)",
        0x0E20 or 0x200E or 0x0E00 or 0x000E => "AirPods Pro",
        0x1420 or 0x2014 or 0x1400 or 0x0014 => "AirPods Pro (2. Nesil)",
        0x2420 or 0x2024 or 0x2400 or 0x0024 => "AirPods Pro (2. Nesil, USB-C)",
        0x0A20 or 0x200A or 0x0A00 or 0x000A => "AirPods Max",
        0x0320 or 0x2003 => "Powerbeats 3",
        0x0520 or 0x2005 => "Beats Studio3",
        0x0B20 or 0x200B => "Powerbeats Pro",
        0x0C20 or 0x200C => "Beats Solo Pro",
        0x1020 or 0x2010 => "Beats Flex",
        0x1120 or 0x2011 => "Beats Studio Buds",
        0x1220 or 0x2012 => "Beats Fit Pro",
        0x1720 or 0x2017 => "Beats Studio Pro",
        _ => "Apple AirPods / Beats"
    };

    public void Dispose()
    {
        StopMonitoring();
        _watcher.Received -= OnAdvertisementReceived;
    }
}
