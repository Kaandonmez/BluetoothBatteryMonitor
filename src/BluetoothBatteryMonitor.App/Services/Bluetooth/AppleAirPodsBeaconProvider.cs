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
        // Kapağı kapatılmış veya sinyali kesilmiş eski AirPods beacon'larını temizle
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
            // Bluetooth kapalı veya servis kullanılamıyor
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

                // SADECE ve SADECE Proximity Pairing (0x07) mesajları AirPods pil telemetrisi taşır.
                // 0x10 (Nearby Info) ve 0x05 (AirDrop) gibi Continuity mesajları pil taşımaz;
                // aksine bayt 5'te rastgele iOS aktivite bayrakları (örn: 0x13 -> %10 ve %30) taşıyarak
                // kulaklık pilinin %10 ve %30'a çökmesine neden olur!
                byte packetType = data[0];
                if (packetType != 0x07) continue;

                string macString = args.BluetoothAddress.ToString("X12");
                string formattedMac = string.Join(":", Enumerable.Range(0, 6).Select(i => macString.Substring(i * 2, 2)));
                string candidateId = $"AirPods_{formattedMac}";
                _detectedAirPods.TryGetValue(candidateId, out var existing);

                // Eğer MAC adresi BLE RPA rotasyonu nedeniyle değiştiyse son 60 sn içindeki aktif AirPods'u smoothing için kullan
                if (existing == null && _detectedAirPods.Count > 0)
                {
                    existing = _detectedAirPods.Values
                        .Where(d => (DateTime.Now - d.LastUpdated).TotalSeconds < 60)
                        .OrderByDescending(d => d.LastUpdated)
                        .FirstOrDefault();
                }

                // AirPods modelini ve pillerini ayrıştır
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
            // Ayrıştırma veya paket hatası yutulur
        }
    }

    public static BluetoothDeviceModel? ParseAirPodsData(ulong address, byte[] data, BluetoothDeviceModel? existing = null)
    {
        // En az 9 bayt olmalı ve Tip 0x07 (Proximity Pairing) olmalı
        if (data.Length < 9 || data[0] != 0x07) return null;

        // Model tespiti:
        // Gerçek Apple paketlerinde data[2] = 0x01 (prefix) olup Model ID data[3] ve data[4]'tedir.
        // Bazı klon/test paketlerinde ise data[2] ve data[3]'tedir.
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

        // Standart AirPods Proximity indeksleri:
        // Tip 0x07 (Proximity): offset = 6 (data[6]: Pod A / Pod B pilleri)
        // data[7]: Şarj bayrakları ve Kutu pili
        const int offset = 6;
        if (offset + 1 >= data.Length) return null;

        // data[offset]: Üst nibble = Pod A, Alt nibble = Pod B
        int nibble1 = (data[offset] >> 4) & 0x0F;
        int nibble2 = data[offset] & 0x0F;

        // data[offset + 1]: ÜST nibble = Şarj durum bitleri, ALT nibble = Kutu pili
        int chargingBits = (data[offset + 1] >> 4) & 0x0F;
        int caseNibble = data[offset + 1] & 0x0F;

        // data[5] Status baytı (Bit 5 = 0x20: isFlipped) veya data[8]
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

        // Yumuşatma (Smoothing Cache):
        // Apple anlık olarak 0x0F (15 = bilinmiyor / N/A / kulakta değil) raporladığında
        // son 60 saniye içindeki son geçerli seviyeleri koru
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

        // Anti-Spike / Anomali Filtresi:
        // Fiziksel olarak bir lityum pil 10 saniye içinde %100'den %10 veya %30'a düşemez.
        // Eğer önceki seviye >= 60 iken yeni seviye aniden <= 35'e düşüyorsa (ve kulaklık şarjda değilse),
        // geçici paket parazitlerini önlemek için önceki kararlı seviyeyi koru.
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

        // Ana pil seviyesi olarak kulaklıkların kendisini seç (kutu pili kulaklıkların ana pilini ezmemeli)
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
