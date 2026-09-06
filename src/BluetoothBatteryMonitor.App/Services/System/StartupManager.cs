using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class StartupManager
{
    private const string AppName = "BluetoothBatteryMonitor";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                    return true;
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
                return true;
            }
        }
        catch
        {
            // İzin hatası vb.
        }

        return false;
    }
}
