using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BluetoothBatteryMonitor.App.Models;

public class AppSettings
{
    public bool LaunchAtStartup { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public int CriticalBatteryThreshold { get; set; } = 10;
    public int RefreshIntervalSeconds { get; set; } = 60;
    public bool ShowBatteryPercentageInTray { get; set; } = true;
    public bool AutoSwitchAudioEndpoint { get; set; } = true;
    public bool EnableRestApi { get; set; } = true;
    public int RestApiPort { get; set; } = 23253;
    public string Theme { get; set; } = "System"; // "System", "Dark", "Light"
    public string Language { get; set; } = "en"; // "en", "tr"

    public Dictionary<string, int> DeviceSpecificThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cihaza özel eşik tanımlıysa onu döner, tanımlı değilse global LowBatteryThreshold değerini döner.
    /// </summary>
    public int GetEffectiveLowBatteryThreshold(string? deviceId, ulong macAddress = 0)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            if (DeviceSpecificThresholds.TryGetValue(deviceId, out int customThreshold))
            {
                return customThreshold;
            }

            ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
            if (mac != 0)
            {
                string macHex = mac.ToString("X12");
                if (DeviceSpecificThresholds.TryGetValue(macHex, out int macThreshold))
                {
                    return macThreshold;
                }
            }
        }
        else if (macAddress != 0)
        {
            string macHex = macAddress.ToString("X12");
            if (DeviceSpecificThresholds.TryGetValue(macHex, out int macThreshold))
            {
                return macThreshold;
            }
        }

        return LowBatteryThreshold;
    }

    public bool HasCustomThreshold(string? deviceId, ulong macAddress = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (DeviceSpecificThresholds.ContainsKey(deviceId)) return true;

        ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
        if (mac != 0)
        {
            string macHex = mac.ToString("X12");
            if (DeviceSpecificThresholds.ContainsKey(macHex)) return true;
        }

        return false;
    }

    public void SetDeviceThreshold(string deviceId, int? threshold, ulong macAddress = 0, string? deviceName = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;

        ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
        string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

        if (threshold.HasValue && threshold.Value > 0)
        {
            DeviceSpecificThresholds[deviceId] = threshold.Value;
            if (!string.IsNullOrEmpty(macHex))
            {
                DeviceSpecificThresholds[macHex] = threshold.Value;
            }

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                DeviceNames[deviceId] = deviceName;
                if (!string.IsNullOrEmpty(macHex))
                {
                    DeviceNames[macHex] = deviceName;
                }
            }
        }
        else
        {
            DeviceSpecificThresholds.Remove(deviceId);
            if (!string.IsNullOrEmpty(macHex))
            {
                DeviceSpecificThresholds.Remove(macHex);
            }
        }
        Save();
    }

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BluetoothBatteryMonitor");

    private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    if (settings.DeviceSpecificThresholds != null)
                    {
                        settings.DeviceSpecificThresholds = new Dictionary<string, int>(settings.DeviceSpecificThresholds, StringComparer.OrdinalIgnoreCase);
                    }
                    if (settings.DeviceNames != null)
                    {
                        settings.DeviceNames = new Dictionary<string, string>(settings.DeviceNames, StringComparer.OrdinalIgnoreCase);
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // Varsayılan ayarlarla devam edilir
        }

        return new AppSettings();
    }

    public static event EventHandler<AppSettings>? SettingsChanged;

    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);

            SettingsChanged?.Invoke(null, this);
        }
        catch
        {
            // Hata sessizce yutulabilir veya loglanabilir
        }
    }
}
