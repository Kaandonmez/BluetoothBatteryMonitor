using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Windows Tak ve Çalıştır (PnP / DEVPKEY) özellikleri üzerinden pil bilgilerini okuyan sağlayıcı.
/// Xbox Wireless Controller, modern Bluetooth kulaklıklar, fareler ve telefonların Windows tarafından
/// kaydedilen pil değerlerini ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2) yakalar.
/// </summary>
public class WindowsPnpBatteryProvider
{
    // DEVPKEY_Device_BatteryLevel ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2)
    public const string PnpBatteryLevelKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    // System.Devices.BatteryLevel (Windows modern pil özelliği)
    public const string SystemBatteryLevelKey = "System.Devices.BatteryLevel";

    // DEVPKEY_Device_BatteryStatus ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 3)
    public const string PnpBatteryStatusKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 3";

    public const string PnpIsConnectedKey = "System.Devices.Connected";
    public const string PnpAepIsConnectedKey = "System.Devices.Aep.IsConnected";
    public const string PnpAepCategoryKey = "System.Devices.Aep.Category";
    public const string PnpDeviceDescKey = "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 2";
    public const string PnpBluetoothAddressKey = "{78C34FC8-104A-4ACA-9EA4-524D52996E57} 256";
    public const string PnpDeviceInstanceIdKey = "System.Devices.DeviceInstanceId";

    private static readonly string[] RequestedProperties =
    [
        "System.ItemNameDisplay",
        PnpBatteryLevelKey,
        SystemBatteryLevelKey,
        PnpBatteryStatusKey,
        PnpIsConnectedKey,
        PnpAepIsConnectedKey,
        PnpAepCategoryKey,
        PnpDeviceDescKey,
        PnpBluetoothAddressKey,
        PnpDeviceInstanceIdKey
    ];

    private DeviceWatcher? _aepDeviceWatcher;
    private DeviceWatcher? _pnpDeviceWatcher;
    private readonly Dictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;

    public IReadOnlyCollection<BluetoothDeviceModel> Devices
    {
        get
        {
            lock (_devices)
            {
                return _devices.Values.ToList();
            }
        }
    }

    /// <summary>
    /// PnP cihaz izleyicisini başlatır ve eşleştirilmiş tüm Bluetooth ve HFP cihazlarını tarar.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            // Önce mevcut cihazların anlık taramasını yap
            await RefreshAsync();

            // 1. AssociationEndpoint Watcher (Klasik Bluetooth ve BLE AEP)
            string aepSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            _aepDeviceWatcher = DeviceInformation.CreateWatcher(aepSelector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
            _aepDeviceWatcher.Added += OnWatcherDeviceAdded;
            _aepDeviceWatcher.Updated += OnWatcherDeviceUpdated;
            _aepDeviceWatcher.Removed += OnWatcherDeviceRemoved;
            _aepDeviceWatcher.Start();

            // 2. Windows DevNode Watcher (Xbox kolu, HFP Ses Kulaklığı, BTHENUM donanımları)
            string pnpAqs = "(System.Devices.DeviceInstanceId:~~\"BTHENUM\" OR System.Devices.DeviceInstanceId:~~\"BTH\" OR System.Devices.DeviceInstanceId:~~\"BLUETOOTH\" OR System.Devices.DeviceInstanceId:~~\"HID\")";
            _pnpDeviceWatcher = DeviceInformation.CreateWatcher(pnpAqs, RequestedProperties, DeviceInformationKind.Device);
            _pnpDeviceWatcher.Added += OnWatcherDeviceAdded;
            _pnpDeviceWatcher.Updated += OnWatcherDeviceUpdated;
            _pnpDeviceWatcher.Removed += OnWatcherDeviceRemoved;
            _pnpDeviceWatcher.Start();

            Debug.WriteLine("[WindowsPnpBatteryProvider] PnP ve HFP DeviceWatcher'lar başlatıldı.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsPnpBatteryProvider] StartAsync hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Sağlayıcıyı durdurur.
    /// </summary>
    public Task StopAsync()
    {
        if (_aepDeviceWatcher != null)
        {
            try
            {
                _aepDeviceWatcher.Stop();
                _aepDeviceWatcher.Added -= OnWatcherDeviceAdded;
                _aepDeviceWatcher.Updated -= OnWatcherDeviceUpdated;
                _aepDeviceWatcher.Removed -= OnWatcherDeviceRemoved;
                _aepDeviceWatcher = null;
            }
            catch { }
        }

        if (_pnpDeviceWatcher != null)
        {
            try
            {
                _pnpDeviceWatcher.Stop();
                _pnpDeviceWatcher.Added -= OnWatcherDeviceAdded;
                _pnpDeviceWatcher.Updated -= OnWatcherDeviceUpdated;
                _pnpDeviceWatcher.Removed -= OnWatcherDeviceRemoved;
                _pnpDeviceWatcher = null;
            }
            catch { }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Eşleşmiş tüm cihazları (AEP, PnP DevNode, HFP Ses, Xbox) sorgular.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            // 1. Windows PnP DevNode'ları (BTHENUM, HFP Ses aygıtları, Xbox kolları, HID pilleri)
            string pnpAqs = "(System.Devices.DeviceInstanceId:~~\"BTHENUM\" OR System.Devices.DeviceInstanceId:~~\"BTH\" OR System.Devices.DeviceInstanceId:~~\"BLUETOOTH\" OR System.Devices.DeviceInstanceId:~~\"HID\")";
            try
            {
                var pnpDevices = await DeviceInformation.FindAllAsync(pnpAqs, RequestedProperties, DeviceInformationKind.Device);
                foreach (var di in pnpDevices)
                {
                    ProcessDeviceInformation(di);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsPnpBatteryProvider] PnP devnode tarama hatası: {ex.Message}");
            }

            // 2. Klasik Bluetooth AEP cihazları
            try
            {
                string btSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                var btDevices = await DeviceInformation.FindAllAsync(btSelector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                foreach (var di in btDevices)
                {
                    ProcessDeviceInformation(di);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsPnpBatteryProvider] BT AEP tarama hatası: {ex.Message}");
            }

            // 3. BLE AEP cihazları
            try
            {
                string bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                var bleDevices = await DeviceInformation.FindAllAsync(bleSelector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                foreach (var di in bleDevices)
                {
                    ProcessDeviceInformation(di);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsPnpBatteryProvider] BLE AEP tarama hatası: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsPnpBatteryProvider] RefreshAsync genel hatası: {ex.Message}");
        }
    }

    private void OnWatcherDeviceAdded(DeviceWatcher sender, DeviceInformation devInfo)
    {
        ProcessDeviceInformation(devInfo);
    }

    private void OnWatcherDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate devUpdate)
    {
        lock (_devices)
        {
            if (_devices.TryGetValue(devUpdate.Id, out var existing))
            {
                UpdateDeviceWithProperties(existing, devUpdate.Properties);
                existing.LastSeen = DateTime.Now;
                DeviceUpdated?.Invoke(this, existing);
            }
        }
    }

    private void OnWatcherDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate devUpdate)
    {
        lock (_devices)
        {
            if (_devices.Remove(devUpdate.Id))
            {
                DeviceRemoved?.Invoke(this, devUpdate.Id);
            }
        }
    }

    private void ProcessDeviceInformation(DeviceInformation devInfo)
    {
        if (string.IsNullOrWhiteSpace(devInfo.Name) && string.IsNullOrWhiteSpace(devInfo.Id))
        {
            return;
        }

        lock (_devices)
        {
            if (!_devices.TryGetValue(devInfo.Id, out var model))
            {
                model = new BluetoothDeviceModel
                {
                    Id = devInfo.Id,
                    Name = !string.IsNullOrWhiteSpace(devInfo.Name) ? devInfo.Name : "Bluetooth Aygıtı",
                    ProviderSource = "Windows PnP"
                };
                _devices[devInfo.Id] = model;
            }

            UpdateDeviceWithProperties(model, devInfo.Properties);
            model.Type = DetectDeviceType(model.Name, devInfo.Properties);
            model.LastSeen = DateTime.Now;

            DeviceUpdated?.Invoke(this, model);
        }
    }

    private void UpdateDeviceWithProperties(BluetoothDeviceModel model, IReadOnlyDictionary<string, object> properties)
    {
        // 1. Pil Seviyesi Okuma (Önce DEVPKEY_Device_BatteryLevel, sonra System.Devices.BatteryLevel)
        object? batteryObj = null;
        if (properties.TryGetValue(PnpBatteryLevelKey, out var pnpObj) && pnpObj != null)
        {
            batteryObj = pnpObj;
        }
        else if (properties.TryGetValue(SystemBatteryLevelKey, out var sysObj) && sysObj != null)
        {
            batteryObj = sysObj;
        }

        if (batteryObj != null)
        {
            try
            {
                int level = Convert.ToInt32(batteryObj);
                if (level >= 0 && level <= 100)
                {
                    model.Battery.Level = level;
                    model.Battery.LastUpdated = DateTime.Now;
                }
            }
            catch { }
        }

        // 2. Pil Şarj Durumu
        if (properties.TryGetValue(PnpBatteryStatusKey, out var statusObj) && statusObj != null)
        {
            try
            {
                int status = Convert.ToInt32(statusObj);
                // 1 = Charging, 2 = Discharging/Critical
                model.Battery.IsCharging = status == 1;
            }
            catch { }
        }

        // 3. Bağlantı Durumu (AEP canlı bağlantı teyidi zorunludur)
        if (properties.TryGetValue(PnpAepIsConnectedKey, out var aepConnObj) && aepConnObj is bool isAepConnected)
        {
            model.IsConnected = isAepConnected;
        }
        else
        {
            model.IsConnected = false;
        }


        // 4. Bluetooth MAC Adresi (varsa)
        if (properties.TryGetValue(PnpBluetoothAddressKey, out var addrObj) && addrObj != null)
        {
            try
            {
                model.BluetoothAddress = Convert.ToUInt64(addrObj);
            }
            catch { }
        }

        if (model.BluetoothAddress == 0)
        {
            model.BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(model.Id);
        }

        // 5. Kaynak sağlayıcı detaylandırması (Hands-Free HFP ses aygıtı veya Xbox tespiti)
        if (model.Type is DeviceType.Headset or DeviceType.Headphones or DeviceType.Speaker ||
            model.Id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            model.ProviderSource = "Windows PnP / HFP";
        }
        else if (model.Type == DeviceType.Gamepad || model.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
        {
            model.ProviderSource = "Windows PnP (Xbox)";
        }
        else
        {
            model.ProviderSource = "Windows PnP";
        }
    }

    /// <summary>
    /// Cihaz adı ve PnP kategori özelliklerine göre DeviceType belirler.
    /// </summary>
    public static DeviceType DetectDeviceType(string name, IReadOnlyDictionary<string, object>? properties = null)
    {
        string n = name.ToLowerInvariant();

        // İsim bazlı tespit
        if (n.Contains("xbox") || n.Contains("controller") || n.Contains("gamepad") || n.Contains("dualshock") || n.Contains("dualsense"))
        {
            return DeviceType.Gamepad;
        }
        if (n.Contains("airpods") || n.Contains("buds") || n.Contains("tws") || n.Contains("earbuds") || n.Contains("freebuds"))
        {
            return DeviceType.Earbuds;
        }
        if (n.Contains("headset") || n.Contains("hyperx") || n.Contains("arctis") || n.Contains("corsair"))
        {
            return DeviceType.Headset;
        }
        if (n.Contains("headphone") || n.Contains("kulaklık") || n.Contains("wh-1000") || n.Contains("bose") || n.Contains("qc35") || n.Contains("qc45"))
        {
            return DeviceType.Headphones;
        }
        if (n.Contains("mouse") || n.Contains("fare") || n.Contains("mx master") || n.Contains("deathadder") || n.Contains("g pro") || n.Contains("g502"))
        {
            return DeviceType.Mouse;
        }
        if (n.Contains("keyboard") || n.Contains("klavye") || n.Contains("k380") || n.Contains("k810") || n.Contains("mx keys"))
        {
            return DeviceType.Keyboard;
        }
        if (n.Contains("speaker") || n.Contains("hoparlör") || n.Contains("flip") || n.Contains("charge") || n.Contains("soundcore"))
        {
            return DeviceType.Speaker;
        }
        if (n.Contains("phone") || n.Contains("iphone") || n.Contains("galaxy") || n.Contains("telefon") || n.Contains("pixel"))
        {
            return DeviceType.Phone;
        }
        if (n.Contains("watch") || n.Contains("saat") || n.Contains("garmin"))
        {
            return DeviceType.Watch;
        }
        if (n.Contains("pen") || n.Contains("stylus") || n.Contains("pencil") || n.Contains("kalem"))
        {
            return DeviceType.Stylus;
        }

        // Kategori bazlı tespit
        if (properties != null && properties.TryGetValue(PnpAepCategoryKey, out var catObj) && catObj is string category)
        {
            string cat = category.ToLowerInvariant();
            if (cat.Contains("input.gamecontroller")) return DeviceType.Gamepad;
            if (cat.Contains("input.mouse")) return DeviceType.Mouse;
            if (cat.Contains("input.keyboard")) return DeviceType.Keyboard;
            if (cat.Contains("audio.headset")) return DeviceType.Headset;
            if (cat.Contains("audio.headphones")) return DeviceType.Headphones;
            if (cat.Contains("audio.speaker")) return DeviceType.Speaker;
            if (cat.Contains("phone")) return DeviceType.Phone;
        }

        return DeviceType.Unknown;
    }
}
