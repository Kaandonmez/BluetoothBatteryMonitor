using System;

namespace BluetoothBatteryMonitor.Models;

/// <summary>
/// Sistemde algılanan Bluetooth cihazının modeli.
/// </summary>
public class BluetoothDeviceModel
{
    /// <summary>
    /// Benzersiz cihaz kimliği (Windows DeviceId veya Bluetooth MAC adresi).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Cihaz adı (örn. "Xbox Wireless Controller", "AirPods Pro", "Logitech MX Master 3").
    /// </summary>
    public string Name { get; set; } = "Bilinmeyen Cihaz";

    /// <summary>
    /// Cihaz türü (Kulaklık, Fare, Klavye, Gamepad vb.).
    /// </summary>
    public DeviceType Type { get; set; } = DeviceType.Unknown;

    /// <summary>
    /// Cihazın şu an bağlı olup olmadığı.
    /// </summary>
    public bool IsConnected { get; set; }
    public string? AudioCodec { get; set; }

    /// <summary>
    /// Cihaza ait pil bilgisi.
    /// </summary>
    public BatteryInfo Battery { get; set; } = new();

    /// <summary>
    /// Pil verisini sağlayan motor ("BLE GATT", "Windows PnP", "Apple AirPods Beacon", "Logitech HID++").
    /// </summary>
    public string ProviderSource { get; set; } = "Bilinmiyor";

    /// <summary>
    /// Cihazın en son aktif görüldüğü zaman.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>
    /// Varsa Bluetooth donanım adresi (MAC ulong formatında).
    /// Farklı servislerin (PnP, GATT, Beacon) aynı cihazı eşleştirmesi için kullanılır.
    /// </summary>
    public ulong BluetoothAddress { get; set; }
    public ulong SecondaryBluetoothAddress { get; set; }

    /// <summary>
    /// Durum metni ("Bağlı" veya "Bağlı Değil / Son Görülen Pil").
    /// </summary>
    public string ConnectionStatusText => IsConnected ? "Bağlı" : (Battery.EffectiveLowestLevel.HasValue ? "Bağlı Değil (Son Görülen Pil)" : "Bağlı Değil");

    /// <summary>
    /// İki cihaz kaydının aynı fiziksel cihazı temsil edip etmediğini kontrol eder.
    /// MAC adresi, benzersiz DeviceId ve ayırt edici cihaz isimlerini akıllıca eşleştirir.
    /// </summary>
    public bool Matches(BluetoothDeviceModel other)
    {
        if (other == null) return false;

        // 1. Doğrudan ulong Bluetooth MAC adresi eşleşmesi
        if (BluetoothAddress != 0 && other.BluetoothAddress != 0 && BluetoothAddress == other.BluetoothAddress)
        {
            return true;
        }

        // 2. Birebir DeviceId eşleşmesi
        if (!string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(other.Id) &&
            string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Id string'i içerisinden 48-bit MAC adresi tespiti ve eşleşmesi
        ulong mac1 = BluetoothAddress != 0 ? BluetoothAddress : ExtractMacAddress(Id);
        ulong mac2 = other.BluetoothAddress != 0 ? other.BluetoothAddress : ExtractMacAddress(other.Id);
        if (mac1 != 0 && mac2 != 0 && mac1 == mac2)
        {
            return true;
        }

        // 4. Jenerik olmayan ayırt edici isim eşleşmesi
        if (!IsGenericName(Name) && !IsGenericName(other.Name) &&
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            (Type == other.Type || Type == DeviceType.Unknown || other.Type == DeviceType.Unknown))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// DeviceId içerisindeki 48-bit Bluetooth MAC adresini ayrıştırır.
    /// </summary>
    public static ulong ExtractMacAddress(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;

        // 00:11:22:33:44:55 veya 00-11-22-33-44-55 formatı
        var colonMatch = System.Text.RegularExpressions.Regex.Match(id, @"([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})");
        if (colonMatch.Success)
        {
            string cleanHex = colonMatch.Value.Replace(":", "").Replace("-", "");
            if (ulong.TryParse(cleanHex, System.Globalization.NumberStyles.HexNumber, null, out ulong macFromColon))
            {
                return macFromColon;
            }
        }

        // Dev_001BDC073185 veya AirPods_001BDC073185 bitişik 12 haneli hex
        var hexMatch = System.Text.RegularExpressions.Regex.Match(id, @"([0-9a-fA-F]{12})");
        if (hexMatch.Success && ulong.TryParse(hexMatch.Value, System.Globalization.NumberStyles.HexNumber, null, out ulong macFromHex))
        {
            return macFromHex;
        }

        return 0;
    }

    /// <summary>
    /// Verilen ismin jenerik bir yer tutucu olup olmadığını denetler.
    /// </summary>
    public static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.Trim().ToLowerInvariant();
        return lower is "bilinmeyen cihaz" or "bluetooth aygıtı" or "bluetooth cihazı" or "ble cihazı" or "apple cihazı" or "logitech cihazı" or "bluetooth device";
    }
}
