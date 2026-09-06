namespace BluetoothBatteryMonitor.Models;

/// <summary>
/// Bluetooth device types.
/// </summary>
public enum DeviceType
{
    Unknown,
    Headset,
    Headphones,
    Earbuds,
    Mouse,
    Keyboard,
    Gamepad,
    Phone,
    Speaker,
    Watch,
    Stylus,
    Other
}

/// <summary>
/// Extension methods for device type (icon glyph, localized name).
/// Uses standard character codes from Segoe Fluent Icons / Segoe MDL2 Assets.
/// </summary>
public static class DeviceTypeExtensions
{
    public static string GetGlyph(this DeviceType type) => type switch
    {
        DeviceType.Headphones => "\uE7F6", // Headphones
        DeviceType.Earbuds => "\uE7F6",    // Earbuds
        DeviceType.Headset => "\uE95F",    // Headset with mic
        DeviceType.Mouse => "\uE962",      // Mouse
        DeviceType.Keyboard => "\uE765",   // Keyboard
        DeviceType.Gamepad => "\uE7FC",    // Game Controller
        DeviceType.Phone => "\uE8EA",      // Phone
        DeviceType.Speaker => "\uE7F5",    // Speaker
        DeviceType.Watch => "\uE919",      // Watch
        DeviceType.Stylus => "\uEDC6",     // Pen / Stylus
        _ => "\uE702"                      // Bluetooth symbol
    };

    public static string GetDisplayName(this DeviceType type) => type switch
    {
        DeviceType.Headphones => "Kulaklık",
        DeviceType.Earbuds => "TWS Kulaklık",
        DeviceType.Headset => "Mikrofonlu Kulaklık",
        DeviceType.Mouse => "Fare",
        DeviceType.Keyboard => "Klavye",
        DeviceType.Gamepad => "Oyun Kolu",
        DeviceType.Phone => "Telefon",
        DeviceType.Speaker => "Hoparlör",
        DeviceType.Watch => "Akıllı Saat",
        DeviceType.Stylus => "Dokunmatik Kalem",
        _ => "Bluetooth Cihazı"
    };
}
