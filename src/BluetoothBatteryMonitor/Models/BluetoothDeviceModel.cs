using System;

namespace BluetoothBatteryMonitor.Models;

/// <summary>
/// Model representing a Bluetooth device detected on the system.
/// </summary>
public class BluetoothDeviceModel
{
    /// <summary>
    /// Unique device identifier (Windows DeviceId or Bluetooth MAC address).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Device name (e.g. "Xbox Wireless Controller", "AirPods Pro", "Logitech MX Master 3").
    /// </summary>
    public string Name { get; set; } = "Bilinmeyen Cihaz";

    /// <summary>
    /// Device type (Headphones, Mouse, Keyboard, Gamepad, etc.).
    /// </summary>
    public DeviceType Type { get; set; } = DeviceType.Unknown;

    /// <summary>
    /// Whether the device is currently connected.
    /// </summary>
    public bool IsConnected { get; set; }
    public string? AudioCodec { get; set; }

    /// <summary>
    /// Battery information for the device.
    /// </summary>
    public BatteryInfo Battery { get; set; } = new();

    /// <summary>
    /// Engine providing the battery data ("BLE GATT", "Windows PnP", "Apple AirPods Beacon", "Logitech HID++").
    /// </summary>
    public string ProviderSource { get; set; } = "Bilinmiyor";

    /// <summary>
    /// Timestamp of when the device was last seen active.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>
    /// Bluetooth hardware address if available (in ulong MAC format).
    /// Used by different services (PnP, GATT, Beacon) to match the same device.
    /// </summary>
    public ulong BluetoothAddress { get; set; }
    public ulong SecondaryBluetoothAddress { get; set; }

    /// <summary>
    /// Status text ("Connected" or "Disconnected / Last Seen Battery").
    /// </summary>
    public string ConnectionStatusText => IsConnected ? "Bağlı" : (Battery.EffectiveLowestLevel.HasValue ? "Bağlı Değil (Son Görülen Pil)" : "Bağlı Değil");

    /// <summary>
    /// Checks whether two device records represent the same physical device.
    /// Intelligently matches MAC address, unique DeviceId, and distinctive device names.
    /// </summary>
    public bool Matches(BluetoothDeviceModel other)
    {
        if (other == null) return false;

        // 1. Direct ulong Bluetooth MAC address match
        if (BluetoothAddress != 0 && other.BluetoothAddress != 0 && BluetoothAddress == other.BluetoothAddress)
        {
            return true;
        }

        // 2. Exact DeviceId match
        if (!string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(other.Id) &&
            string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. 48-bit MAC address detection and matching from Id string
        ulong mac1 = BluetoothAddress != 0 ? BluetoothAddress : ExtractMacAddress(Id);
        ulong mac2 = other.BluetoothAddress != 0 ? other.BluetoothAddress : ExtractMacAddress(other.Id);
        if (mac1 != 0 && mac2 != 0 && mac1 == mac2)
        {
            return true;
        }

        // 4. Distinctive non-generic name match
        if (!IsGenericName(Name) && !IsGenericName(other.Name) &&
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            (Type == other.Type || Type == DeviceType.Unknown || other.Type == DeviceType.Unknown))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the 48-bit Bluetooth MAC address within DeviceId.
    /// </summary>
    public static ulong ExtractMacAddress(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;

        // 00:11:22:33:44:55 or 00-11-22-33-44-55 format
        var colonMatch = System.Text.RegularExpressions.Regex.Match(id, @"([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})");
        if (colonMatch.Success)
        {
            string cleanHex = colonMatch.Value.Replace(":", "").Replace("-", "");
            if (ulong.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out ulong macFromColon))
            {
                return macFromColon;
            }
        }

        // Dev_001BDC073185 or AirPods_001BDC073185 contiguous 12-digit hex
        var hexMatch = System.Text.RegularExpressions.Regex.Match(id, @"([0-9a-fA-F]{12})");
        if (hexMatch.Success && ulong.TryParse(hexMatch.Value, System.Globalization.NumberStyles.HexNumber, null, out ulong macFromHex))
        {
            return macFromHex;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether the given name is a generic placeholder.
    /// </summary>
    public static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.Trim().ToLowerInvariant();
        return lower is "bilinmeyen cihaz" or "bluetooth aygıtı" or "bluetooth cihazı" or "ble cihazı" or "apple cihazı" or "logitech cihazı" or "bluetooth device";
    }
}
