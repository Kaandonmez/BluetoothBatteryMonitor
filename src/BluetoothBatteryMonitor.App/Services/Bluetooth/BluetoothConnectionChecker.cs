using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Audio;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>
/// Sistemdeki tüm Bluetooth cihazlarının anlık ve gerçek canlı bağlantı durumunu (ACL / GATT bağlantısı)
/// yüksek performanslı Win32 API'leri, WinRT AssociationEndpoint (AEP) telemetrisi ve CoreAudio durumları
/// üzerinden teyit eden merkezi bağlantı denetleyicisi.
/// </summary>
public static class BluetoothConnectionChecker
{
    private const string AepIsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string AepDeviceAddressKey = "System.Devices.Aep.DeviceAddress";

    /// <summary>
    /// Bir tarama döngüsü anında yakalanmış canlı Bluetooth bağlantı anlık görüntüsü (Snapshot).
    /// </summary>
    public class BluetoothConnectionSnapshot
    {
        public Dictionary<ulong, bool> ClassicConnectedByMac { get; } = new();
        public Dictionary<ulong, bool> AepConnectedByMac { get; } = new();
        public Dictionary<string, bool> AepConnectedById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> ConnectedByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ulong> MacByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ulong> KnownPairedMacs { get; } = new();
        public HashSet<string> ActiveAudioNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Anlık görüntü verilerine göre cihazın aktif olarak bağlı olup olmadığını belirler.
        /// Kesin tespit yapılamazsa false döner.
        /// </summary>
        public bool? TryGetStatus(ulong mac, string? id, string? name, DeviceType type)
        {
            string cleanName = !string.IsNullOrWhiteSpace(name) ? BluetoothDeviceModel.CleanNameForComparison(name) : string.Empty;

            // 1. MAC adresi biliniyorsa: Win32 Classic Bluetooth ve WinRT AEP kontrolleri (Hardware MAC)
            if (mac != 0)
            {
                bool inClassic = ClassicConnectedByMac.TryGetValue(mac, out bool classicConnected);
                bool inAep = AepConnectedByMac.TryGetValue(mac, out bool aepConnected);
                bool isKnownPaired = KnownPairedMacs.Contains(mac);

                // Herhangi biri bağlı diyorsa KESİN BAĞLIDIR
                if ((inClassic && classicConnected) || (inAep && aepConnected))
                {
                    return true;
                }

                // Eğer sistemde bu MAC adresi kayıtlı/eşleşmiş ise ve yukarıda bağlı dönmediyse,
                // bu donanım kesinlikle bağlı DEĞİLDİR! İsim benzerlikleri bu kesin donanım sonucunu ezemez.
                if (inClassic || inAep || isKnownPaired)
                {
                    return false;
                }
            }

            // 2. Benzersiz DeviceId üzerinden AEP kontrolü
            if (!string.IsNullOrEmpty(id) && AepConnectedById.TryGetValue(id, out bool idConnected))
            {
                return idConnected;
            }

            // 3. MAC adresi 0 olan aygıtlar için isim üzerinden MAC çözümleme
            if (mac == 0 && !string.IsNullOrEmpty(cleanName) && !BluetoothDeviceModel.IsGenericName(cleanName))
            {
                if (MacByName.TryGetValue(cleanName, out ulong resolvedMac) && resolvedMac != 0)
                {
                    bool inClassic = ClassicConnectedByMac.TryGetValue(resolvedMac, out bool classicConnected);
                    bool inAep = AepConnectedByMac.TryGetValue(resolvedMac, out bool aepConnected);
                    bool isKnownPaired = KnownPairedMacs.Contains(resolvedMac);

                    if ((inClassic && classicConnected) || (inAep && aepConnected))
                    {
                        return true;
                    }

                    if (inClassic || inAep || isKnownPaired)
                    {
                        return false;
                    }
                }
            }

            // 4. Ses aygıtları için Windows CoreAudio aktif MMDevice kontrolü
            bool isAudioDevice = type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker;
            if (isAudioDevice && !string.IsNullOrWhiteSpace(name))
            {
                foreach (var activeName in ActiveAudioNames)
                {
                    if (!string.IsNullOrEmpty(cleanName) && cleanName.Equals(activeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (name.Equals(activeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            // 5. Cihaz adı üzerinden canlı durum kontrolü (Sadece generic olmayan ve MAC adresi sistemde olmayan aygıtlar için)
            if (!string.IsNullOrEmpty(cleanName) && !BluetoothDeviceModel.IsGenericName(cleanName))
            {
                if (ConnectedByName.TryGetValue(cleanName, out bool nameConnected))
                {
                    return nameConnected;
                }
            }

            // 6. Sistemde hiçbir yerde aktif bağlantı kanıtı yoksa bağlı değildir
            return false;
        }

        public bool IsConnected(BluetoothDeviceModel model)
        {
            if (model == null) return false;

            // Active BLE advertising beacons kendi canlı süre aşımını yönetir
            if (model.IsTws && (model.ProviderSource.Contains("Apple Beacon", StringComparison.OrdinalIgnoreCase) ||
                               model.ProviderSource.Contains("Fast Pair", StringComparison.OrdinalIgnoreCase)))
            {
                string cleanName = !string.IsNullOrWhiteSpace(model.Name) ? BluetoothDeviceModel.CleanNameForComparison(model.Name) : string.Empty;

                // 1. Önce aktif Windows CoreAudio kontrolü!
                bool isAudioActive = ActiveAudioNames.Any(a =>
                    a.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(cleanName) && (a.Contains(cleanName, StringComparison.OrdinalIgnoreCase) || cleanName.Contains(a, StringComparison.OrdinalIgnoreCase))) ||
                    (!string.IsNullOrEmpty(model.Name) && (a.Contains(model.Name, StringComparison.OrdinalIgnoreCase) || model.Name.Contains(a, StringComparison.OrdinalIgnoreCase))));

                // 2. Win32 / WinRT eşleşmiş cihaz bağlantı kontrolü (MacByName üzerinden)
                bool isPairedConnected = false;
                if (!string.IsNullOrEmpty(cleanName) && MacByName.TryGetValue(cleanName, out ulong resolvedMac) && resolvedMac != 0)
                {
                    model.SecondaryBluetoothAddress = resolvedMac;
                    if ((ClassicConnectedByMac.TryGetValue(resolvedMac, out bool cConn) && cConn) ||
                        (AepConnectedByMac.TryGetValue(resolvedMac, out bool aConn) && aConn))
                    {
                        isPairedConnected = true;
                    }
                }

                // 3. ConnectedByName kontrolü
                if (!isPairedConnected)
                {
                    foreach (var kvp in ConnectedByName)
                    {
                        if (kvp.Key.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(cleanName) && (kvp.Key.Contains(cleanName, StringComparison.OrdinalIgnoreCase) || cleanName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))))
                        {
                            if (kvp.Value)
                            {
                                isPairedConnected = true;
                                break;
                            }
                        }
                    }
                }

                if (isAudioActive || isPairedConnected)
                {
                    // Aktif ses akışı veya Windows Bluetooth bağlantısı varken kulaklıklar kutuda şarj olamaz
                    if (model.IsLeftCharging && model.IsRightCharging)
                    {
                        model.IsLeftCharging = false;
                        model.IsRightCharging = false;
                    }
                    return true;
                }

                // 4. Eğer aktif ses veya Windows bağlantısı yoksa ve her iki kulaklık da şarj oluyorsa kutudadır
                if (model.IsLeftCharging && model.IsRightCharging)
                {
                    return false;
                }

                ulong beaconMac = model.BluetoothAddress != 0 ? model.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(model.Id);
                if (beaconMac != 0)
                {
                    var stat = TryGetStatus(beaconMac, model.Id, model.Name, model.Type);
                    if (stat.HasValue) return stat.Value;
                }

                return model.IsConnected;
            }

            ulong mac = model.BluetoothAddress != 0 ? model.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(model.Id);
            if (mac == 0 && !string.IsNullOrWhiteSpace(model.Name))
            {
                string clean = BluetoothDeviceModel.CleanNameForComparison(model.Name);
                if (!BluetoothDeviceModel.IsGenericName(clean) && MacByName.TryGetValue(clean, out ulong resolvedMac))
                {
                    mac = resolvedMac;
                    model.BluetoothAddress = resolvedMac;
                }
            }

            var status = TryGetStatus(mac, model.Id, model.Name, model.Type);
            if (status == true) return true;

            // İkincil MAC kontrolü (Dual-mode)
            if (model.SecondaryBluetoothAddress != 0)
            {
                var secStatus = TryGetStatus(model.SecondaryBluetoothAddress, null, model.Name, model.Type);
                if (secStatus == true) return true;
            }

            return status ?? false;
        }
    }

    /// <summary>
    /// Sistem genelindeki tüm Bluetooth ve ses bağlantı durumlarının hızlı bir anlık görüntüsünü alır (< 50ms).
    /// </summary>
    public static async Task<BluetoothConnectionSnapshot> CaptureSnapshotAsync(IAudioEndpointManager? audioManager = null)
    {
        var snapshot = new BluetoothConnectionSnapshot();

        // 1. Win32 Classic Bluetooth Cihazlarını Tara (bthprops.cpl / BluetoothApis.dll)
        try
        {
            CaptureWin32ClassicDevices(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BluetoothConnectionChecker] Win32 Bluetooth sorgu hatası: {ex.Message}");
        }

        // 2. WinRT AssociationEndpoint (Classic ve BLE) Cihazlarını Paralel Tara
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var reqProps = new[] { AepIsConnectedKey, AepDeviceAddressKey, "System.ItemNameDisplay" };

            var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);

            var classicTask = DeviceInformation.FindAllAsync(classicSelector, reqProps, DeviceInformationKind.AssociationEndpoint).AsTask(cts.Token);
            var bleTask = DeviceInformation.FindAllAsync(bleSelector, reqProps, DeviceInformationKind.AssociationEndpoint).AsTask(cts.Token);

            await Task.WhenAll(classicTask, bleTask);

            ProcessAepResults(snapshot, classicTask.Result);
            ProcessAepResults(snapshot, bleTask.Result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BluetoothConnectionChecker] WinRT AEP sorgu hatası: {ex.Message}");
        }

        // 3. Aktif CoreAudio Çıkışlarını Al
        try
        {
            var audioMgr = audioManager ?? new AudioEndpointManager();
            var endpoints = audioMgr.GetPlaybackEndpoints();
            foreach (var ep in endpoints)
            {
                if (string.IsNullOrWhiteSpace(ep.Name)) continue;

                // Parantez içi temiz aygıt adını çıkar (örn: "Kulaklıklar (AirPods Pro - Find My)" -> "AirPods Pro - Find My")
                string inside = ep.Name;
                int openParen = ep.Name.IndexOf('(');
                int closeParen = ep.Name.LastIndexOf(')');
                if (openParen >= 0 && closeParen > openParen)
                {
                    inside = ep.Name.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                }

                string cleanInside = BluetoothDeviceModel.CleanNameForComparison(inside);
                string cleanFull = BluetoothDeviceModel.CleanNameForComparison(ep.Name);

                bool isBluetooth = (!string.IsNullOrEmpty(ep.DeviceInstanceId) && ep.DeviceInstanceId.Contains("BTH", StringComparison.OrdinalIgnoreCase))
                                   || (!string.IsNullOrEmpty(ep.Id) && ep.Id.Contains("BTH", StringComparison.OrdinalIgnoreCase))
                                   || (!string.IsNullOrEmpty(ep.Description) && ep.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                                   || snapshot.ConnectedByName.ContainsKey(cleanInside)
                                   || snapshot.ConnectedByName.ContainsKey(inside)
                                   || snapshot.MacByName.ContainsKey(cleanInside)
                                   || snapshot.MacByName.ContainsKey(inside)
                                   || inside.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
                                   || cleanInside.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
                                   || ep.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

                if (isBluetooth)
                {
                    snapshot.ActiveAudioNames.Add(ep.Name);
                    if (!string.IsNullOrEmpty(inside)) snapshot.ActiveAudioNames.Add(inside);
                    if (!string.IsNullOrEmpty(cleanInside)) snapshot.ActiveAudioNames.Add(cleanInside);
                    if (!string.IsNullOrEmpty(cleanFull)) snapshot.ActiveAudioNames.Add(cleanFull);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BluetoothConnectionChecker] AudioEndpoint sorgu hatası: {ex.Message}");
        }

        return snapshot;
    }

    private static void CaptureWin32ClassicDevices(BluetoothConnectionSnapshot snapshot)
    {
        var searchParams = new NativeMethods.BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = true,
            fReturnRemembered = true,
            fReturnConnected = true,
            fReturnUnknown = true,
            fIssueInquiry = false,
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero
        };

        var devInfo = new NativeMethods.BLUETOOTH_DEVICE_INFO
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.BLUETOOTH_DEVICE_INFO>()
        };

        IntPtr hFind = NativeMethods.BluetoothFindFirstDevice(ref searchParams, ref devInfo);
        if (hFind != IntPtr.Zero)
        {
            try
            {
                do
                {
                    if (devInfo.Address != 0)
                    {
                        snapshot.ClassicConnectedByMac[devInfo.Address] = devInfo.fConnected;
                        snapshot.KnownPairedMacs.Add(devInfo.Address);

                        string devName = devInfo.szName;
                        if (!string.IsNullOrWhiteSpace(devName))
                        {
                            string clean = BluetoothDeviceModel.CleanNameForComparison(devName);
                            if (!string.IsNullOrWhiteSpace(clean) && !BluetoothDeviceModel.IsGenericName(clean))
                            {
                                if (!snapshot.ConnectedByName.TryGetValue(clean, out bool existing) || devInfo.fConnected)
                                {
                                    snapshot.ConnectedByName[clean] = devInfo.fConnected;
                                }
                                snapshot.MacByName[clean] = devInfo.Address;
                            }
                        }
                    }
                    devInfo = new NativeMethods.BLUETOOTH_DEVICE_INFO
                    {
                        dwSize = (uint)Marshal.SizeOf<NativeMethods.BLUETOOTH_DEVICE_INFO>()
                    };
                } while (NativeMethods.BluetoothFindNextDevice(hFind, ref devInfo));
            }
            finally
            {
                NativeMethods.BluetoothFindDeviceClose(hFind);
            }
        }
    }

    private static void ProcessAepResults(BluetoothConnectionSnapshot snapshot, DeviceInformationCollection devices)
    {
        if (devices == null) return;

        foreach (var dev in devices)
        {
            bool isConnected = false;
            if (dev.Properties.TryGetValue(AepIsConnectedKey, out var connObj) && connObj is bool b)
            {
                isConnected = b;
            }

            snapshot.AepConnectedById[dev.Id] = isConnected;

            // MAC adresi çözümleme
            ulong mac = 0;
            if (dev.Properties.TryGetValue(AepDeviceAddressKey, out var addrObj) && addrObj is string addrStr)
            {
                mac = BluetoothDeviceModel.ExtractMacAddress(addrStr);
            }

            if (mac == 0)
            {
                mac = BluetoothDeviceModel.ExtractMacAddress(dev.Id);
            }

            if (mac != 0)
            {
                snapshot.KnownPairedMacs.Add(mac);
                // Eğer daha önce bağlı olduğu bilinmiyorsa veya yeni değer bağlıysa güncelle
                if (!snapshot.AepConnectedByMac.TryGetValue(mac, out bool existing) || isConnected)
                {
                    snapshot.AepConnectedByMac[mac] = isConnected;
                }
            }

            string? name = dev.Name;
            if (dev.Properties.TryGetValue("System.ItemNameDisplay", out var nameObj) && nameObj is string sName && !string.IsNullOrWhiteSpace(sName))
            {
                name = sName;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                string clean = BluetoothDeviceModel.CleanNameForComparison(name);
                if (!string.IsNullOrWhiteSpace(clean) && !BluetoothDeviceModel.IsGenericName(clean))
                {
                    if (!snapshot.ConnectedByName.TryGetValue(clean, out bool existing) || isConnected)
                    {
                        snapshot.ConnectedByName[clean] = isConnected;
                    }
                    if (mac != 0 && (!snapshot.MacByName.ContainsKey(clean) || isConnected))
                    {
                        snapshot.MacByName[clean] = mac;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verilen cihazın canlı bağlantı durumunu teyit eder.
    /// Tek bir doğru kaynak (Single Source of Truth) olarak BluetoothConnectionSnapshot kullanır.
    /// </summary>
    public static async Task<bool> IsDeviceConnectedAsync(BluetoothDeviceModel model, BluetoothConnectionSnapshot? snapshot = null)
    {
        if (model == null) return false;

        var snap = snapshot ?? await CaptureSnapshotAsync();
        return snap.IsConnected(model);
    }

    private static async Task<bool> CheckWinRtAddressConnectedAsync(ulong mac)
    {
        if (mac == 0) return false;

        try
        {
            using var cts = new CancellationTokenSource(600);
            var classic = await BluetoothDevice.FromBluetoothAddressAsync(mac).AsTask(cts.Token);
            if (classic != null && classic.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                return true;
            }
        }
        catch { }

        try
        {
            using var cts = new CancellationTokenSource(600);
            var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(mac).AsTask(cts.Token);
            if (ble != null && ble.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Bir cihaz listesinin bağlantı durumlarını snapshot üzerinden günceller.
    /// </summary>
    public static void UpdateConnectionStatuses(IEnumerable<BluetoothDeviceModel> devices, BluetoothConnectionSnapshot snapshot)
    {
        if (devices == null || snapshot == null) return;

        foreach (var dev in devices)
        {
            dev.IsConnected = snapshot.IsConnected(dev);
        }
    }
}
