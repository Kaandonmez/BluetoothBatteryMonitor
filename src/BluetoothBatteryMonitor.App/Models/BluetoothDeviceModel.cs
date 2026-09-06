using System;

namespace BluetoothBatteryMonitor.App.Models;

public class BluetoothDeviceModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Bilinmeyen Cihaz";
    public string? ModelName { get; set; }
    public DeviceType DeviceType { get; set; } = DeviceType.Generic;
    public DeviceType Type { get => DeviceType; set => DeviceType = value; }

    public int? BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public bool IsConnected { get; set; } = false;
    public string ConnectionStatusText
    {
        get
        {
            if (IsConnected) return Services.Localization.LocalizationService.GetString("Device_Connected");
            if (IsTws && (IsLeftCharging || IsRightCharging || IsCaseCharging))
                return Services.Localization.LocalizationService.GetString("Device_ChargingInCase");
            return Services.Localization.LocalizationService.GetString("Device_Disconnected");
        }
    }
    public string? AudioCodec { get; set; }

    // TWS (Dedicated channels for AirPods, Beats, Galaxy Buds, etc.)
    public bool IsTws { get; set; }
    public int? LeftBatteryLevel { get; set; }
    public bool IsLeftCharging { get; set; }
    public int? RightBatteryLevel { get; set; }
    public bool IsRightCharging { get; set; }
    public int? CaseBatteryLevel { get; set; }
    public bool IsCaseCharging { get; set; }

    public string ProviderSource { get; set; } = "Unknown";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public ulong BluetoothAddress { get; set; }
    public ulong SecondaryBluetoothAddress { get; set; }
    public bool IsAggregated { get; set; }

    /// <summary>
    /// Returns effective minimum battery level (including left/right buds and case for TWS devices).
    /// </summary>
    public int? EffectiveBatteryLevel
    {
        get
        {
            if (!IsTws) return BatteryLevel;

            int? min = null;
            if (LeftBatteryLevel.HasValue && LeftBatteryLevel >= 0)
                min = LeftBatteryLevel.Value;

            if (RightBatteryLevel.HasValue && RightBatteryLevel >= 0)
                min = min.HasValue ? Math.Min(min.Value, RightBatteryLevel.Value) : RightBatteryLevel.Value;

            // Case battery should not override earbuds battery; only used as fallback if both buds are unavailable
            if (!min.HasValue && CaseBatteryLevel.HasValue && CaseBatteryLevel >= 0)
                min = CaseBatteryLevel.Value;

            return min ?? BatteryLevel;
        }
    }

    /// <summary>
    /// Checks whether two models represent the same physical Bluetooth device.
    /// Evaluates MAC address matching, dual-mode Classic BT/BLE compatibility, cross-provider PnP/GATT transitions,
    /// and specific friendly name normalization rules.
    /// </summary>
    public bool Matches(BluetoothDeviceModel other)
    {
        if (other == null) return false;

        // 1. Direct unique Id match
        if (!string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(other.Id) &&
            string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Detect MAC addresses
        ulong mac1 = BluetoothAddress != 0 ? BluetoothAddress : ExtractMacAddress(Id);
        ulong mac2 = other.BluetoothAddress != 0 ? other.BluetoothAddress : ExtractMacAddress(other.Id);

        // 2. Exact Bluetooth MAC address match (identical hardware MAC is guaranteed to be the same physical device)
        if (mac1 != 0 && mac2 != 0 && mac1 == mac2)
        {
            return true;
        }

        // 3. Non-generic cleaned friendly name match
        string name1 = CleanNameForComparison(Name);
        string name2 = CleanNameForComparison(other.Name);
        if (!IsGenericName(name1) && !IsGenericName(name2))
        {
            // 3.a Identical normalized name
            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
            {
                // If one MAC is unknown (0), identical, or adjacent (dual-mode chipset), treat as the same physical device
                if (mac1 == 0 || mac2 == 0 || mac1 == mac2 || IsAdjacentMac(mac1, mac2))
                {
                    return true;
                }

                // Cross-profile or provider: Classic/HFP vs BLE/GATT, or Windows PnP vs BLE GATT/Custom Provider
                if (IsCrossTransportOrProvider(this, other))
                {
                    return true;
                }

                // Mobile / Apple devices using RPA (Random Private Address): Classic MAC differs from BLE MAC by hardware design
                if (IsAppleOrMobileDevice(name1, Id) || IsAppleOrMobileDevice(name2, other.Id))
                {
                    return true;
                }
            }

            // 3.b Substring or variant matching (e.g., "AirPods Pro - Find My" with "AirPods Pro")
            // CAUTION: Two devices with different hardware MACs must NEVER be merged via substring similarity!
            bool macsConflict = (mac1 != 0 && mac2 != 0 && mac1 != mac2 && !IsAdjacentMac(mac1, mac2));
            if (!macsConflict && name1.Length >= 4 && name2.Length >= 4 &&
                (DeviceType == other.DeviceType || DeviceType == DeviceType.Generic || other.DeviceType == DeviceType.Generic))
            {
                if (AreCompatibleModelVariants(name1, name2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks for adjacent MAC addresses allocated by dual-mode chipsets (e.g., MAC and MAC+1 for BR/EDR and BLE).
    /// </summary>
    public static bool IsAdjacentMac(ulong m1, ulong m2)
    {
        if (m1 == 0 || m2 == 0) return false;
        ulong diff = m1 > m2 ? m1 - m2 : m2 - m1;
        return diff <= 3;
    }

    /// <summary>
    /// Checks if a device uses Classic Bluetooth / HFP or audio profile.
    /// </summary>
    public static bool IsClassicOrHfp(BluetoothDeviceModel dev)
    {
        if (dev == null) return false;
        string src = dev.ProviderSource ?? string.Empty;
        string id = dev.Id ?? string.Empty;

        return src.Contains("HFP", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("AVRCP", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("Classic", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("Klasik", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrEmpty(dev.AudioCodec);
    }

    /// <summary>
    /// Checks if a device uses BLE GATT or Low Energy profile.
    /// </summary>
    public static bool IsBleOrGatt(BluetoothDeviceModel dev)
    {
        if (dev == null) return false;
        string src = dev.ProviderSource ?? string.Empty;
        string id = dev.Id ?? string.Empty;
        string name = dev.Name ?? string.Empty;

        return src.Contains("GATT", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("BLE", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("Beacon", StringComparison.OrdinalIgnoreCase) ||
               src.Contains("Fast Pair", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
               id.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(" LE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if two records arrive via complementary dual-mode transport layers (Classic vs BLE or PnP vs GATT).
    /// </summary>
    public static bool IsCrossTransportOrProvider(BluetoothDeviceModel a, BluetoothDeviceModel b)
    {
        if (a == null || b == null) return false;

        // One Classic/HFP and the other BLE/GATT
        if ((IsClassicOrHfp(a) && IsBleOrGatt(b)) || (IsClassicOrHfp(b) && IsBleOrGatt(a)))
        {
            return true;
        }

        // One Windows PnP and the other BLE GATT or custom provider
        string srcA = a.ProviderSource ?? string.Empty;
        string srcB = b.ProviderSource ?? string.Empty;
        bool aIsPnp = srcA.Contains("PnP", StringComparison.OrdinalIgnoreCase);
        bool bIsPnp = srcB.Contains("PnP", StringComparison.OrdinalIgnoreCase);
        bool aIsGatt = srcA.Contains("GATT", StringComparison.OrdinalIgnoreCase);
        bool bIsGatt = srcB.Contains("GATT", StringComparison.OrdinalIgnoreCase);

        if ((aIsPnp && bIsGatt) || (bIsPnp && aIsGatt))
        {
            return true;
        }

        // Within Windows PnP, one BTHENUM and the other BTHLE
        string idA = a.Id ?? string.Empty;
        string idB = b.Id ?? string.Empty;
        if ((idA.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) && idB.Contains("BTHLE", StringComparison.OrdinalIgnoreCase)) ||
            (idB.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) && idA.Contains("BTHLE", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool AreCompatibleModelVariants(string name1, string name2)
    {
        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;
        if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase)) return true;

        string longer = name1.Length >= name2.Length ? name1 : name2;
        string shorter = name1.Length < name2.Length ? name1 : name2;

        if (!longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string diff = longer.Substring(shorter.Length).Trim();
        if (string.IsNullOrEmpty(diff)) return true;

        // If diff contains model-distinguishing terms (e.g. "Pro", "Max", "Plus", "Mini", "Ultra", "Lite", "3S" vs "3"), they are distinct models!
        var forbiddenWords = new[] { "pro", "max", "plus", "mini", "ultra", "lite", "se", "fe", "sport", "active" };
        var diffWords = diff.Split(new[] { ' ', '-', '_', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in diffWords)
        {
            if (forbiddenWords.Contains(word.ToLowerInvariant()) || (word.Length > 0 && char.IsDigit(word[0])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether the device is an Apple device (iPhone, iPad, etc.) or a smartphone utilizing RPA.
    /// Excludes audio equipment such as "headphone", "earphone", "microphone".
    /// </summary>
    public static bool IsAppleOrMobileDevice(string? name, string? id)
    {
        string full = ((name ?? string.Empty) + " " + (id ?? string.Empty)).ToLowerInvariant();
        return full.Contains("iphone") ||
               full.Contains("ipad") ||
               full.Contains("ipod") ||
               full.Contains("apple watch") ||
               full.Contains("galaxy s") ||
               full.Contains("galaxy z") ||
               full.Contains("galaxy note") ||
               full.Contains("galaxy a") ||
               full.Contains("pixel") ||
               full.Contains("xperia") ||
               full.Contains("xiaomi") ||
               full.Contains("redmi") ||
               full.Contains("huawei") ||
               full.Contains("oneplus") ||
               full.Contains("telefon") ||
               System.Text.RegularExpressions.Regex.IsMatch(full, @"(?<!head|ear|micro)phone");
    }

    public static ulong ExtractMacAddress(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;

        // 1. Windows UWP BluetoothLE format: BluetoothLE#BluetoothLE<adapter>-<device>
        // Target peripheral MAC address is after the adapter (following the dash).
        int bleIdx = id.IndexOf("BluetoothLE#BluetoothLE", StringComparison.OrdinalIgnoreCase);
        if (bleIdx >= 0)
        {
            string after = id.Substring(bleIdx + "BluetoothLE#BluetoothLE".Length);
            // Format 1: 17-char adapter (00:11:22:33:44:55 or 00-11-22-33-44-55) + '-' + device
            if (after.Length >= 18 && after[17] == '-')
            {
                string devPart = after.Substring(18);
                string cleanDev = devPart.Replace(":", "").Replace("-", "");
                if (cleanDev.Length >= 12) cleanDev = cleanDev.Substring(0, 12);
                if (ulong.TryParse(cleanDev, System.Globalization.NumberStyles.HexNumber, null, out ulong bleMac) && IsValidMac(bleMac))
                {
                    return bleMac;
                }
            }
            // Format 2: 12-char adapter (001122334455) + '-' + device
            if (after.Length >= 13 && after[12] == '-')
            {
                string devPart = after.Substring(13);
                string cleanDev = devPart.Replace(":", "").Replace("-", "");
                if (cleanDev.Length >= 12) cleanDev = cleanDev.Substring(0, 12);
                if (ulong.TryParse(cleanDev, System.Globalization.NumberStyles.HexNumber, null, out ulong bleMac) && IsValidMac(bleMac))
                {
                    return bleMac;
                }
            }
        }

        // 2. Device instance paths (BTHENUM, BTHLE, DEV_...): collect candidate MAC matches
        var candidates = new List<(int Index, ulong Mac)>();

        // 2.a Colon-separated MAC: 00:11:22:33:44:55
        var colonMatches = System.Text.RegularExpressions.Regex.Matches(id, @"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})");
        foreach (System.Text.RegularExpressions.Match match in colonMatches)
        {
            string cleanHex = match.Value.Replace(":", "");
            if (ulong.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out ulong mac) && IsValidMac(mac))
            {
                candidates.Add((match.Index, mac));
            }
        }

        // 2.b Dash-separated MAC: 00-11-22-33-44-55
        var dashMatches = System.Text.RegularExpressions.Regex.Matches(id, @"([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})");
        foreach (System.Text.RegularExpressions.Match match in dashMatches)
        {
            string cleanHex = match.Value.Replace("-", "");
            if (ulong.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out ulong mac) && IsValidMac(mac))
            {
                candidates.Add((match.Index, mac));
            }
        }

        // 2.c Strip Windows GUIDs ({...})
        string withoutGuids = System.Text.RegularExpressions.Regex.Replace(id, @"\{[0-9a-fA-F\-]{36}\}", "");

        // 2.d Match DEV_<MAC>, &<MAC>_, _<MAC>_ or continuous 12-char hex MAC
        var devMatches = System.Text.RegularExpressions.Regex.Matches(withoutGuids, @"(?:DEV_|&|_)([0-9a-fA-F]{12})(?:_|\b|\\|#|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in devMatches)
        {
            if (ulong.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out ulong mac) && IsValidMac(mac))
            {
                candidates.Add((match.Index, mac));
            }
        }

        // 2.e Standalone 12-char hex outside GUIDs
        var hexMatches = System.Text.RegularExpressions.Regex.Matches(withoutGuids, @"\b([0-9a-fA-F]{12})\b");
        foreach (System.Text.RegularExpressions.Match match in hexMatches)
        {
            if (ulong.TryParse(match.Value, System.Globalization.NumberStyles.HexNumber, null, out ulong mac) && IsValidMac(mac))
            {
                candidates.Add((match.Index, mac));
            }
        }

        if (candidates.Count > 0)
        {
            // Pick right-most valid endpoint MAC in string
            return candidates.OrderByDescending(c => c.Index).First().Mac;
        }

        return 0;
    }

    private static bool IsValidMac(ulong mac)
    {
        // 0 and Bluetooth SIG Base UUID (0x00805F9B34FB) are invalid hardware MACs
        return mac != 0 && mac != 0x00805F9B34FB;
    }

    public static string CleanNameForComparison(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Normalize smart quotes and apostrophes
        string cleaned = name.Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'');

        cleaned = cleaned
            .Replace(" Hands-Free AG", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Hands-Free HF Audio", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Hands-Free Audio", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Hands-Free", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" (Hands-Free)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Handsfree", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" HF Audio", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" HF", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Avrcp Transport", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" A2DP SNK", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Stereo", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Logitech ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("®", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" (LE)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" - Find My", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        // Normalize Generation suffixes (e.g., " (2. Nesil)", " (2nd Generation)", " (Gen 2)")
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*\(\s*\d+[\.\s]*(?:Nesil|Gen|Generation)\s*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*\(\s*\d+[a-zA-Z]{2}\s+(?:Nesil|Gen|Generation)\s*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Strip dual-mode LE prefix and suffix
        if (cleaned.StartsWith("LE_", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("LE-", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("LE ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(3).Trim();
        }
        if (cleaned.EndsWith(" LE", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith("-LE", StringComparison.OrdinalIgnoreCase) ||
            cleaned.EndsWith("_LE", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }

        return cleaned.Trim();
    }

    public static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.Trim().ToLowerInvariant();
        return lower is "bilinmeyen cihaz" or
                        "unknown device" or
                        "bluetooth aygıtı" or
                        "bluetooth cihazı" or
                        "bluetooth device" or
                        "bluetooth" or
                        "ble cihazı" or
                        "ble cihaz" or
                        "ble device" or
                        "apple cihazı" or
                        "apple device" or
                        "logitech cihazı" or
                        "wireless controller" or
                        "generic" or
                        "generic bluetooth adapter" or
                        "kablosuz oyun kumandası" or
                        "gamepad" or
                        "wireless device" or
                        "hands-free audio" or
                        "audio device";
    }
}
