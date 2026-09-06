using System;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class ThemeManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static event EventHandler<bool>? ThemeChanged;

    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                ApplyCurrentTheme();
            }
        };

        ApplyCurrentTheme();
    }

    public static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(RegistryValueName);
            if (value is int lightThemeEnabled)
            {
                return lightThemeEnabled == 0;
            }
        }
        catch
        {
            // Fallback
        }

        return true; // Varsayılan Koyu Tema
    }

    public static void ApplyTheme(string themePreference)
    {
        bool isDark = themePreference switch
        {
            "Light" => false,
            "Dark" => true,
            _ => IsWindowsDarkTheme()
        };

        try
        {
            ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch
        {
            // UI thread dışındaysa veya henüz pencere yoksa yutulabilir
        }

        ThemeChanged?.Invoke(null, isDark);
    }

    public static void ApplyCurrentTheme()
    {
        var settings = Models.AppSettings.Load();
        ApplyTheme(settings.Theme);
    }
}
