using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Microsoft.Win32;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Audio;

public interface IBluetoothAudioCodecDetector
{
    string? DetectCodec(BluetoothDeviceModel device, AudioDeviceInfo? endpoint = null);
}

public class BluetoothAudioCodecDetector : IBluetoothAudioCodecDetector
{
    public static IBluetoothAudioCodecDetector Default { get; set; } = new BluetoothAudioCodecDetector();

    private readonly Func<string, string, object?>? _registryReader;
    private readonly int _osBuildNumber;

    public BluetoothAudioCodecDetector(Func<string, string, object?>? registryReader = null, int? osBuildNumber = null)
    {
        _registryReader = registryReader;
        _osBuildNumber = osBuildNumber ?? Environment.OSVersion.Version.Build;
    }

    public string? DetectCodec(BluetoothDeviceModel device, AudioDeviceInfo? endpoint = null)
    {
        if (device == null) return null;
        if (!device.IsConnected) return null;

        // Yalnızca ses aktarabilen cihazlar (Kulaklık, Earbuds, Hoparlör vb.) için kodek geçerlidir
        bool isAudio = device.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker || endpoint != null || IsProbableAudioDevice(device);
        if (!isAudio) return null;

        // 1. Aşama: Alternatif A2DP Sürücüsü (Alternative A2DP Driver) Kayıt Defteri Kontrolü (HKCU ve HKLM)
        string? altDriverCodec = CheckAlternativeA2dpDriver(device);
        if (!string.IsNullOrEmpty(altDriverCodec))
        {
            return NormalizeCodecName(altDriverCodec);
        }

        // 2. Aşama: Ses Çıkış Noktası (Endpoint) ve Biçim Özellikleri Kontrolü
        if (endpoint != null)
        {
            string? epCodec = CheckEndpointProperties(endpoint);
            if (!string.IsNullOrEmpty(epCodec))
            {
                return NormalizeCodecName(epCodec);
            }
        }

        // 3. Aşama: Windows Kayıt Defteri BthA2dp ve Donanım Parametreleri (PnP A2DP Sink)
        string? regCodec = CheckWindowsA2dpRegistry(device);
        if (!string.IsNullOrEmpty(regCodec))
        {
            return NormalizeCodecName(regCodec);
        }

        // 4. Aşama: Windows Olay Günlükleri (Event Log / ETW A2dpStreaming) - Cihaza Özel Eşleşme
        string? eventLogCodec = CheckWindowsEventLogs(device);
        if (!string.IsNullOrEmpty(eventLogCodec))
        {
            return NormalizeCodecName(eventLogCodec);
        }

        // 5. Aşama: Eğer modelde önceden doğrulanmış bir kodek varsa onu koru; yoksa akıllı donanım ve OS müzakeresi yap
        if (!string.IsNullOrWhiteSpace(device.AudioCodec))
        {
            return NormalizeCodecName(device.AudioCodec);
        }

        return InferCodecFromDeviceAndOs(device);
    }

    private string? CheckAlternativeA2dpDriver(BluetoothDeviceModel device)
    {
        try
        {
            string[] baseKeys = { @"SOFTWARE\Alternative A2DP Driver", @"SOFTWARE\WOW6432Node\Alternative A2DP Driver" };
            ulong mac = device.BluetoothAddress != 0 ? device.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(device.Id);
            string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

            // Önce HKCU (Kullanıcı yapılandırması), sonra HKLM kontrol edilir
            RegistryHive[] hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };

            foreach (var hive in hives)
            {
                foreach (var baseKey in baseKeys)
                {
                    // Cihaza özel ayar
                    if (!string.IsNullOrEmpty(macHex))
                    {
                        string devSubKey = $@"{baseKey}\Devices\{macHex}";
                        var devCodec = ReadRegistryValue(devSubKey, "Codec", hive) ??
                                       ReadRegistryValue(devSubKey, "SelectedCodec", hive) ??
                                       ReadRegistryValue(devSubKey, "ActiveCodec", hive) ??
                                       ReadRegistryValue(devSubKey, "CurrentCodec", hive);
                        if (devCodec != null) return devCodec.ToString();
                    }

                    // Global aktif kodek
                    var globalCodec = ReadRegistryValue(baseKey, "ActiveCodec", hive) ??
                                      ReadRegistryValue(baseKey, "SelectedCodec", hive) ??
                                      ReadRegistryValue(baseKey, "Codec", hive) ??
                                      ReadRegistryValue(baseKey, "CurrentCodec", hive);
                    if (globalCodec != null) return globalCodec.ToString();
                }
            }
        }
        catch
        {
            // Registry okuma hatasını güvenle yut
        }

        return null;
    }

    private static string? CheckEndpointProperties(AudioDeviceInfo endpoint)
    {
        string text = $"{endpoint.Name} {endpoint.Description} {endpoint.DeviceInstanceId}".ToLowerInvariant();

        if (text.Contains("ldac")) return "LDAC";
        if (text.Contains("aptx hd") || text.Contains("aptx-hd")) return "aptX HD";
        if (text.Contains("aptx adaptive") || text.Contains("aptx-adaptive")) return "aptX HD";
        if (text.Contains("aptx ll") || text.Contains("aptx-ll")) return "aptX";
        if (text.Contains("aptx")) return "aptX";
        if (text.Contains("lhdc")) return "LHDC";
        if (text.Contains("aac")) return "AAC";
        if (text.Contains("sbc")) return "SBC";
        if (text.Contains("lc3")) return "LC3";

        return null;
    }

    private string? CheckWindowsA2dpRegistry(BluetoothDeviceModel device)
    {
        try
        {
            ulong mac = device.BluetoothAddress != 0 ? device.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(device.Id);
            string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

            string bthA2dpParams = @"SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters";

            if (!string.IsNullOrEmpty(macHex))
            {
                string devSubKey = $@"{bthA2dpParams}\Devices\{macHex}";
                var codecVal = ReadRegistryValue(devSubKey, "Codec") ??
                               ReadRegistryValue(devSubKey, "SelectedCodec") ??
                               ReadRegistryValue(devSubKey, "A2dpCodec");
                if (codecVal != null) return codecVal.ToString();

                // PnP A2DP Sink Cihaz Parametreleri Kontrolü (HKLM\SYSTEM\CurrentControlSet\Enum\BTHENUM\{0000110b-...})
                string? pnpCodec = CheckPnpA2dpKeys(macHex);
                if (!string.IsNullOrEmpty(pnpCodec)) return pnpCodec;
            }

            // AAC'nin Windows genelinde devre dışı bırakılıp bırakılmadığı kontrol edilir
            var aacEnableVal = ReadRegistryValue(bthA2dpParams, "BluetoothAacEnable") ?? ReadRegistryValue(bthA2dpParams, "AacEnable");
            if (aacEnableVal is int intAac && intAac == 0)
            {
                // AAC açıkça kapatılmış: aptX veya SBC'ye düşer
                return IsAptxCapableDevice(device) ? "aptX" : "SBC";
            }
        }
        catch
        {
            // Registry okuma hatası
        }

        return null;
    }

    private string? CheckPnpA2dpKeys(string macHex)
    {
        try
        {
            const string bthenumPath = @"SYSTEM\CurrentControlSet\Enum\BTHENUM";
            using var bthenumKey = Registry.LocalMachine.OpenSubKey(bthenumPath);
            if (bthenumKey == null) return null;

            foreach (var subName in bthenumKey.GetSubKeyNames())
            {
                // {0000110b-0000-1000-8000-00805f9b34fb} A2DP Audio Sink Service UUID'sidir
                if (!subName.Contains("{0000110b-", StringComparison.OrdinalIgnoreCase)) continue;

                using var devKey = bthenumKey.OpenSubKey(subName);
                if (devKey == null) continue;

                foreach (var instName in devKey.GetSubKeyNames())
                {
                    if (!instName.Contains(macHex, StringComparison.OrdinalIgnoreCase)) continue;

                    using var instKey = devKey.OpenSubKey(instName);
                    if (instKey == null) continue;

                    using var paramsKey = instKey.OpenSubKey("Device Parameters");
                    if (paramsKey != null)
                    {
                        var val = paramsKey.GetValue("Codec") ??
                                  paramsKey.GetValue("SelectedCodec") ??
                                  paramsKey.GetValue("A2dpCodec") ??
                                  paramsKey.GetValue("A2dpFormat");
                        if (val != null) return val.ToString();
                    }
                }
            }
        }
        catch
        {
            // PnP sorgulama hatası
        }

        return null;
    }

    private static string? CheckWindowsEventLogs(BluetoothDeviceModel device)
    {
        try
        {
            ulong mac = device.BluetoothAddress != 0 ? device.BluetoothAddress : BluetoothDeviceModel.ExtractMacAddress(device.Id);
            string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;
            string macColon = mac != 0 ? string.Join(":", Enumerable.Range(0, 6).Select(i => ((mac >> ((5 - i) * 8)) & 0xFF).ToString("X2"))) : string.Empty;
            string devName = (device.Name ?? string.Empty).Trim();

            // Cihazı ayırt edebilecek herhangi bir tanımlayıcı yoksa yanlış cihazın günlüğünü okumamak için çık
            if (string.IsNullOrEmpty(macHex) && string.IsNullOrEmpty(devName)) return null;

            string query = "*[System[Provider[@Name='Microsoft-Windows-Bluetooth-Policy' or @Name='Microsoft-Windows-BTH-BTHUSB' or @Name='Microsoft-Windows-Bluetooth-Audio'] and TimeCreated[timediff(@SystemTime) <= 86400000]]]";
            using var reader = new EventLogReader(new EventLogQuery("Microsoft-Windows-Bluetooth-Policy/Operational", PathType.LogName, query));

            for (var ev = reader.ReadEvent(); ev != null; ev = reader.ReadEvent())
            {
                using (ev)
                {
                    string xml = ev.ToXml();
                    if (string.IsNullOrEmpty(xml)) continue;

                    // Olayın kesinlikle bu cihaza ait olduğunu doğrula (MAC veya belirgin isim eşleşmesi)
                    bool belongsToDevice = false;
                    if (!string.IsNullOrEmpty(macHex) && xml.Contains(macHex, StringComparison.OrdinalIgnoreCase)) belongsToDevice = true;
                    else if (!string.IsNullOrEmpty(macColon) && xml.Contains(macColon, StringComparison.OrdinalIgnoreCase)) belongsToDevice = true;
                    else if (!string.IsNullOrEmpty(devName) && devName.Length >= 4 && !BluetoothDeviceModel.IsGenericName(devName) && xml.Contains(devName, StringComparison.OrdinalIgnoreCase)) belongsToDevice = true;

                    if (!belongsToDevice) continue;

                    if (xml.Contains("0x012D", StringComparison.OrdinalIgnoreCase) || xml.Contains("LDAC", StringComparison.OrdinalIgnoreCase))
                        return "LDAC";
                    if (xml.Contains("0x00D7", StringComparison.OrdinalIgnoreCase) || xml.Contains("aptX HD", StringComparison.OrdinalIgnoreCase))
                        return "aptX HD";
                    if (xml.Contains("0x004F", StringComparison.OrdinalIgnoreCase) || xml.Contains("aptX", StringComparison.OrdinalIgnoreCase))
                        return "aptX";
                    if (xml.Contains("AAC", StringComparison.OrdinalIgnoreCase) || xml.Contains("AacCodec", StringComparison.OrdinalIgnoreCase) || xml.Contains("0x0002", StringComparison.OrdinalIgnoreCase))
                        return "AAC";
                    if (xml.Contains("SBC", StringComparison.OrdinalIgnoreCase) || xml.Contains("0x0000", StringComparison.OrdinalIgnoreCase))
                        return "SBC";
                }
            }
        }
        catch
        {
            // Olay günlüğü erişim izni yoksa veya kanal bulunamadıysa sessizce geçilir
        }

        return null;
    }

    public static bool IsProbableAudioDevice(BluetoothDeviceModel device)
    {
        string text = $"{device.Name} {device.ModelName}".ToLowerInvariant();
        return text.Contains("headphone") || text.Contains("headset") || text.Contains("earbud") ||
               text.Contains("airpod") || text.Contains("buds") || text.Contains("sound") ||
               text.Contains("speaker") || text.Contains("audio") || text.Contains("wh-") ||
               text.Contains("wf-") || text.Contains("freebuds") || text.Contains("anc");
    }

    private string InferCodecFromDeviceAndOs(BluetoothDeviceModel device)
    {
        string name = (device.Name ?? string.Empty).Trim();
        string model = (device.ModelName ?? string.Empty).Trim();
        string combined = $"{name} {model}".Trim();

        // 1. Apple ve Beats Ailesi (Yalnızca AAC ve SBC destekler)
        if (IsAppleOrBeatsAudio(combined))
        {
            return _osBuildNumber >= 19044 ? "AAC" : "SBC";
        }

        // 2. Sony Kulaklıklar (LDAC, AAC, SBC; eski modellerde aptX)
        if (IsSonyAudio(combined))
        {
            return _osBuildNumber >= 19044 ? "AAC" : "SBC";
        }

        // 3. Samsung Galaxy Buds Ailesi
        if (IsSamsungBuds(combined))
        {
            return _osBuildNumber >= 19044 ? "AAC" : "SBC";
        }

        // 4. Bose Kulaklıklar (QC35, QC45, NC700 vb. AAC destekler)
        if (IsBoseAudio(combined))
        {
            return _osBuildNumber >= 19044 ? "AAC" : "SBC";
        }

        // 5. Sennheiser / B&W / aptX Yüksek Kalite Cihazlar
        if (IsAptxCapableDevice(device))
        {
            if (combined.Contains("HD", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Momentum 4", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("PX7", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("PX8", StringComparison.OrdinalIgnoreCase))
            {
                return "aptX HD";
            }
            return "aptX";
        }

        // 6. Modern Windows Genel Fallback:
        // Windows 10 21H2 ve Windows 11 (Build >= 19044) standart olarak modern A2DP kulaklıklarda AAC'yi varsayılan yapar.
        return _osBuildNumber >= 19044 ? "AAC" : "SBC";
    }

    private static bool IsAppleOrBeatsAudio(string name)
    {
        return name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("AirPods Pro", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("AirPods Max", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Beats", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Solo Pro", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Studio Pro", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSonyAudio(string name)
    {
        return name.Contains("WH-1000XM", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("WF-1000XM", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("LinkBuds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("WH-CH", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("WF-C", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("WI-1000X", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MDR-", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Sony", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSamsungBuds(string name)
    {
        return name.Contains("Galaxy Buds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Buds Pro", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Buds Live", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Buds2", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Buds FE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoseAudio(string name)
    {
        return name.Contains("Bose", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("QuietComfort", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("QC35", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("QC45", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("NC 700", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("SoundLink", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAptxCapableDevice(BluetoothDeviceModel device)
    {
        string name = $"{device.Name} {device.ModelName}".ToLowerInvariant();
        return name.Contains("aptx") ||
               name.Contains("sennheiser") ||
               name.Contains("momentum") ||
               name.Contains("bowers & wilkins") ||
               name.Contains("b&w") ||
               name.Contains("audio-technica") ||
               name.Contains("m50xbt") ||
               name.Contains("shure") ||
               name.Contains("fiio") ||
               name.Contains("b&o") ||
               name.Contains("beoplay");
    }

    public static string NormalizeCodecName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "SBC";

        string trimmed = raw.Trim();
        string lower = trimmed.ToLowerInvariant();

        if (lower.Contains("ldac") || lower == "4") return "LDAC";
        if (lower.Contains("aptx hd") || lower.Contains("aptx-hd") || lower == "3") return "aptX HD";
        if (lower.Contains("aptx") || lower == "2") return "aptX";
        if (lower.Contains("aac") || lower == "1") return "AAC";
        if (lower.Contains("lhdc") || lower == "5") return "LHDC";
        if (lower.Contains("sbc") || lower == "0") return "SBC";

        return trimmed.ToUpperInvariant();
    }

    private object? ReadRegistryValue(string subKey, string valueName, RegistryHive hive = RegistryHive.LocalMachine)
    {
        if (_registryReader != null)
        {
            return _registryReader(subKey, valueName);
        }

        try
        {
            using var baseKey = hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName);
        }
        catch
        {
            return null;
        }
    }
}
