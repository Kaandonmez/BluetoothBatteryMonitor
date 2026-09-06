using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Manages automatic launching of the application at Windows startup (HKCU\...\Run).
/// Safely writes to the current user profile without requiring administrator privileges.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "BluetoothBatteryMonitor";

    /// <summary>
    /// Gets the full executable file path of the application.
    /// </summary>
    public static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
        {
            return $"\"{processPath}\"";
        }

        var entryLocation = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(entryLocation) && File.Exists(entryLocation))
        {
            return $"\"{entryLocation}\"";
        }

        return $"\"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BluetoothBatteryMonitor.exe")}\"";
    }

    /// <summary>
    /// Checks whether the application is registered to run at startup.
    /// </summary>
    public static bool IsRunAtStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key == null) return false;

            var value = key.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupManager] IsRunAtStartup kontrol hatası: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables running at startup.
    /// </summary>
    public static bool SetRunAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            if (enable)
            {
                var execPath = GetExecutablePath();
                key.SetValue(AppName, execPath, RegistryValueKind.String);
                Debug.WriteLine($"[StartupManager] Windows ile başlatma aktif edildi: {execPath}");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                    Debug.WriteLine("[StartupManager] Windows ile başlatma devreden çıkarıldı.");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupManager] SetRunAtStartup ({enable}) hatası: {ex.Message}");
            return false;
        }
    }
}
