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

        // Register providers
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

        // Listen to Windows session lock / unlock events (Power saving)
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Start()
    {
        foreach (var provider in _providers)
        {
            provider.StartMonitoring();
        }

        // Start initial scan
        _ = RefreshAllDevicesAsync();

        // Setup smart polling timer
        ScheduleNextPoll(TimeSpan.FromSeconds(10));
    }

    public async Task RefreshAllDevicesAsync()
    {
        if (_isSessionLocked || _isDisposed) return;
        if (!await _scanLock.WaitAsync(100)) return; // Prevent concurrent scans

        try
        {
            StatusChanged?.Invoke(this, "Scanning Bluetooth devices...");

            var connectionSnapshot = await BluetoothConnectionChecker.CaptureSnapshotAsync(_audioManager);

            // Update connection state of cached devices with live snapshot
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
                // Validate provider devices with live snapshot
                BluetoothConnectionChecker.UpdateConnectionStatuses(devList, connectionSnapshot);

                foreach (var dev in devList)
                {
                    MergeOrAddDevice(dev);
                }
            }

            // Update stale devices or closed-lid beacons
            var now = DateTime.Now;
            foreach (var kvp in _devices)
            {
                // Apple AirPods / Beats and Google Fast Pair beacons stop advertising when case is closed.
                // For disconnected beacons only: purge device if no advertisement received for 30 seconds.
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

            // Aggregate dual-mode (Classic + BLE GATT) devices
            AggregateDualModeDevices();

            // Finalize live connection status of all devices
            BluetoothConnectionChecker.UpdateConnectionStatuses(_devices.Values, connectionSnapshot);

            // Run codec detection and notification checks ONLY on confirmed connected devices
            foreach (var activeDev in CurrentDevices)
            {
                if (activeDev.IsConnected && (activeDev.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker))
                {
                    activeDev.AudioCodec = _codecDetector.DetectCodec(activeDev);
                }
                _notificationService.CheckAndNotifyBattery(activeDev, settings);
            }

            StatusChanged?.Invoke(this, $"{_devices.Count} device(s) found.");
            NotifyDevicesUpdated();
        }
        finally
        {
            _scanLock.Release();

            // Schedule next poll intelligently
            int interval = AppSettings.Load().RefreshIntervalSeconds;
            if (_devices.IsEmpty)
            {
                interval = Math.Max(interval, 300); // 5 minutes when no devices are paired
            }
            ScheduleNextPoll(TimeSpan.FromSeconds(interval));
        }
    }

    private async void OnProviderDeviceUpdated(object? sender, BluetoothDeviceModel dev)
    {
        // Verify live connection status of incoming event
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

        // Secondary pass: Merge transitive matches to guarantee complete uniqueness
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

        // 1. Audio device (Headphones/Earbuds/Speaker) is preferred over generic node for audio routing and codec detection
        if (candidateIsAudio && !currentIsAudio) return true;
        if (!candidateIsAudio && currentIsAudio) return false;

        // 2. Device type priority (e.g. Phone, Gamepad > Generic)
        int candPriority = GetDeviceTypePriority(candidate.DeviceType);
        int currPriority = GetDeviceTypePriority(current.DeviceType);
        if (candPriority > currPriority) return true;
        if (candPriority < currPriority) return false;

        // 3. Prefer Classic BTHENUM node over BLE node for Windows Audio Endpoint compatibility
        bool candidateIsBtEnum = (candidate.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
        bool currentIsBtEnum = (current.Id ?? string.Empty).Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
        if (candidateIsBtEnum && !currentIsBtEnum) return true;
        if (!candidateIsBtEnum && currentIsBtEnum) return false;

        // 4. Model name richness
        bool candidateHasModel = !string.IsNullOrWhiteSpace(candidate.ModelName);
        bool currentHasModel = !string.IsNullOrWhiteSpace(current.ModelName);
        if (candidateHasModel && !currentHasModel) return true;
        if (!candidateHasModel && currentHasModel) return false;

        // 5. Clean display name (prefer names without LE prefix/suffix)
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

        // Name priority: Preserve custom or detailed name (do not prefer technical LE prefix over clean name)
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

        // Device type: Upgrade generic type to more specific device type
        if (GetDeviceTypePriority(secondary.DeviceType) > GetDeviceTypePriority(master.DeviceType))
        {
            master.DeviceType = secondary.DeviceType;
        }

        // MAC address propagation (preserve dual-mode BR/EDR and BLE addresses)
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

        // Battery telemetry:
        // 1. For TWS devices: transfer all component levels (Left, Right, Case)
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
            // 2. Standard battery level:
            // High-resolution (1% step) BLE GATT/PnP data always takes precedence over coarse (10% step) Classic HFP data!
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
                    // BLE GATT or PnP BLE data takes precedence over HFP
                    master.BatteryLevel = secondary.BatteryLevel;
                    master.IsCharging = secondary.IsCharging;
                }
                else if (!masterIsHfp && secondaryIsHfp)
                {
                    // Master already holds high-resolution telemetry; do not overwrite with coarse HFP data!
                    master.IsCharging = master.IsCharging || secondary.IsCharging;
                }
                else
                {
                    // Both are in the same class (both HFP or both BLE/PnP):
                    // If newer telemetry arrived (secondary.LastUpdated > master.LastUpdated), always update!
                    // In simultaneous/startup scans, prefer high-resolution (1% step, non-multiple of 10) values.
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

        // Connection status: device is connected if either node is connected
        master.IsConnected = master.IsConnected || secondary.IsConnected;

        // Audio codec propagation (prioritize detected high-resolution codec)
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

        // Mark dual-mode aggregated
        master.IsAggregated = true;
        secondary.IsAggregated = true;

        // Nicely merge provider sources (e.g. "Windows PnP / BLE GATT")
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
        if (string.IsNullOrWhiteSpace(source1) || source1.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || source1.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
            return source2 ?? "Unknown";
        if (string.IsNullOrWhiteSpace(source2) || source2.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || source2.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
            return source1;

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFrom(string src)
        {
            var raw = src.Split(new[] { " + ", " / ", " • ", " (Live)", " (Canlı)" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var r in raw)
            {
                if (string.IsNullOrWhiteSpace(r) || r.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || r.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase)) continue;
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

            // Direct in-place update for identical node ID (connection status receives fresh value)
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

            // If incoming live event is newer than cached device (disconnect or connect event), update connection state
            if (newDev.LastUpdated > (master == newDev ? secondary.LastUpdated : master.LastUpdated))
            {
                master.IsConnected = newDev.IsConnected;
            }

            // Clean up and merge any remaining duplicate candidates
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
            // Screen locked: Pause polling to save battery
            _isSessionLocked = true;
            _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            // Session unlocked: Resume polling immediately
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
