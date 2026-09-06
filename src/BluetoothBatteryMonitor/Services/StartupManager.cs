using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Uygulamanın Windows açılışında otomatik başlatılmasını yönetir (HKCU\...\Run).
/// Yönetici yetkisi gerektirmeden geçerli kullanıcı profiline güvenle yazar.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "BluetoothBatteryMonitor";

    /// <summary>
    /// Uygulamanın çalıştırılabilir tam dosya yolunu alır.
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
    /// Uygulamanın başlangıçta çalışacak şekilde kayıtlı olup olmadığını denetler.
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
    /// Başlangıçta çalıştırma durumunu açar veya kapatır.
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
