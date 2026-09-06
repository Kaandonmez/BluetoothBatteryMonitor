using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Audio;
using BluetoothBatteryMonitor.App.Services.Notification;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public class BluetoothBatteryEngine : IDisposable
{
    private readonly List<IBluetoothBatteryProvider> _providers = new();
    private readonly ToastNotificationService _notificationService;
    private readonly IBluetoothAudioCodecDetector _codecDetector;
    private readonly IAudioEndpointManager _audioManager;
    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    private Timer? _pollingTimer;
    private bool _isSessionLocked;
    private bool _isDisposed;

    public event EventHandler<IReadOnlyList<BluetoothDeviceModel>>? DevicesUpdated;
    public event EventHandler<string>? StatusChanged;

    public IReadOnlyList<BluetoothDeviceModel> CurrentDevices
    {
        get
        {
            lock (_deviceLockObj)
            {
                return AggregateDevices(_devices.Values)
                    .OrderByDescending(d => d.IsConnected)
                    .ThenByDescending(d => GetDeviceTypePriority(d.Type))
                    .ThenBy(d => d.Name)
                    .ToList();
            }
        }
    }
    public IReadOnlyList<IBluetoothBatteryProvider> Providers => _providers;

    public BluetoothBatteryEngine(ToastNotificationService notificationService, IBluetoothAudioCodecDetector? codecDetector = null, IAudioEndpointManager? audioManager = null)
    {
        _notificationService = notificationService;
        _codecDetector = codecDetector ?? BluetoothAudioCodecDetector.Default;
        _audioManager = audioManager ?? new AudioEndpointManager();

        // Sağlayıcıları kaydet
        _providers.Add(new BleGattBatteryProvider());
        _providers.Add(new AppleAirPodsBeaconProvider());
        _providers.Add(new WindowsPnpBatteryProvider());
        _providers.Add(new LogitechHidBatteryProvider());
        _providers.Add(new LogitechGHubBatteryProvider());
        _providers.Add(new PlayStationControllerBatteryProvider());
        _providers.Add(new SamsungGalaxyBudsBatteryProvider());
        _providers.Add(new SonyHeadphonesBatteryProvider());
        _providers.Add(new GoogleFastPairBatteryProvider());
        _providers.Add(new NintendoSwitchBatteryProvider());
        _providers.Add(new SteelSeriesBatteryProvider());

        foreach (var provider in _providers)
        {
            provider.DeviceUpdated += OnProviderDeviceUpdated;
        }

        // Windows oturum kilitlenme / açılma olaylarını dinle (Enerji tasarrufu)
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Start()
    {
        foreach (var provider in _providers)
        {
            provider.StartMonitoring();
        }

        // İlk taramayı başlat
        _ = RefreshAllDevicesAsync();

        // Akıllı zamanlayıcıyı kur
        ScheduleNextPoll(TimeSpan.FromSeconds(10));
    }

    public async Task RefreshAllDevicesAsync()
    {
        if (_isSessionLocked || _isDisposed) return;
        if (!await _scanLock.WaitAsync(100)) return; // Çakışan taramaları engelle

        try
        {
            StatusChanged?.Invoke(this, "Bluetooth cihazları taranıyor...");

            var connectionSnapshot = await BluetoothConnectionChecker.CaptureSnapshotAsync(_audioManager);

            // Mevcut önbellekteki cihazların bağlantı durumunu hemen canlı anlık görüntü ile güncelle
            BluetoothConnectionChecker.UpdateConnectionStatuses(_devices.Values, connectionSnapshot);

            var scanTasks = _providers.Select(async p =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    return await p.GetDevicesAsync(cts.Token);
                }
                catch
                {
                    return (IReadOnlyList<BluetoothDeviceModel>)Array.Empty<BluetoothDeviceModel>();
                }
            });

            var results = await Task.WhenAll(scanTasks);
            var settings = AppSettings.Load();

            foreach (var devList in results)
            {
                // Sağlayıcıdan gelen cihazların durumunu da derhal snapshot ile doğrula
                BluetoothConnectionChecker.UpdateConnectionStatuses(devList, connectionSnapshot);

                foreach (var dev in devList)
                {
                    MergeOrAddDevice(dev);
                }
            }

            // Bağlantısı kopmuş / kapağı kapatılmış beacon veya eski cihazların durumunu güncelle
            var now = DateTime.Now;
            foreach (var kvp in _devices)
            {
                // Apple AirPods / Beats ve Google Fast Pair beacon'ları kutu kapandığında yayın yapmayı keser.
                // Yalnızca bağlı OLMAYAN beacon'lar için: 30 saniye boyunca yeni paket gelmediyse cihazı ekrandan düşür.
                if (!kvp.Value.IsConnected && kvp.Value.IsTws && (kvp.Value.ProviderSource.Contains("Apple Beacon") || kvp.Value.ProviderSource.Contains("Fast Pair")))
                {
                    if (now - kvp.Value.LastUpdated > TimeSpan.FromSeconds(30))
                    {
                        _devices.TryRemove(kvp.Key, out _);
                    }
                }
                else if (now - kvp.Value.LastUpdated > TimeSpan.FromMinutes(10) && !kvp.Value.IsConnected)
                {
                    _devices.TryRemove(kvp.Key, out _);
                }
            }

            // Çift modlu (Classic + BLE GATT) cihazları kümele
            AggregateDualModeDevices();

            // Tüm cihazların canlı bağlantı durumunu kesinleştir
            BluetoothConnectionChecker.UpdateConnectionStatuses(_devices.Values, connectionSnapshot);

            // Kodek tespiti ve bildirim kontrolünü SADECE kesin teyit edilmiş cihazlar üzerinde yap
            foreach (var activeDev in CurrentDevices)
            {
                if (activeDev.IsConnected && (activeDev.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker))
                {
                    activeDev.AudioCodec = _codecDetector.DetectCodec(activeDev);
                }
                _notificationService.CheckAndNotifyBattery(activeDev, settings);
            }

            StatusChanged?.Invoke(this, $"{_devices.Count} cihaz bulundu.");
            NotifyDevicesUpdated();
        }
        finally
        {
            _scanLock.Release();

            // Bir sonraki yoklamayı akıllıca planla
            int interval = AppSettings.Load().RefreshIntervalSeconds;
            if (_devices.IsEmpty)
            {
                interval = Math.Max(interval, 300); // Cihaz yokken 5 dakika
            }
            ScheduleNextPoll(TimeSpan.FromSeconds(interval));
        }
    }

    private async void OnProviderDeviceUpdated(object? sender, BluetoothDeviceModel dev)
    {
        // Gelen canlı olayın gerçek bağlantı durumunu doğrula
        dev.IsConnected = await BluetoothConnectionChecker.IsDeviceConnectedAsync(dev);

        var activeDev = MergeOrAddDevice(dev);
        if (activeDev.IsConnected && (activeDev.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker))
        {
            activeDev.AudioCodec = _codecDetector.DetectCodec(activeDev);
        }
        var settings = AppSettings.Load();
        _notificationService.CheckAndNotifyBattery(activeDev, settings);
        NotifyDevicesUpdated();
    }

    private readonly object _deviceLockObj = new();

    public void AggregateDualModeDevices()
    {
        lock (_deviceLockObj)
        {
            var list = _devices.Values.ToList();
            var aggregated = AggregateDevices(list);

            _devices.Clear();
            foreach (var dev in aggregated)
            {
                _devices[dev.Id] = dev;
            }
        }
    }

    public static List<BluetoothDeviceModel> AggregateDevices(IEnumerable<BluetoothDeviceModel> rawDevices)
    {
        if (rawDevices == null) return new List<BluetoothDeviceModel>();

        var result = new List<BluetoothDeviceModel>();

        foreach (var device in rawDevices)
        {
            if (device == null) continue;

            var existing = result.FirstOrDefault(d => d.Matches(device) || d.Id.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                result.Add(device);
            }
            else
            {
                MergeIntoBestMaster(result, existing, device);
            }
        }

        // İkincil geçiş: Çapraz / geçişli (transitive) eşleşmeleri birleştirerek tam tekillik garantisi sağla
        bool mergedAny;
        do
        {
            mergedAny = false;
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    if (result[i].Matches(result[j]) || result[i].Id.Equals(result[j].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        var keep = result[i];
                        var remove = result[j];
                        result.RemoveAt(j);
                        MergeIntoBestMaster(result, keep, remove);
                        mergedAny = true;
                        break;
                    }
                }
                if (mergedAny) break;
            }
        } while (mergedAny);

        return result;
    }

    private static void MergeIntoBestMaster(List<BluetoothDeviceModel> result, BluetoothDeviceModel existing, BluetoothDeviceModel incoming)
    {
        bool incomingIsPreferred = IsPreferredMaster(incoming, existing);

        if (incomingIsPreferred)
        {
            int index = result.IndexOf(existing);
            MergeDualModePair(incoming, existing);
            if (index >= 0)
            {
                result[index] = incoming;
            }
            else
            {
                result.Add(incoming);
            }
        }
        else
        {
            MergeDualModePair(existing, incoming);
        }
    }

    public static bool IsPreferredMaster(BluetoothDeviceModel candidate, BluetoothDeviceModel current)
    {
        if (candidate == null) return false;
        if (current == null) return true;

        bool candidateIsAudio = candidate.DeviceType is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker;
        bool currentIsAudio = current.DeviceType is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker;

        // 1. Ses aygıtı (Headphones/Earbuds/Speaker) ses yönlendirme ve kodek için her zaman genel düğüme tercih edilir
        if (candidateIsAudio && !currentIsAudio) return true;
        if (!candidateIsAudio && currentIsAudio) return false;

        // 2. Cihaz tipi önceliği (Örn: Phone, Gamepad > Generic)
        int candPriority = GetDeviceTypePriority(candidate.DeviceType);
        int currPriority = GetDeviceTypePriority(current.DeviceType);
        if (candPriority > currPriority) return true;
        if (candPriority < currPriority) return false;

        // 3. Windows Audio Endpoint uyumu için Klasik BTHENUM düğümü BLE düğümüne tercih edilir
        bool candidateIsBtEnum = (candidate.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
        bool currentIsBtEnum = (current.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
        if (candidateIsBtEnum && !currentIsBtEnum) return true;
        if (!candidateIsBtEnum && currentIsBtEnum) return false;

        // 4. Model adı zenginliği
        bool candidateHasModel = !string.IsNullOrWhiteSpace(candidate.ModelName);
        bool currentHasModel = !string.IsNullOrWhiteSpace(current.ModelName);
        if (candidateHasModel && !currentHasModel) return true;
        if (!candidateHasModel && currentHasModel) return false;

        // 5. İsim temizliği (LE prefix'i olmayan temiz isim tercih edilir)
        bool candidateHasLe = candidate.Name.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
                              candidate.Name.StartsWith("LE-", StringComparison.OrdinalIgnoreCase) ||
                              candidate.Name.StartsWith("LE ", StringComparison.OrdinalIgnoreCase) ||
                              candidate.Name.EndsWith(" LE", StringComparison.OrdinalIgnoreCase) ||
                              candidate.Name.EndsWith("-LE", StringComparison.OrdinalIgnoreCase) ||
                              candidate.Name.EndsWith("_LE", StringComparison.OrdinalIgnoreCase);
        bool currentHasLe = current.Name.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
                            current.Name.StartsWith("LE-", StringComparison.OrdinalIgnoreCase) ||
                            current.Name.StartsWith("LE ", StringComparison.OrdinalIgnoreCase) ||
                            current.Name.EndsWith(" LE", StringComparison.OrdinalIgnoreCase) ||
                            current.Name.EndsWith("-LE", StringComparison.OrdinalIgnoreCase) ||
                            current.Name.EndsWith("_LE", StringComparison.OrdinalIgnoreCase);
        if (!candidateHasLe && currentHasLe) return true;
        if (candidateHasLe && !currentHasLe) return false;

        return false;
    }

    public static int GetDeviceTypePriority(DeviceType type) => type switch
    {
        DeviceType.Headphones => 10,
        DeviceType.Earbuds => 10,
        DeviceType.Speaker => 9,
        DeviceType.Phone => 8,
        DeviceType.Gamepad => 7,
        DeviceType.Mouse => 6,
        DeviceType.Keyboard => 6,
        DeviceType.Watch => 5,
        DeviceType.Stylus => 4,
        DeviceType.Generic => 1,
        _ => 0
    };

    public static void MergeDualModePair(BluetoothDeviceModel master, BluetoothDeviceModel secondary)
    {
        if (master == null || secondary == null) return;

        bool secondaryHasLe = secondary.Name.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
                              secondary.Name.StartsWith("LE-", StringComparison.OrdinalIgnoreCase) ||
                              secondary.Name.StartsWith("LE ", StringComparison.OrdinalIgnoreCase) ||
                              secondary.Name.EndsWith(" LE", StringComparison.OrdinalIgnoreCase) ||
                              secondary.Name.EndsWith("-LE", StringComparison.OrdinalIgnoreCase) ||
                              secondary.Name.EndsWith("_LE", StringComparison.OrdinalIgnoreCase);

        bool masterHasLe = master.Name.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
                           master.Name.StartsWith("LE-", StringComparison.OrdinalIgnoreCase) ||
                           master.Name.StartsWith("LE ", StringComparison.OrdinalIgnoreCase) ||
                           master.Name.EndsWith(" LE", StringComparison.OrdinalIgnoreCase) ||
                           master.Name.EndsWith("-LE", StringComparison.OrdinalIgnoreCase) ||
                           master.Name.EndsWith("_LE", StringComparison.OrdinalIgnoreCase);

        // İsim önceliği: Özel veya daha detaylı adı koru (Teknik LE prefixlerini temiz ada tercih etme)
        if (!BluetoothDeviceModel.IsGenericName(secondary.Name))
        {
            if (BluetoothDeviceModel.IsGenericName(master.Name))
            {
                master.Name = secondaryHasLe ? BluetoothDeviceModel.CleanNameForComparison(secondary.Name) : secondary.Name;
            }
            else if (masterHasLe && !secondaryHasLe)
            {
                master.Name = secondary.Name;
            }
            else if (!secondaryHasLe && secondary.Name.Length > master.Name.Length && !master.Name.Contains("AirPods Pro (", StringComparison.OrdinalIgnoreCase))
            {
                master.Name = secondary.Name;
            }
        }

        if (!string.IsNullOrWhiteSpace(secondary.ModelName) && string.IsNullOrWhiteSpace(master.ModelName))
        {
            master.ModelName = secondary.ModelName;
        }

        // Cihaz türü: Genel türü daha spesifik türle güncelle
        if (GetDeviceTypePriority(secondary.DeviceType) > GetDeviceTypePriority(master.DeviceType))
        {
            master.DeviceType = secondary.DeviceType;
        }

        // MAC adresi aktarımı (Dual-mode BR/EDR ve BLE adreslerini koru)
        if (master.BluetoothAddress == 0 && secondary.BluetoothAddress != 0)
        {
            master.BluetoothAddress = secondary.BluetoothAddress;
        }
        else if (secondary.BluetoothAddress != 0 && (secondary.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) && !(master.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            if (master.BluetoothAddress != 0 && master.BluetoothAddress != secondary.BluetoothAddress)
            {
                master.SecondaryBluetoothAddress = master.BluetoothAddress;
            }
            master.BluetoothAddress = secondary.BluetoothAddress;
        }
        else if (secondary.BluetoothAddress != 0 && secondary.BluetoothAddress != master.BluetoothAddress)
        {
            master.SecondaryBluetoothAddress = secondary.BluetoothAddress;
        }

        if (secondary.SecondaryBluetoothAddress != 0 && secondary.SecondaryBluetoothAddress != master.BluetoothAddress)
        {
            master.SecondaryBluetoothAddress = secondary.SecondaryBluetoothAddress;
        }

        // Pil verileri:
        // 1. TWS cihazı ise (Sol, Sağ, Kutu) parçalarını eksiksiz aktar
        if (secondary.IsTws)
        {
            master.IsTws = true;
            if (secondary.LeftBatteryLevel.HasValue) master.LeftBatteryLevel = secondary.LeftBatteryLevel;
            if (secondary.RightBatteryLevel.HasValue) master.RightBatteryLevel = secondary.RightBatteryLevel;
            if (secondary.CaseBatteryLevel.HasValue) master.CaseBatteryLevel = secondary.CaseBatteryLevel;
            master.IsLeftCharging = secondary.IsLeftCharging || master.IsLeftCharging;
            master.IsRightCharging = secondary.IsRightCharging || master.IsRightCharging;
            master.IsCaseCharging = secondary.IsCaseCharging || master.IsCaseCharging;
            master.BatteryLevel = master.EffectiveBatteryLevel;
            master.IsCharging = master.IsLeftCharging || master.IsRightCharging || master.IsCaseCharging || secondary.IsCharging || master.IsCharging;
        }
        else
        {
            // 2. Standart pil seviyesi:
            // BLE GATT veya PnP BLE yüksek çözünürlüklü (%1 adım) verisi, kaba (%10 dilimli) Klasik HFP verisine her zaman önceliklidir!
            bool masterIsHfp = IsHfpSource(master);
            bool secondaryIsHfp = IsHfpSource(secondary);

            if (secondary.BatteryLevel.HasValue)
            {
                if (!master.BatteryLevel.HasValue)
                {
                    master.BatteryLevel = secondary.BatteryLevel;
                    master.IsCharging = secondary.IsCharging;
                }
                else if (masterIsHfp && !secondaryIsHfp)
                {
                    // BLE GATT veya PnP BLE verisi HFP'ye önceliklidir
                    master.BatteryLevel = secondary.BatteryLevel;
                    master.IsCharging = secondary.IsCharging;
                }
                else if (!masterIsHfp && secondaryIsHfp)
                {
                    // Master zaten yüksek çözünürlüklü veriye sahip; kaba HFP ile ezme!
                    master.IsCharging = master.IsCharging || secondary.IsCharging;
                }
                else
                {
                    // İkisi de aynı sınıfta (ikisi de HFP veya ikisi de BLE/PnP):
                    // Yeni bir telemetri geldiyse (secondary.LastUpdated > master.LastUpdated), daima güncelle!
                    // Eşzamanlı/başlangıç taramasında (aynı zaman damgası) ise yüksek çözünürlüklü olan (%1 hassasiyet, 10'un tam katı olmayan) tercih edilir.
                    bool masterIsStep10 = master.BatteryLevel.Value % 10 == 0;
                    bool secondaryIsStep10 = secondary.BatteryLevel.Value % 10 == 0;

                    if (secondary.LastUpdated > master.LastUpdated)
                    {
                        master.BatteryLevel = secondary.BatteryLevel;
                        master.IsCharging = secondary.IsCharging;
                    }
                    else if (masterIsStep10 && !secondaryIsStep10)
                    {
                        master.BatteryLevel = secondary.BatteryLevel;
                        master.IsCharging = secondary.IsCharging;
                    }
                    else
                    {
                        master.IsCharging = master.IsCharging || secondary.IsCharging;
                    }
                }
            }
        }

        // Bağlantı durumu: Herhangi biri bağlıysa birleşik cihaz bağlıdır
        master.IsConnected = master.IsConnected || secondary.IsConnected;

        // Ses kodeki aktarımı (Yüksek kaliteli veya tespit edilmiş spesifik kodeki önceliklendir)
        if (!string.IsNullOrEmpty(secondary.AudioCodec))
        {
            if (string.IsNullOrEmpty(master.AudioCodec) || (master.AudioCodec == "SBC" && secondary.AudioCodec != "SBC"))
            {
                master.AudioCodec = secondary.AudioCodec;
            }
        }
        else if (!string.IsNullOrEmpty(master.AudioCodec))
        {
            secondary.AudioCodec = master.AudioCodec;
        }

        // Çift modlu kümelendiğini işaretle
        master.IsAggregated = true;
        secondary.IsAggregated = true;

        // Sağlayıcı kaynağını şık bir şekilde birleştir (örn: "Windows PnP / BLE GATT")
        master.ProviderSource = MergeProviderSources(master.ProviderSource, secondary.ProviderSource);
        master.LastUpdated = master.LastUpdated > secondary.LastUpdated ? master.LastUpdated : secondary.LastUpdated;
    }

    public static bool IsHfpSource(BluetoothDeviceModel dev)
    {
        if (dev == null) return false;
        string src = dev.ProviderSource ?? string.Empty;
        string id = dev.Id ?? string.Empty;

        if (src.Contains("GATT", StringComparison.OrdinalIgnoreCase)) return false;
        if (src.Contains("HFP", StringComparison.OrdinalIgnoreCase)) return true;
        if (src.Contains("AVRCP", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) && !src.Contains("GATT", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    public static string MergeProviderSources(string? source1, string? source2)
    {
        if (string.IsNullOrWhiteSpace(source1) || source1.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
            return source2 ?? "Bilinmiyor";
        if (string.IsNullOrWhiteSpace(source2) || source2.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
            return source1;

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFrom(string src)
        {
            var raw = src.Split(new[] { " + ", " / ", " • ", " (Canlı)" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var r in raw)
            {
                if (string.IsNullOrWhiteSpace(r) || r.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase)) continue;
                tokens.Add(r);
            }
        }

        AddFrom(source1);
        AddFrom(source2);

        var ordered = new List<string>();
        bool hasBleGatt = tokens.Contains("BLE GATT");
        if (hasBleGatt)
        {
            tokens.Remove("HFP");
        }

        if (tokens.Contains("Windows PnP")) ordered.Add("Windows PnP");
        if (hasBleGatt) ordered.Add("BLE GATT");
        if (tokens.Contains("Apple AirPods Beacon") || tokens.Contains("Apple Beacon")) ordered.Add("Apple Beacon");
        if (tokens.Contains("Google Fast Pair") || tokens.Contains("Fast Pair")) ordered.Add("Fast Pair");
        if (tokens.Contains("HFP") && !ordered.Contains("Windows PnP / HFP")) ordered.Add("HFP");
        if (tokens.Contains("Xbox")) ordered.Add("Xbox");
        if (tokens.Contains("G-Hub") || tokens.Contains("Logitech G-Hub")) ordered.Add("Logitech G-Hub");
        if (tokens.Contains("HID")) ordered.Add("HID");

        foreach (var t in tokens)
        {
            if (!ordered.Any(o => o.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(o, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(t);
            }
        }

        return ordered.Count > 0 ? string.Join(" / ", ordered) : source1;
    }

    public BluetoothDeviceModel MergeOrAddDevice(BluetoothDeviceModel newDev)
    {
        if (newDev == null) return newDev!;

        lock (_deviceLockObj)
        {
            BluetoothDeviceModel? target = null;

            foreach (var existing in _devices.Values)
            {
                if (existing.Matches(newDev) || existing.Id.Equals(newDev.Id, StringComparison.OrdinalIgnoreCase))
                {
                    target = existing;
                    break;
                }
            }

            if (target == null)
            {
                _devices[newDev.Id] = newDev;
                return newDev;
            }

            // Aynı düğüm için doğrudan güncelleme (bağlantı durumu güncel değeri almalıdır)
            if (string.Equals(target.Id, newDev.Id, StringComparison.OrdinalIgnoreCase))
            {
                target.IsConnected = newDev.IsConnected;
                if (newDev.BatteryLevel.HasValue) target.BatteryLevel = newDev.BatteryLevel;
                target.IsCharging = newDev.IsCharging;
                target.LastUpdated = newDev.LastUpdated;
                if (!string.IsNullOrWhiteSpace(newDev.Name) && !BluetoothDeviceModel.IsGenericName(newDev.Name)) target.Name = newDev.Name;
                if (!string.IsNullOrWhiteSpace(newDev.AudioCodec)) target.AudioCodec = newDev.AudioCodec;
                return target;
            }

            bool incomingIsPreferred = IsPreferredMaster(newDev, target);

            BluetoothDeviceModel master = target;
            BluetoothDeviceModel secondary = newDev;

            if (incomingIsPreferred)
            {
                master = newDev;
                secondary = target;

                MergeDualModePair(master, secondary);
                _devices.TryRemove(target.Id, out _);
                _devices[newDev.Id] = master;
            }
            else
            {
                MergeDualModePair(master, secondary);
            }

            // Eğer yeni gelen olay önbellekteki cihazdan daha güncelse (canlı kopma veya bağlanma olayı),
            // birleşik cihazın bağlantı durumunu güncel olayın durumuna eşitle
            if (newDev.LastUpdated > (master == newDev ? secondary.LastUpdated : master.LastUpdated))
            {
                master.IsConnected = newDev.IsConnected;
            }

            // Mükerrer olabilecek diğer kayıtları temizle ve birleştir
            var duplicateKeys = _devices.Where(kvp => kvp.Value != master &&
                (kvp.Value.Matches(master) || kvp.Value.Id.Equals(master.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key).ToList();

            foreach (var dupKey in duplicateKeys)
            {
                if (_devices.TryRemove(dupKey, out var removedDev) && removedDev != null)
                {
                    MergeDualModePair(master, removedDev);
                }
            }

            return master;
        }
    }

    private void NotifyDevicesUpdated()
    {
        DevicesUpdated?.Invoke(this, CurrentDevices);
    }

    private void ScheduleNextPoll(TimeSpan delay)
    {
        _pollingTimer?.Dispose();
        if (_isDisposed || _isSessionLocked) return;

        _pollingTimer = new Timer(async _ =>
        {
            await RefreshAllDevicesAsync();
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            // Ekran kilitlendi: Yoklamayı durdur, pil tasarrufu sağla
            _isSessionLocked = true;
            _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            // Oturum açıldı: Yoklamayı hemen yeniden başlat
            _isSessionLocked = false;
            _ = RefreshAllDevicesAsync();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _pollingTimer?.Dispose();
        _scanLock.Dispose();

        foreach (var provider in _providers)
        {
            provider.DeviceUpdated -= OnProviderDeviceUpdated;
            provider.Dispose();
        }
        _providers.Clear();
    }
}
