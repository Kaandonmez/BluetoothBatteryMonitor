using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.Services.Localization;

namespace BluetoothBatteryMonitor.App.ViewModels;

public record ThresholdOption(int Value, string DisplayName);
public record RefreshIntervalOption(int Seconds, string DisplayName);

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IEnumerable<BluetoothDeviceModel>? _knownDevices;

    public static IReadOnlyList<ThresholdOption> AvailableThresholds => LocalizationService.CurrentLanguage == "tr"
        ? new[]
        {
            new ThresholdOption(0, "Varsayılan (Global)"),
            new ThresholdOption(10, "%10 (Fare / Düşük)"),
            new ThresholdOption(15, "%15"),
            new ThresholdOption(20, "%20 (Standart)"),
            new ThresholdOption(25, "%25"),
            new ThresholdOption(30, "%30"),
            new ThresholdOption(40, "%40 (Erken Uyarı)")
        }
        : new[]
        {
            new ThresholdOption(0, "Default (Global)"),
            new ThresholdOption(10, "10% (Mouse / Low Power)"),
            new ThresholdOption(15, "15%"),
            new ThresholdOption(20, "20% (Standard)"),
            new ThresholdOption(25, "25%"),
            new ThresholdOption(30, "30%"),
            new ThresholdOption(40, "40% (Early Warning)")
        };

    public static IReadOnlyList<RefreshIntervalOption> AvailableRefreshIntervals => LocalizationService.CurrentLanguage == "tr"
        ? new[]
        {
            new RefreshIntervalOption(30, "30 Saniye"),
            new RefreshIntervalOption(60, "60 Saniye"),
            new RefreshIntervalOption(120, "120 Saniye"),
            new RefreshIntervalOption(300, "300 Saniye")
        }
        : new[]
        {
            new RefreshIntervalOption(30, "30 Seconds"),
            new RefreshIntervalOption(60, "60 Seconds"),
            new RefreshIntervalOption(120, "120 Seconds"),
            new RefreshIntervalOption(300, "300 Seconds")
        };

    public SettingsViewModel(IEnumerable<BluetoothDeviceModel>? knownDevices = null)
    {
        _settings = AppSettings.Load();
        _knownDevices = knownDevices;

        _launchAtStartup = StartupManager.IsStartupEnabled();
        _enableNotifications = _settings.EnableNotifications;
        _lowBatteryThreshold = _settings.LowBatteryThreshold;
        _criticalBatteryThreshold = _settings.CriticalBatteryThreshold;
        _refreshIntervalSeconds = _settings.RefreshIntervalSeconds;
        _selectedTheme = _settings.Theme;
        _selectedLanguage = _settings.Language;
        _autoSwitchAudioEndpoint = _settings.AutoSwitchAudioEndpoint;
        _enableRestApi = _settings.EnableRestApi;
        _restApiPort = _settings.RestApiPort;

        PopulateDeviceThresholds(knownDevices);
    }

    private void PopulateDeviceThresholds(IEnumerable<BluetoothDeviceModel>? knownDevices)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Bilinen bağlı/eşleşmiş cihazları ekle
        if (knownDevices != null)
        {
            foreach (var dev in knownDevices)
            {
                if (string.IsNullOrWhiteSpace(dev.Id)) continue;

                ulong mac = dev.BluetoothAddress != 0 ? dev.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(dev.Id);
                string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

                if (seenKeys.Contains(dev.Id) || (!string.IsNullOrEmpty(macHex) && seenKeys.Contains(macHex)))
                    continue;

                string friendlyName = !string.IsNullOrWhiteSpace(dev.Name) && !BluetoothDeviceModel.IsGenericName(dev.Name)
                    ? dev.Name
                    : (_settings.DeviceNames.TryGetValue(dev.Id, out var sn) ? sn : dev.Name);

                // Aynı ada sahip cihazları (örn. hem BTHENUM hem BTHLE düğümü olan tek telefon) tekilleştir
                if (!string.IsNullOrWhiteSpace(friendlyName) && !BluetoothDeviceModel.IsGenericName(friendlyName))
                {
                    if (seenNames.Contains(friendlyName))
                    {
                        seenKeys.Add(dev.Id);
                        if (!string.IsNullOrEmpty(macHex)) seenKeys.Add(macHex);
                        continue;
                    }
                    seenNames.Add(friendlyName);
                }

                int currentThreshold = 0;
                if (_settings.DeviceSpecificThresholds.TryGetValue(dev.Id, out int t1))
                    currentThreshold = t1;
                else if (!string.IsNullOrEmpty(macHex) && _settings.DeviceSpecificThresholds.TryGetValue(macHex, out int t2))
                    currentThreshold = t2;

                DeviceThresholds.Add(new DeviceThresholdItem
                {
                    Id = dev.Id,
                    MacHex = macHex,
                    Name = friendlyName,
                    SelectedThreshold = currentThreshold
                });

                seenKeys.Add(dev.Id);
                if (!string.IsNullOrEmpty(macHex)) seenKeys.Add(macHex);
            }
        }

        // 2. Ayarlarda önceden kaydedilmiş diğer cihazları ekle
        foreach (var kvp in _settings.DeviceSpecificThresholds)
        {
            if (seenKeys.Contains(kvp.Key)) continue;

            string name = _settings.DeviceNames.TryGetValue(kvp.Key, out var sn) ? sn : kvp.Key;
            ulong mac = BluetoothDeviceModel.ExtractMacAddress(kvp.Key);
            string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

            DeviceThresholds.Add(new DeviceThresholdItem
            {
                Id = kvp.Key,
                MacHex = macHex,
                Name = name,
                SelectedThreshold = kvp.Value
            });

            seenKeys.Add(kvp.Key);
            if (!string.IsNullOrEmpty(macHex)) seenKeys.Add(macHex);
        }

        // 3. Eğer hiç cihaz bulunamadıysa Windows kayıt defterindeki eşleşmiş cihazları tara
        if (DeviceThresholds.Count == 0)
        {
            try
            {
                using var bthKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
                if (bthKey != null)
                {
                    foreach (var subName in bthKey.GetSubKeyNames())
                    {
                        using var devKey = bthKey.OpenSubKey(subName);
                        if (devKey != null)
                        {
                            string devName = subName;
                            var nameVal = devKey.GetValue("Name") as byte[];
                            if (nameVal != null && nameVal.Length > 0)
                            {
                                devName = Encoding.UTF8.GetString(nameVal).TrimEnd('\0');
                            }

                            if (!seenKeys.Contains(subName))
                            {
                                int th = _settings.GetEffectiveLowBatteryThreshold(subName);
                                DeviceThresholds.Add(new DeviceThresholdItem
                                {
                                    Id = subName,
                                    MacHex = subName,
                                    Name = devName,
                                    SelectedThreshold = _settings.HasCustomThreshold(subName) ? th : 0
                                });
                                seenKeys.Add(subName);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Registry okuma hatasını güvenle yut
            }
        }

        HasDeviceThresholds = DeviceThresholds.Count > 0;
    }

    [ObservableProperty]
    private ObservableCollection<DeviceThresholdItem> _deviceThresholds = new();

    [ObservableProperty]
    private bool _hasDeviceThresholds;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private bool _enableNotifications;

    [ObservableProperty]
    private bool _autoSwitchAudioEndpoint;

    [ObservableProperty]
    private bool _enableRestApi;

    [ObservableProperty]
    private int _restApiPort;

    partial void OnRestApiPortChanged(int value)
    {
        OnPropertyChanged(nameof(RestApiUrlPreview));
    }

    public string RestApiUrlPreview => $"http://127.0.0.1:{(RestApiPort >= 1024 && RestApiPort <= 65535 ? RestApiPort : 23253)}/devices";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LowBatteryThresholdText))]
    private int _lowBatteryThreshold;

    public string LowBatteryThresholdText => LocalizationService.FormatPercent(LowBatteryThreshold);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CriticalBatteryThresholdText))]
    private int _criticalBatteryThreshold;

    public string CriticalBatteryThresholdText => LocalizationService.FormatPercent(CriticalBatteryThreshold);

    [ObservableProperty]
    private int _refreshIntervalSeconds;

    [ObservableProperty]
    private string _selectedTheme;

    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "System", "Dark", "Light" };

    [ObservableProperty]
    private string _selectedLanguage;

    public IReadOnlyList<LanguageOption> AvailableLanguages => LocalizationService.SupportedLanguages;

    [RelayCommand]
    private void Save()
    {
        int validPort = (RestApiPort >= 1024 && RestApiPort <= 65535) ? RestApiPort : 23253;
        RestApiPort = validPort;

        var newThresholds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in DeviceThresholds)
        {
            if (item.SelectedThreshold > 0)
            {
                newThresholds[item.Id] = item.SelectedThreshold;
                if (!string.IsNullOrEmpty(item.MacHex))
                {
                    newThresholds[item.MacHex] = item.SelectedThreshold;
                }

                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    _settings.DeviceNames[item.Id] = item.Name;
                    if (!string.IsNullOrEmpty(item.MacHex))
                    {
                        _settings.DeviceNames[item.MacHex] = item.Name;
                    }
                }

                // Eğer aynı ada sahip başka bağlı cihaz düğümleri varsa (ör. çift modlu telefon) onlara da eşiği uygula
                if (_knownDevices != null && !string.IsNullOrWhiteSpace(item.Name) && !BluetoothDeviceModel.IsGenericName(item.Name))
                {
                    foreach (var other in _knownDevices.Where(d => string.Equals(d.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        newThresholds[other.Id] = item.SelectedThreshold;
                        ulong omac = other.BluetoothAddress != 0 ? other.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(other.Id);
                        if (omac != 0)
                        {
                            string omacHex = omac.ToString("X12");
                            newThresholds[omacHex] = item.SelectedThreshold;
                            _settings.DeviceNames[omacHex] = item.Name;
                        }
                        _settings.DeviceNames[other.Id] = item.Name;
                    }
                }
            }
            else
            {
                newThresholds.Remove(item.Id);
                if (!string.IsNullOrEmpty(item.MacHex))
                {
                    newThresholds.Remove(item.MacHex);
                }

                if (_knownDevices != null && !string.IsNullOrWhiteSpace(item.Name) && !BluetoothDeviceModel.IsGenericName(item.Name))
                {
                    foreach (var other in _knownDevices.Where(d => string.Equals(d.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        newThresholds.Remove(other.Id);
                        ulong omac = other.BluetoothAddress != 0 ? other.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(other.Id);
                        if (omac != 0) newThresholds.Remove(omac.ToString("X12"));
                    }
                }
            }
        }
        _settings.DeviceSpecificThresholds = newThresholds;

        _settings.EnableNotifications = EnableNotifications;
        _settings.AutoSwitchAudioEndpoint = AutoSwitchAudioEndpoint;
        _settings.EnableRestApi = EnableRestApi;
        _settings.RestApiPort = validPort;
        _settings.LowBatteryThreshold = LowBatteryThreshold;
        _settings.CriticalBatteryThreshold = CriticalBatteryThreshold;
        _settings.RefreshIntervalSeconds = RefreshIntervalSeconds;
        _settings.Theme = SelectedTheme;
        _settings.Language = SelectedLanguage;
        _settings.LaunchAtStartup = LaunchAtStartup;

        _settings.Save();

        // Başlangıç ayarını güncelle
        StartupManager.SetStartup(LaunchAtStartup);

        // Temayı uygula
        ThemeManager.ApplyTheme(SelectedTheme);

        // Dili uygula
        LocalizationService.ApplyLanguage(SelectedLanguage);
    }
}

public partial class DeviceThresholdItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string MacHex { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private int _selectedThreshold;
}
