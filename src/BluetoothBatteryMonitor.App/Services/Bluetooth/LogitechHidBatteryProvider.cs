using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public record LogitechBatteryInfo(
    int Level,
    bool IsCharging,
    string StatusDescription);

public class LogitechHidBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Logitech HID++";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    // Logitech Vendor ID: 0x046D
    public const string LogitechVendorId = "VID_046D";
    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string HidInterfaceGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";

    public const ushort FeatureBatteryStatus = 0x1000;
    public const ushort FeatureUnifiedBattery = 0x1004;

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<BluetoothDeviceModel>();

        try
        {
            // 1. Scan standard PnP Logitech devices
            string aqs = $"(System.Devices.DeviceInstanceId:~~\"{LogitechVendorId}\")";
            var props = new[]
            {
                PnpBatteryKey,
                "System.ItemNameDisplay",
                "System.Devices.Connected"
            };

            var pnpDevices = await DeviceInformation.FindAllAsync(
                aqs,
                props,
                DeviceInformationKind.Device).AsTask(cancellationToken);

            foreach (var dev in pnpDevices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                int? battery = null;
                if (dev.Properties.TryGetValue(PnpBatteryKey, out var val) && val != null)
                {
                    if (int.TryParse(val.ToString(), out int b))
                        battery = b;
                }

                string name = !string.IsNullOrWhiteSpace(dev.Name) ? dev.Name : "Logitech Device";
                bool isConnected = false;
                if (dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var aepVal) && aepVal is bool ac)
                {
                    isConnected = ac;
                }


                if (battery.HasValue)
                {
                    var model = new BluetoothDeviceModel
                    {
                        Id = dev.Id,
                        Name = CleanLogitechName(name),
                        BatteryLevel = battery.Value,
                        IsConnected = isConnected,
                        DeviceType = DetectLogitechType(name),
                        BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(dev.Id),
                        ProviderSource = "Logitech HID++",
                        LastUpdated = DateTime.Now
                    };

                    list.Add(model);
                }
            }

            // 2. Query HID++ 2.0 via Win32 HID API (especially Bluetooth keyboards like K380)
            string hidSelector = $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceGuid}\"";
            var hidDevices = await DeviceInformation.FindAllAsync(hidSelector).AsTask(cancellationToken);

            var logitechHidList = hidDevices.Where(d =>
                d.Id.Contains(LogitechVendorId, StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains("K380", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Id.Contains("Col07", StringComparison.OrdinalIgnoreCase) ? 2 : (d.Id.Contains("Col0", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                .ToList();

            foreach (var dev in logitechHidList)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Check if already added
                if (list.Any(x => x.Id == dev.Id || (!string.IsNullOrEmpty(x.Name) && x.Name.Equals(CleanLogitechName(dev.Name), StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                var batInfo = await QueryLogitechHidBatteryAsync(dev.Id, cancellationToken);
                if (batInfo != null)
                {
                    string devName = !string.IsNullOrWhiteSpace(dev.Name) ? dev.Name : "Logitech Device";
                    var model = new BluetoothDeviceModel
                    {
                        Id = dev.Id,
                        Name = CleanLogitechName(devName),
                        BatteryLevel = batInfo.Level,
                        IsCharging = batInfo.IsCharging,
                        IsConnected = true,
                        DeviceType = DetectLogitechType(devName),
                        BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(dev.Id),
                        ProviderSource = "Logitech HID++",
                        LastUpdated = DateTime.Now
                    };

                    list.Add(model);
                }
            }
        }
        catch
        {
            // Scan error
        }

        return list;
    }

    /// <summary>
    /// Queries battery report from HID++ 2.0 device (K380 etc.) via Win32 HID API.
    /// </summary>
    private static async Task<LogitechBatteryInfo?> QueryLogitechHidBatteryAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            using var handle = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (handle.IsInvalid) return null;

            // 1. Verify collection capability via PreparsedData and Caps
            if (NativeMethods.HidD_GetPreparsedData(handle, out var pData))
            {
                bool isTargetCollection = false;
                int status = NativeMethods.HidP_GetCaps(pData, out var caps);
                if (status >= 0 || (uint)status == 0x00110000)
                {
                    // Logitech vendor channel (UsagePage 0xFF00, Usage 0x0002) or 20-byte Output Report
                    if ((caps.UsagePage == 0xFF00 && (caps.Usage == 0x0002 || caps.Usage == 0x0001)) ||
                        caps.OutputReportByteLength >= 20 || caps.InputReportByteLength >= 20)
                    {
                        isTargetCollection = true;
                    }
                }
                NativeMethods.HidD_FreePreparsedData(pData);

                if (!isTargetCollection && !devicePath.Contains("Col07", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            // Keep file stream open for session duration (prevent early handle disposal)
            using var fs = new FileStream(handle, FileAccess.ReadWrite, 20, isAsync: true);

            // 2. Discover Feature 0x1000 (Battery Level Status) or 0x1004 index via Root Feature (0x0000)
            byte[] rootReq = new byte[20];
            rootReq[0] = 0x11; // HID++ Long Report ID
            rootReq[1] = 0xFF; // Direct Bluetooth Device index
            rootReq[2] = 0x00; // IRoot Feature
            rootReq[3] = 0x0D; // getFeature function 0 | SwID
            rootReq[4] = 0x10; // Feature 0x1000 MSB
            rootReq[5] = 0x00; // Feature 0x1000 LSB

            byte featureIndex = 0;

            if (NativeMethods.HidD_SetOutputReport(handle, rootReq, (uint)rootReq.Length))
            {
                using var ctsRoot = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsRoot.CancelAfter(600);

                byte[] rootResp = new byte[20];
                int read = await fs.ReadAsync(rootResp, 0, 20, ctsRoot.Token);
                if (read >= 5 && rootResp[0] == 0x11 && rootResp[1] == 0xFF && rootResp[2] == 0x00)
                {
                    featureIndex = rootResp[4];
                }
            }

            // If 0x1000 is not found, try Feature 0x1004 (Unified Battery)
            if (featureIndex == 0)
            {
                rootReq[4] = 0x10;
                rootReq[5] = 0x04;
                if (NativeMethods.HidD_SetOutputReport(handle, rootReq, (uint)rootReq.Length))
                {
                    using var ctsUnified = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    ctsUnified.CancelAfter(600);

                    byte[] rootResp = new byte[20];
                    int read = await fs.ReadAsync(rootResp, 0, 20, ctsUnified.Token);
                    if (read >= 5 && rootResp[0] == 0x11 && rootResp[1] == 0xFF && rootResp[2] == 0x00)
                    {
                        featureIndex = rootResp[4];
                    }
                }
            }

            // If battery feature is not supported, device has no battery (e.g. wired mouse/keyboard)
            if (featureIndex == 0)
            {
                return null;
            }

            // 3. Query battery telemetry via discovered FeatureIndex
            byte[] batReq = new byte[20];
            batReq[0] = 0x11;
            batReq[1] = 0xFF;
            batReq[2] = featureIndex;
            batReq[3] = 0x0D; // getBatteryLevelStatus function 0 | SwID

            if (NativeMethods.HidD_SetOutputReport(handle, batReq, (uint)batReq.Length))
            {
                using var ctsBat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsBat.CancelAfter(600);

                byte[] batResp = new byte[20];
                int read = await fs.ReadAsync(batResp, 0, 20, ctsBat.Token);
                // batResp[2] must match featureIndex without error (0x8F)
                if (read >= 6 && batResp[2] == featureIndex && TryParseLogitechBatteryReport(batResp, out var info))
                {
                    return info;
                }
            }
        }
        catch
        {
            // Device access error
        }

        return null;
    }

    /// <summary>
    /// Decodes HID++ 1.0 / 2.0 response bytes.
    /// Feature 0x1000 (Battery Status) and 0x1004 (Unified Battery) are supported.
    /// </summary>
    public static bool TryParseLogitechBatteryReport(byte[] report, out LogitechBatteryInfo? info)
    {
        info = null;
        if (report == null || report.Length < 7)
        {
            return false;
        }

        byte reportId = report[0];

        // HID++ Long Report (0x11)
        if (reportId == 0x11 && report.Length >= 7)
        {
            // HID++ 2.0 Feature 0x1000 (Battery Status) format:
            // report[4]: Discharging / battery percentage (0-100)
            // report[5]: Next level threshold
            // report[6]: Charging status (0=Discharging, 1=Charging, 2=Full)
            byte percentage = report[4];
            if (percentage <= 100)
            {
                byte chargeStatus = report[6];
                bool isCharging = chargeStatus == 1 || chargeStatus == 2;
                string statusDesc = chargeStatus switch
                {
                    1 => "Charging",
                    2 => "Full",
                    _ => "Discharging"
                };

                info = new LogitechBatteryInfo(percentage, isCharging, statusDesc);
                return true;
            }

            // HID++ 2.0 Feature 0x1004 (Unified Battery) fallback:
            // report[4]: level (0-100), report[5]: status (0=Discharging, 1=Charging, 2=Full)
            byte uPercentage = report[4];
            byte uStatus = report[5];
            if (uPercentage <= 100)
            {
                bool isCharging = uStatus == 1;
                string statusDesc = uStatus switch
                {
                    1 => "Charging",
                    2 => "Full",
                    _ => "Discharging"
                };

                info = new LogitechBatteryInfo(uPercentage, isCharging, statusDesc);
                return true;
            }
        }

        return false;
    }

    public static string CleanLogitechName(string rawName)
    {
        string cleaned = rawName.Replace("(TM)", "").Replace("®", "").Trim();
        if (!cleaned.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) &&
            !cleaned.Contains("Logitech", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = $"Logitech {cleaned}";
        }
        return cleaned;
    }

    public static DeviceType DetectLogitechType(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("keyboard") || lower.Contains("klavye") || lower.Contains("k380") || lower.Contains("mx keys") || lower.Contains("craft"))
            return DeviceType.Keyboard;
        if (lower.Contains("mouse") || lower.Contains("fare") || lower.Contains("master") || lower.Contains("anywhere") || lower.Contains("lift") || lower.Contains("g pro") || lower.Contains("g502"))
            return DeviceType.Mouse;
        if (lower.Contains("headset") || lower.Contains("pro x") || lower.Contains("zone"))
            return DeviceType.Headphones;

        return DeviceType.Mouse;
    }

    private DeviceWatcher? _watcher;

    public void StartMonitoring()
    {
        try
        {
            string aqs = $"(System.Devices.DeviceInstanceId:~~\"{LogitechVendorId}\")";
            var props = new[]
            {
                PnpBatteryKey,
                "System.ItemNameDisplay",
                "System.Devices.Connected"
            };

            _watcher = DeviceInformation.CreateWatcher(aqs, props, DeviceInformationKind.Device);
            _watcher.Updated += (s, update) =>
            {
                if (update.Properties.TryGetValue(PnpBatteryKey, out var val) && val != null)
                {
                    if (int.TryParse(val.ToString(), out int b))
                    {
                        var model = new BluetoothDeviceModel
                        {
                            Id = update.Id,
                            BatteryLevel = b,
                            ProviderSource = "Logitech HID++ (Live)",
                            LastUpdated = DateTime.Now
                        };
                        DeviceUpdated?.Invoke(this, model);
                    }
                }
            };
            _watcher.Start();
        }
        catch
        {
            // Periodic polling continues if watcher cannot be started
        }
    }

    public void StopMonitoring()
    {
        try
        {
            _watcher?.Stop();
            _watcher = null;
        }
        catch { }
    }

    public void Dispose()
    {
        StopMonitoring();
    }

    private static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

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
        public static extern bool HidD_SetOutputReport(
            SafeFileHandle HidDeviceObject,
            [In] byte[] ReportBuffer,
            uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(
            SafeFileHandle HidDeviceObject,
            [Out] byte[] ReportBuffer,
            uint ReportBufferLength);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(
            SafeFileHandle HidDeviceObject,
            out IntPtr PreparsedData);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(
            IntPtr PreparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(
            IntPtr PreparsedData,
            out HIDP_CAPS Capabilities);

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }
    }
}
