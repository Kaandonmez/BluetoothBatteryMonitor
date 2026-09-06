using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public class WindowsPnpBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Windows PnP / HFP-AVRCP / Xbox";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    // DEVPKEY_Device_BatteryLevel: {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2
    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string SystemIsConnectedKey = "System.Devices.Connected";

    private static readonly string[] RequestedProperties =
    [
        PnpBatteryKey,
        SystemIsConnectedKey,
        "System.ItemNameDisplay"
    ];

    private const string PnpAqsFilter = "(System.Devices.DeviceInstanceId:~~\"BTHENUM\" OR System.Devices.DeviceInstanceId:~~\"BTH\" OR System.Devices.DeviceInstanceId:~~\"BLUETOOTH\" OR System.Devices.DeviceInstanceId:~~\"HID\")";

    private DeviceWatcher? _deviceWatcher;
    private bool _isMonitoring;

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<BluetoothDeviceModel>();

        try
        {
            var connectionSnapshot = await BluetoothConnectionChecker.CaptureSnapshotAsync();

            var deviceInfos = await DeviceInformation.FindAllAsync(
                PnpAqsFilter,
                RequestedProperties,
                DeviceInformationKind.Device).AsTask(cancellationToken);

            foreach (var info in deviceInfos)
            {
                if (cancellationToken.IsCancellationRequested) break;

                int? battery = ExtractBatteryLevel(info);
                if (battery.HasValue)
                {
                    string cleanName = CleanDeviceName(info.Name);
                    var devType = DetectDeviceType(cleanName, info.Id);
                    ulong mac = BluetoothDeviceModel.ExtractMacAddress(info.Id);

                    bool isConnected = connectionSnapshot.TryGetStatus(mac, info.Id, cleanName, devType) ?? false;

                    string providerSource = "Windows PnP";
                    if (devType == DeviceType.Gamepad || cleanName.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
                    {
                        providerSource = "Windows PnP (Xbox)";
                    }
                    else if (devType is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker ||
                             info.Id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
                    {
                        providerSource = "Windows PnP / HFP";
                    }

                    var model = new BluetoothDeviceModel
                    {
                        Id = info.Id,
                        Name = cleanName,
                        BatteryLevel = battery.Value,
                        IsConnected = isConnected,
                        DeviceType = devType,
                        BluetoothAddress = mac,
                        ProviderSource = providerSource,
                        LastUpdated = DateTime.Now
                    };

                    list.Add(model);
                }
            }
        }
        catch
        {
            // PnP sorgu hatası
        }

        return list;
    }

    private static int? ExtractBatteryLevel(DeviceInformation info)
    {
        if (info.Properties.TryGetValue(PnpBatteryKey, out var pnpObj) && pnpObj != null)
        {
            if (pnpObj is byte b) return b;
            if (pnpObj is int i) return i;
            if (pnpObj is short s) return (int)s;
            if (int.TryParse(pnpObj.ToString(), out int parsed)) return parsed;
        }

        return null;
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;

        try
        {
            _deviceWatcher = DeviceInformation.CreateWatcher(
                PnpAqsFilter,
                RequestedProperties,
                DeviceInformationKind.Device);

            _deviceWatcher.Updated += OnDeviceWatcherUpdated;
            _deviceWatcher.Added += OnDeviceWatcherAdded;
            _deviceWatcher.Start();
            _isMonitoring = true;
        }
        catch
        {
            // Watcher başlatılamazsa periyodik sorgu ile idare edilir
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        try
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.Stop();
                _deviceWatcher.Updated -= OnDeviceWatcherUpdated;
                _deviceWatcher.Added -= OnDeviceWatcherAdded;
                _deviceWatcher = null;
            }
            _isMonitoring = false;
        }
        catch { }
    }

    private async void OnDeviceWatcherAdded(DeviceWatcher sender, DeviceInformation info)
    {
        int? battery = ExtractBatteryLevel(info);
        if (battery.HasValue)
        {
            string cleanName = CleanDeviceName(info.Name);
            var devType = DetectDeviceType(cleanName, info.Id);

            string providerSource = "Windows PnP";
            if (devType == DeviceType.Gamepad || cleanName.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
            {
                providerSource = "Windows PnP (Xbox)";
            }
            else if (devType is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker ||
                     info.Id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
            {
                providerSource = "Windows PnP / HFP";
            }

            var model = new BluetoothDeviceModel
            {
                Id = info.Id,
                Name = cleanName,
                BatteryLevel = battery.Value,
                IsConnected = false,
                DeviceType = devType,
                BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(info.Id),
                ProviderSource = providerSource,
                LastUpdated = DateTime.Now
            };

            model.IsConnected = await BluetoothConnectionChecker.IsDeviceConnectedAsync(model);
            DeviceUpdated?.Invoke(this, model);
        }
    }

    private async void OnDeviceWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate infoUpdate)
    {
        if (infoUpdate.Properties.TryGetValue(PnpBatteryKey, out var pnpObj) && pnpObj != null)
        {
            if (int.TryParse(pnpObj.ToString(), out int level))
            {
                var model = new BluetoothDeviceModel
                {
                    Id = infoUpdate.Id,
                    BatteryLevel = level,
                    LastUpdated = DateTime.Now,
                    BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(infoUpdate.Id),
                    ProviderSource = "Windows PnP (Canlı)"
                };

                model.IsConnected = await BluetoothConnectionChecker.IsDeviceConnectedAsync(model);
                DeviceUpdated?.Invoke(this, model);
            }
        }
    }

    private static string CleanDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bluetooth Aygıtı";
        string cleaned = name.Replace(" Hands-Free AG", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" Hands-Free HF Audio", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" Hands-Free", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" HF Audio", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" HF", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" Avrcp Transport", "", StringComparison.OrdinalIgnoreCase)
                             .Replace(" A2DP SNK", "", StringComparison.OrdinalIgnoreCase)
                             .Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? name.Trim() : cleaned;
    }

    private static DeviceType DetectDeviceType(string? name, string? id)
    {
        string full = ((name ?? "") + " " + (id ?? "")).ToLowerInvariant();

        if (full.Contains("xbox") || full.Contains("controller") || full.Contains("gamepad") || full.Contains("dualshock") || full.Contains("dualsense"))
            return DeviceType.Gamepad;
        if (full.Contains("mouse") || full.Contains("fare"))
            return DeviceType.Mouse;
        if (full.Contains("keyboard") || full.Contains("klavye"))
            return DeviceType.Keyboard;
        if (full.Contains("airpod") || full.Contains("buds") || full.Contains("tws"))
            return DeviceType.Earbuds;
        if (full.Contains("headset") || full.Contains("headphone") || full.Contains("wh-"))
            return DeviceType.Headphones;
        if (full.Contains("speaker") || full.Contains("hoparlör") || full.Contains("jbl") || full.Contains("mifa") || full.Contains("soundcore"))
            return DeviceType.Speaker;
        if (full.Contains("iphone") || full.Contains("phone") || full.Contains("telefon") || full.Contains("galaxy s") || full.Contains("pixel") || full.Contains("xperia") || full.Contains("xiaomi") || full.Contains("huawei") || full.Contains("oneplus"))
            return DeviceType.Phone;
        if (full.Contains("watch") || full.Contains("saat") || full.Contains("band") || full.Contains("fit"))
            return DeviceType.Watch;
        if (full.Contains("pen") || full.Contains("stylus") || full.Contains("pencil"))
            return DeviceType.Stylus;

        return DeviceType.Generic;
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
