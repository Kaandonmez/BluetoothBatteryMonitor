using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace BluetoothBatteryMonitor.App.Services.Localization;

public record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("tr", "Türkçe")
    };

    public static string CurrentLanguage { get; private set; } = "en";

    public static event EventHandler<string>? LanguageChanged;

    public static string FormatPercent(int value)
    {
        return CurrentLanguage == "tr" ? $"%{value}" : $"{value}%";
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Flyout
            ["App_Title"] = "Bluetooth Battery Monitor",
            ["Flyout_Header"] = "Bluetooth Devices",
            ["Flyout_RefreshTooltip"] = "Refresh Devices",
            ["Flyout_BluetoothSettings"] = "Bluetooth Settings",
            ["Flyout_AppSettingsTooltip"] = "Application Settings",
            ["Flyout_HideTooltip"] = "Hide",
            ["Flyout_NoDevicesTitle"] = "No Connected Bluetooth Devices",
            ["Flyout_NoDevicesDesc"] = "Turn on your Bluetooth device and connect it to your PC.",
            ["Flyout_Status_Connecting"] = "Connecting...",
            ["Flyout_Status_Scanning"] = "Scanning Bluetooth devices...",
            ["Flyout_Status_UpToDate"] = "All devices up to date",
            ["Flyout_Status_DevicesFound"] = "{0} device(s) connected",

            // Device Card
            ["Device_Connected"] = "Connected",
            ["Device_Disconnected"] = "Disconnected",
            ["Device_ChargingInCase"] = "In Case / Charging",
            ["Device_UnknownBattery"] = "Unknown",
            ["Device_Case"] = "Case: ",
            ["Device_CustomThresholdBadge"] = " • Alert: {0}%",
            ["Device_ThresholdDefault"] = "Default ({0}%)",
            ["Device_ThresholdCustom"] = "Custom Alert: {0}%",
            ["Device_MuteTooltip"] = "Mute / Unmute",
            ["Codec_LDAC"] = "Sony LDAC (High-Resolution Audio • 990 kbps / 96 kHz)",
            ["Codec_AptXHD"] = "Qualcomm aptX HD (High-Resolution Audio • 576 kbps / 48 kHz)",
            ["Codec_AptX"] = "Qualcomm aptX (Low Latency High Quality • 352 kbps)",
            ["Codec_AAC"] = "Advanced Audio Coding (Windows 11 / AAC HD Audio • 256 kbps)",
            ["Codec_SBC"] = "Subband Codec (Standard Bluetooth Audio)",
            ["Codec_Active"] = "Active Audio Codec: {0}",

            // Device Context Menu
            ["Menu_CustomThresholdHeader"] = "Set Custom Battery Alert",
            ["Menu_DefaultThreshold"] = "Default Alert (Global)",
            ["Menu_Threshold10"] = "10% (Mouse / Low Power)",
            ["Menu_Threshold15"] = "15%",
            ["Menu_Threshold20"] = "20% (Standard Headset)",
            ["Menu_Threshold25"] = "25%",
            ["Menu_Threshold30"] = "30%",
            ["Menu_Threshold40"] = "40% (Early Warning)",

            // Tray Menu
            ["Tray_OpenHide"] = "Open / Hide",
            ["Tray_Refresh"] = "Refresh Devices",
            ["Tray_LaunchAtStartup"] = "Launch with Windows",
            ["Tray_Settings"] = "Settings...",
            ["Tray_About"] = "About",
            ["Tray_Exit"] = "Exit",
            ["Tray_TooltipNoDevices"] = "Bluetooth Battery Monitor (No devices connected)",

            // Settings Window
            ["Settings_Title"] = "Settings - Bluetooth Battery Monitor",
            ["Settings_Header"] = "Application Settings",
            ["Settings_SubHeader"] = "Customize battery tracking and notification preferences.",
            ["Settings_LaunchAtStartup"] = "Launch with Windows",
            ["Settings_LaunchAtStartupDesc"] = "Start minimized in system tray on PC startup",
            ["Settings_Notifications"] = "Low Battery Notifications",
            ["Settings_NotificationsDesc"] = "Send Windows Toast alerts when battery is low",
            ["Settings_AudioRouter"] = "Smart Audio Router",
            ["Settings_AudioRouterDesc"] = "Automatically switch default audio device when headphones connect",
            ["Settings_RestApi"] = "Local REST API Server",
            ["Settings_RestApiDesc"] = "Serve battery data over local HTTP port for third-party tools",
            ["Settings_RestApiPort"] = "Listening Port (1024 - 65535)",
            ["Settings_LowThreshold"] = "Low Battery Alert Threshold",
            ["Settings_DeviceThresholds"] = "Device-Specific Thresholds",
            ["Settings_DeviceThresholdsDesc"] = "Custom warning levels per device (e.g. 10% for mouse, 25% for headphones)",
            ["Settings_DeviceThresholdsTip"] = "💡 Tip: You can also right-click any device card in the flyout window to set its threshold.",
            ["Settings_CriticalThreshold"] = "Critical Battery Alert Threshold",
            ["Settings_PollingFrequency"] = "Polling Frequency",
            ["Settings_PollingFrequencyDesc"] = "Time interval between battery checks",
            ["Settings_Theme"] = "Appearance Theme",
            ["Settings_ThemeDesc"] = "Choose application theme",
            ["Settings_Language"] = "Language",
            ["Settings_LanguageDesc"] = "Choose application language (English / Türkçe)",
            ["Settings_SaveAndClose"] = "Save & Close",

            // About Window
            ["About_Title"] = "About - Bluetooth Battery Monitor",
            ["About_AppName"] = "Bluetooth Battery Monitor",
            ["About_Version"] = "Version 1.0.0 (x64)",
            ["About_Overview"] = "Overview",
            ["About_OverviewText"] = "A modern Fluent Design desktop application for Windows 10 and 11 that lives in your system tray and displays real-time battery levels and audio telemetry for your connected Bluetooth devices.",
            ["About_Features"] = "Key Highlights",
            ["About_Feature1"] = "• 36 Hardware Protocols: AirPods, Galaxy Buds, Sony MDR, Logitech HID++/LIGHTSPEED, DualSense, Joy-Con, etc.",
            ["About_Feature2"] = "• Dynamic System Tray Icon (GDI+ battery gauge rendering)",
            ["About_Feature3"] = "• Native Windows Toast Notifications (Low & Critical battery alerts)",
            ["About_Feature4"] = "• Smart Audio Routing & Codec Inspector (LDAC, aptX HD, AAC, SBC)",
            ["About_Feature5"] = "• Embedded Local REST / JSON API (Rainmeter, Stream Deck, Home Assistant)",
            ["About_Close"] = "OK",

            // Toast Notifications
            ["Toast_CriticalTitle"] = "⚠️ Critical Battery: {0}",
            ["Toast_CriticalBody"] = "{0} battery is critically low ({1}%). Please recharge immediately!",
            ["Toast_LowTitle"] = "🔋 Low Battery: {0}",
            ["Toast_LowBody"] = "{0} battery is low ({1}%). You may need to recharge soon.",

            // Software Updates
            ["Update_Title"] = "Software Updates",
            ["Update_Desc"] = "Check GitHub for new features, bug fixes, and hardware protocols",
            ["Update_CheckNow"] = "Check for Updates",
            ["Update_Checking"] = "Checking GitHub for updates...",
            ["Update_UpToDate"] = "You are using the latest version (v{0}).",
            ["Update_Available"] = "New version available: v{0}!",
            ["Update_Download"] = "Download & Install Update",
            ["Update_Error"] = "Could not check for updates. Please try again later."
        },

        ["tr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Flyout
            ["App_Title"] = "Bluetooth Pil Monitörü",
            ["Flyout_Header"] = "Bluetooth Cihazları",
            ["Flyout_RefreshTooltip"] = "Cihazları Yenile",
            ["Flyout_BluetoothSettings"] = "Bluetooth Ayarları",
            ["Flyout_AppSettingsTooltip"] = "Uygulama Ayarları",
            ["Flyout_HideTooltip"] = "Gizle",
            ["Flyout_NoDevicesTitle"] = "Bağlı Bluetooth Cihazı Yok",
            ["Flyout_NoDevicesDesc"] = "Bluetooth cihazınızı açın ve bilgisayarınıza bağlayın.",
            ["Flyout_Status_Connecting"] = "Bağlanıyor...",
            ["Flyout_Status_Scanning"] = "Bluetooth cihazları taranıyor...",
            ["Flyout_Status_UpToDate"] = "Tüm aygıtlar güncel",
            ["Flyout_Status_DevicesFound"] = "{0} cihaz bağlı",

            // Device Card
            ["Device_Connected"] = "Bağlı",
            ["Device_Disconnected"] = "Bağlı Değil",
            ["Device_ChargingInCase"] = "Kutuda / Şarj Oluyor",
            ["Device_UnknownBattery"] = "Bilinmiyor",
            ["Device_Case"] = "Kutu: ",
            ["Device_CustomThresholdBadge"] = " • Eşik: %{0}",
            ["Device_ThresholdDefault"] = "Varsayılan (%{0})",
            ["Device_ThresholdCustom"] = "Özel Uyarı: %{0}",
            ["Device_MuteTooltip"] = "Sesi Kapat / Aç",
            ["Codec_LDAC"] = "Sony LDAC (Yüksek Çözünürlüklü Ses • 990 kbps / 96 kHz)",
            ["Codec_AptXHD"] = "Qualcomm aptX HD (Yüksek Çözünürlüklü Ses • 576 kbps / 48 kHz)",
            ["Codec_AptX"] = "Qualcomm aptX (Düşük Gecikmeli Yüksek Kalite • 352 kbps)",
            ["Codec_AAC"] = "Advanced Audio Coding (Windows 11 / AAC HD Ses • 256 kbps)",
            ["Codec_SBC"] = "Subband Codec (Standart Bluetooth Ses)",
            ["Codec_Active"] = "Aktif Ses Kodeki: {0}",

            // Device Context Menu
            ["Menu_CustomThresholdHeader"] = "Özel Pil Eşiği Belirle",
            ["Menu_DefaultThreshold"] = "Varsayılan Eşik (Global)",
            ["Menu_Threshold10"] = "%10 (Fare / Düşük Tüketim)",
            ["Menu_Threshold15"] = "%15",
            ["Menu_Threshold20"] = "%20 (Standart Kulaklık)",
            ["Menu_Threshold25"] = "%25",
            ["Menu_Threshold30"] = "%30",
            ["Menu_Threshold40"] = "%40 (Erken Uyarı)",

            // Tray Menu
            ["Tray_OpenHide"] = "Aç / Gizle",
            ["Tray_Refresh"] = "Aygıtları Yenile",
            ["Tray_LaunchAtStartup"] = "Windows ile Başlat",
            ["Tray_Settings"] = "Ayarlar...",
            ["Tray_About"] = "Hakkında",
            ["Tray_Exit"] = "Çıkış",
            ["Tray_TooltipNoDevices"] = "Bluetooth Pil Monitörü (Bağlı cihaz yok)",

            // Settings Window
            ["Settings_Title"] = "Ayarlar - Bluetooth Pil Monitörü",
            ["Settings_Header"] = "Uygulama Ayarları",
            ["Settings_SubHeader"] = "Pil takibi ve bildirim tercihlerinizi özelleştirin.",
            ["Settings_LaunchAtStartup"] = "Windows ile Başlat",
            ["Settings_LaunchAtStartupDesc"] = "Bilgisayar açıldığında arka planda başlasın",
            ["Settings_Notifications"] = "Düşük Pil Bildirimleri",
            ["Settings_NotificationsDesc"] = "Pil azaldığında Windows Toast uyarısı gönder",
            ["Settings_AudioRouter"] = "Akıllı Ses Yönlendirici",
            ["Settings_AudioRouterDesc"] = "Kulaklık bağlandığında varsayılan ses çıkışını otomatik değiştir",
            ["Settings_RestApi"] = "Yerel REST API Sunucusu",
            ["Settings_RestApiDesc"] = "Pil verilerini üçüncü parti araçlar için yerel HTTP portundan sun",
            ["Settings_RestApiPort"] = "Dinleme Portu (1024 - 65535)",
            ["Settings_LowThreshold"] = "Düşük Pil Uyarı Eşiği",
            ["Settings_DeviceThresholds"] = "Cihaz Bazlı Özel Eşikler",
            ["Settings_DeviceThresholdsDesc"] = "Her cihaz için farklı pil seviyesinde uyarı (Örn: Fare için %10, Kulaklık için %25)",
            ["Settings_DeviceThresholdsTip"] = "💡 İpucu: Flyout penceresindeki cihaz kartına sağ tıklayarak da o cihaza özel eşik belirleyebilirsiniz.",
            ["Settings_CriticalThreshold"] = "Kritik Pil Uyarı Eşiği",
            ["Settings_PollingFrequency"] = "Yoklama Sıklığı",
            ["Settings_PollingFrequencyDesc"] = "Pil kontrolleri arasındaki süre",
            ["Settings_Theme"] = "Tema Seçimi",
            ["Settings_ThemeDesc"] = "Arayüz görünümünü belirleyin",
            ["Settings_Language"] = "Dil Seçimi",
            ["Settings_LanguageDesc"] = "Uygulama dilini belirleyin (English / Türkçe)",
            ["Settings_SaveAndClose"] = "Kaydet ve Kapat",

            // About Window
            ["About_Title"] = "Hakkında - Bluetooth Pil Monitörü",
            ["About_AppName"] = "Bluetooth Pil Monitörü",
            ["About_Version"] = "Sürüm 1.0.0 (x64)",
            ["About_Overview"] = "Genel Bakış",
            ["About_OverviewText"] = "Windows 10 ve Windows 11 için modern Fluent Design arayüzüne sahip, bağlı Bluetooth cihazlarınızın pil seviyelerini ve ses durumlarını gerçek zamanlı gösteren masaüstü aracı.",
            ["About_Features"] = "Öne Çıkan Özellikler",
            ["About_Feature1"] = "• 36 Donanım Protokolü: AirPods, Galaxy Buds, Sony MDR, Logitech HID++/LIGHTSPEED, DualSense, Joy-Con vb.",
            ["About_Feature2"] = "• Dinamik Sistem Tepsisi Simgesi (GDI+ pil seviyesi çizimi)",
            ["About_Feature3"] = "• Windows Toast Bildirimleri (Kritik ve Düşük pil alarmları)",
            ["About_Feature4"] = "• Akıllı Ses Yönlendirici & Kodek Tespiti (LDAC, aptX HD, AAC, SBC)",
            ["About_Feature5"] = "• Yerel REST / JSON API (Rainmeter, Stream Deck, Home Assistant)",
            ["About_Close"] = "Tamam",

            // Toast Notifications
            ["Toast_CriticalTitle"] = "⚠️ Kritik Pil: {0}",
            ["Toast_CriticalBody"] = "{0} pili kritik seviyede (%{1}). Lütfen hemen şarj edin!",
            ["Toast_LowTitle"] = "🔋 Düşük Pil: {0}",
            ["Toast_LowBody"] = "{0} pili azaldı (%{1}). Yakında şarja takmanız gerekebilir.",

            // Software Updates
            ["Update_Title"] = "Yazılım Güncellemeleri",
            ["Update_Desc"] = "Yeni özellikler, hata düzeltmeleri ve donanım protokolleri için GitHub'ı kontrol edin",
            ["Update_CheckNow"] = "Güncellemeleri Denetle",
            ["Update_Checking"] = "GitHub güncellemeleri denetleniyor...",
            ["Update_UpToDate"] = "Uygulamanız en güncel sürümde! (v{0})",
            ["Update_Available"] = "Yeni bir sürüm mevcut: v{0}!",
            ["Update_Download"] = "Güncellemeyi İndir ve Kur",
            ["Update_Error"] = "Güncellemeler denetlenemedi. Lütfen daha sonra tekrar deneyin."
        }
    };

    public static string GetString(string key, params object[] args)
    {
        string lang = CurrentLanguage;
        if (!Translations.TryGetValue(lang, out var dict) || !dict.TryGetValue(key, out var val))
        {
            if (!Translations["en"].TryGetValue(key, out val))
            {
                val = key;
            }
        }

        return args != null && args.Length > 0 ? string.Format(val, args) : val;
    }

    public static void Initialize(string? initialLanguage = null)
    {
        string lang = initialLanguage ?? "en";
        if (string.IsNullOrWhiteSpace(lang) || !Translations.ContainsKey(lang))
        {
            lang = "en";
        }
        ApplyLanguage(lang);
    }

    public static void ApplyLanguage(string languageCode)
    {
        string code = (languageCode?.ToLowerInvariant()) switch
        {
            "tr" or "tr-tr" => "tr",
            _ => "en"
        };

        CurrentLanguage = code;

        // Thread kültürlerini senkronize et
        try
        {
            var culture = new CultureInfo(code == "tr" ? "tr-TR" : "en-US");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch { }

        // WPF DynamicResource sözlüğünü güncelle
        var app = Application.Current;
        if (app != null)
        {
            var targetDict = Translations[code];
            foreach (var kvp in targetDict)
            {
                app.Resources[kvp.Key] = kvp.Value;
            }
        }

        LanguageChanged?.Invoke(null, code);
    }
}
