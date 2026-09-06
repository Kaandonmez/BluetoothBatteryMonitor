using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;
using Windows.Devices.Bluetooth.Advertisement;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Çözümlenen AirPods / Beats beacon verileri.
/// </summary>
public record AirPodsBatteryData(
    ushort ModelId,
    string ModelName,
    int? LeftLevel,
    bool IsLeftCharging,
    int? RightLevel,
    bool IsRightCharging,
    int? CaseLevel,
    bool IsCaseCharging);

/// <summary>
/// BluetoothLEAdvertisementWatcher ile Apple üretici verilerini (0x004C) arka planda
/// koklayarak AirPods, AirPods Pro, AirPods Max ve Beats cihazlarının Sol, Sağ ve Şarj Kutusu
/// pil yüzdelerini ve şarj durumlarını gerçek zamanlı ayrıştıran servis.
/// </summary>
public class AppleAirPodsBeaconProvider
{
    public const ushort AppleCompanyId = 0x004C;

    private BluetoothLEAdvertisementWatcher? _watcher;
    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;

    public IReadOnlyCollection<BluetoothDeviceModel> Devices => _devices.Values.ToList();

    public Task StartAsync()
    {
        try
        {
            if (_watcher == null)
            {
                _watcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };

                // Apple Company ID filtresi ekle (böylece ilgisiz paketler işlemciyi yormaz)
                var manufacturerFilter = new BluetoothLEManufacturerData
                {
                    CompanyId = AppleCompanyId
                };
                _watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerFilter);

                _watcher.Received += OnAdvertisementReceived;
            }

            _watcher.Start();
            Debug.WriteLine("[AppleAirPodsBeaconProvider] BLE Advertisement Watcher başlatıldı.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppleAirPodsBeaconProvider] StartAsync hatası: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task RefreshAsync()
    {
        // BLE Advertisement Watcher arka planda sürekli dinlemede kalır
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.Stop();
                _watcher.Received -= OnAdvertisementReceived;
                _watcher = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppleAirPodsBeaconProvider] StopAsync hatası: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var mfg in args.Advertisement.ManufacturerData)
        {
            if (mfg.CompanyId != AppleCompanyId || mfg.Data == null || mfg.Data.Length < 7)
            {
                continue;
            }

            byte[] data = new byte[mfg.Data.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(mfg.Data))
            {
                reader.ReadBytes(data);
            }
            if (TryParseAirPodsAdvertisement(data, out var parsed) && parsed != null)
            {
                string deviceId = $"AirPods_{args.BluetoothAddress:X12}";

                bool isOverEar = parsed.ModelName.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
                                 parsed.ModelName.Contains("Studio", StringComparison.OrdinalIgnoreCase) ||
                                 parsed.ModelName.Contains("Solo", StringComparison.OrdinalIgnoreCase);

                var model = _devices.GetOrAdd(deviceId, id => new BluetoothDeviceModel
                {
                    Id = id,
                    Name = parsed.ModelName,
                    Type = isOverEar ? DeviceType.Headphones : DeviceType.Earbuds,
                    BluetoothAddress = args.BluetoothAddress,
                    ProviderSource = "Apple AirPods Beacon"
                });

                model.IsConnected = false;
                model.LastSeen = DateTime.Now;

                // Pil bilgilerini aktar
                if (isOverEar)
                {
                    model.Battery.HasMultipleBatteries = false;
                    model.Battery.Level = parsed.LeftLevel ?? parsed.RightLevel ?? parsed.CaseLevel;
                    model.Battery.IsCharging = parsed.IsLeftCharging || parsed.IsRightCharging;
                }
                else
                {
                    model.Battery.HasMultipleBatteries = true;
                    model.Battery.LeftLevel = parsed.LeftLevel;
                    model.Battery.IsLeftCharging = parsed.IsLeftCharging;
                    model.Battery.RightLevel = parsed.RightLevel;
                    model.Battery.IsRightCharging = parsed.IsRightCharging;
                    model.Battery.CaseLevel = parsed.CaseLevel;
                    model.Battery.IsCaseCharging = parsed.IsCaseCharging;
                    model.Battery.IsCharging = parsed.IsLeftCharging || parsed.IsRightCharging || parsed.IsCaseCharging;
                    model.Battery.Level = model.Battery.EffectiveLowestLevel;
                }
                model.Battery.LastUpdated = DateTime.Now;

                DeviceUpdated?.Invoke(this, model);
            }
        }
    }

    /// <summary>
    /// Apple BLE üretici verisi bayt dizisini çözümler.
    /// Tip 0x07 (Proximity Pairing) ve 0x10 paketlerini destekler.
    /// </summary>
    public static bool TryParseAirPodsAdvertisement(byte[] data, out AirPodsBatteryData? result)
    {
        result = null;
        if (data == null || data.Length < 7)
        {
            return false;
        }

        byte packetType = data[0];

        // Tip 0x07: Apple Proximity Pairing (AirPods durum yayını)
        if (packetType == 0x07 && data.Length >= 9)
        {
            ushort modelId = (ushort)((data[2] << 8) | data[3]);
            string modelName = GetModelName(modelId);

            // data[6]: Pil nibble'ları (Üst: Sol/Sağ, Alt: Sağ/Sol)
            int nibble1 = (data[6] >> 4) & 0x0F;
            int nibble2 = data[6] & 0x0F;

            // data[7]: Üst nibble = Şarj durum bitleri, Alt nibble = Kutu pili
            int chargingBits = (data[7] >> 4) & 0x0F;
            int caseNibble = data[7] & 0x0F;

            // data[5] veya data[8] baytındaki flip (ters çevirme) biti
            bool isFlipped = data.Length > 8 && ((data[8] & 0x20) != 0 || (data[5] & 0x02) != 0);

            // 0-10 arası değerler %0-%100 (x10) temsil eder. 15 (0x0F) bağlantısız / kulakta değil demektir.
            int? podA = ConvertNibbleToPercent(nibble1);
            int? podB = ConvertNibbleToPercent(nibble2);
            int? caseBattery = ConvertNibbleToPercent(caseNibble);

            bool podACharging = (chargingBits & 0x01) != 0;
            bool podBCharging = (chargingBits & 0x02) != 0;
            bool caseCharging = (chargingBits & 0x04) != 0;

            int? leftLevel = isFlipped ? podB : podA;
            bool isLeftCharging = isFlipped ? podBCharging : podACharging;

            int? rightLevel = isFlipped ? podA : podB;
            bool isRightCharging = isFlipped ? podACharging : podBCharging;

            result = new AirPodsBatteryData(
                ModelId: modelId,
                ModelName: modelName,
                LeftLevel: leftLevel,
                IsLeftCharging: isLeftCharging,
                RightLevel: rightLevel,
                IsRightCharging: isRightCharging,
                CaseLevel: caseBattery,
                IsCaseCharging: caseCharging
            );

            return true;
        }

        // Tip 0x10: Nearby Action/Info paketi
        if (packetType == 0x10 && data.Length >= 5)
        {
            // Genel durum bildirimi
            result = new AirPodsBatteryData(
                ModelId: 0,
                ModelName: "Apple Cihazı",
                LeftLevel: null,
                IsLeftCharging: false,
                RightLevel: null,
                IsRightCharging: false,
                CaseLevel: null,
                IsCaseCharging: false
            );
            return false;
        }

        return false;
    }

    private static int? ConvertNibbleToPercent(int nibble)
    {
        if (nibble >= 0 && nibble <= 10)
        {
            return nibble * 10;
        }
        return null; // 15 veya geçersizse devre dışı
    }

    public static string GetModelName(ushort modelId) => modelId switch
    {
        0x0220 or 0x2002 => "AirPods (1. Nesil)",
        0x0F20 or 0x200F => "AirPods (2. Nesil)",
        0x1320 or 0x2013 => "AirPods (3. Nesil)",
        0x1B20 or 0x201B => "AirPods (4. Nesil)",
        0x0E20 or 0x200E => "AirPods Pro (1. Nesil)",
        0x1420 or 0x2014 => "AirPods Pro (2. Nesil)",
        0x2420 or 0x2024 => "AirPods Pro (2. Nesil, USB-C)",
        0x0A20 or 0x200A => "AirPods Max",
        0x0520 or 0x2005 => "Beats Studio3",
        0x0B20 or 0x200B => "Powerbeats Pro",
        0x0C20 or 0x200C => "Beats Solo Pro",
        0x1020 or 0x2010 => "Beats Flex",
        0x1120 or 0x2011 => "Beats Studio Buds",
        0x1220 or 0x2012 => "Beats Fit Pro",
        0x1720 or 0x2017 => "Beats Studio Pro",
        _ => "Apple AirPods"
    };
}
