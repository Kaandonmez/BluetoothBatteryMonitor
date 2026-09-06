using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.Models;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace BluetoothBatteryMonitor.Services;

public record LogitechBatteryInfo(
    int Level,
    bool IsCharging,
    string StatusDescription);

/// <summary>
/// Provider querying HID++ 1.0 and HID++ 2.0 protocols (Feature 0x1000: Battery Status
/// and Feature 0x1004: Unified Battery) for Logitech Bluetooth and wireless devices (Mice, Keyboards, etc.).
/// </summary>
public class LogitechHidBatteryProvider
{
    public const ushort LogitechVendorId = 0x046D;
    public const ushort FeatureBatteryStatus = 0x1000;
    public const ushort FeatureUnifiedBattery = 0x1004;

    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;
    public event EventHandler<string>? DeviceRemoved;

    public IReadOnlyCollection<BluetoothDeviceModel> Devices => _devices.Values.ToList();

    public async Task StartAsync()
    {
        await RefreshAsync();
    }

    public Task StopAsync()
    {
        _devices.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Scans Logitech HID devices on the system and queries battery reports.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            // Windows HID device interface class GUID: {4D1E55B2-F16F-11CF-88CB-001111000030}
            string selector = "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"";
            var hidDevices = await DeviceInformation.FindAllAsync(selector);

            foreach (var dev in hidDevices)
            {
                // VID_046D check
                if (dev.Id.Contains("VID_046D", StringComparison.OrdinalIgnoreCase) ||
                    dev.Name.Contains("Logitech", StringComparison.OrdinalIgnoreCase))
                {
                    QueryLogitechDevice(dev);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogitechHidBatteryProvider] RefreshAsync hatası: {ex.Message}");
        }
    }

    private void QueryLogitechDevice(DeviceInformation dev)
    {
        try
        {
            // Query HID Feature / Report by opening device file path
            // Uses SafeFileHandle and HidD_GetFeature for HID++ queries
            using var handle = NativeMethods.CreateFile(
                dev.Id,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                // 1. HID++ 2.0 Feature 0x1004 (Unified Battery) query
                // Long report (20 bytes): [0x11, DevIndex, FeatureIndex, Func/SwID, 0, ...]
                byte[] request = new byte[20];
                request[0] = 0x11; // HID++ Long Report ID
                request[1] = 0xFF; // Device index (receiver or direct device)

                byte[] response = new byte[20];
                if (NativeMethods.HidD_GetFeature(handle, response, (uint)response.Length))
                {
                    if (TryParseLogitechBatteryReport(response, out var batteryInfo) && batteryInfo != null)
                    {
                        UpdateModel(dev.Id, dev.Name, batteryInfo);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogitechHidBatteryProvider] Cihaz okuma istisnası ({dev.Name}): {ex.Message}");
        }

        // If HID handle could not be opened directly (e.g. Windows protection), PnP fallback check
        if (dev.Properties.TryGetValue(WindowsPnpBatteryProvider.PnpBatteryLevelKey, out var pnpLvl) && pnpLvl != null)
        {
            try
            {
                int level = Convert.ToInt32(pnpLvl);
                if (level >= 0 && level <= 100)
                {
                    var info = new LogitechBatteryInfo(level, false, "Normal");
                    UpdateModel(dev.Id, dev.Name, info);
                }
            }
            catch { }
        }
    }

    private void UpdateModel(string deviceId, string deviceName, LogitechBatteryInfo info)
    {
        var model = _devices.GetOrAdd(deviceId, id => new BluetoothDeviceModel
        {
            Id = id,
            Name = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : "Logitech Cihazı",
            Type = WindowsPnpBatteryProvider.DetectDeviceType(deviceName),
            ProviderSource = "Logitech HID++"
        });

        model.IsConnected = true;
        model.LastSeen = DateTime.Now;
        model.Battery.Level = info.Level;
        model.Battery.IsCharging = info.IsCharging;
        model.Battery.LastUpdated = DateTime.Now;

        DeviceUpdated?.Invoke(this, model);
    }

    /// <summary>
    /// Decodes HID++ 1.0 / 2.0 response bytes.
    /// Supports Feature 0x1000 (Battery Status) and 0x1004 (Unified Battery).
    /// </summary>
    public static bool TryParseLogitechBatteryReport(byte[] report, out LogitechBatteryInfo? info)
    {
        info = null;
        if (report == null || report.Length < 7)
        {
            return false;
        }

        byte reportId = report[0];

        // HID++ Long Report (0x11) or Short Report (0x10)
        if (reportId == 0x11 && report.Length >= 7)
        {
            // HID++ 2.0 Unified Battery (0x1004) format:
            // report[4]: Battery percentage (0-100)
            // report[5]: Charging status (0=Discharging, 1=Charging, 2=Full)
            byte percentage = report[4];
            byte status = report[5];

            if (percentage <= 100)
            {
                bool isCharging = status == 1;
                string statusDesc = status switch
                {
                    1 => "Şarj Ediliyor",
                    2 => "Tam Dolu",
                    _ => "Pilde"
                };

                info = new LogitechBatteryInfo(percentage, isCharging, statusDesc);
                return true;
            }

            // Alternative: Feature 0x1000 (Battery Status) format:
            // report[4]: level (0-100), report[6]: charging status
            byte altPercent = report[4];
            if (altPercent <= 100)
            {
                bool isCharging = report.Length > 6 && (report[6] == 1 || report[6] == 2);
                info = new LogitechBatteryInfo(altPercent, isCharging, isCharging ? "Şarj Ediliyor" : "Pilde");
                return true;
            }
        }

        return false;
    }

    private static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(
            SafeFileHandle HidDeviceObject,
            [Out] byte[] ReportBuffer,
            uint ReportBufferLength);
    }
}
