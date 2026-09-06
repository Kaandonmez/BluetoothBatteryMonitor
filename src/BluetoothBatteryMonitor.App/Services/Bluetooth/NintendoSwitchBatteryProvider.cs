using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public record NintendoBatteryInfo(
    int BatteryLevel,
    bool IsCharging,
    string ModelName);

/// <summary>
/// Provider that parses Bluetooth HID telemetry reports (Report 0x21, 0x30, 0x31, 0x3F)
/// for Nintendo Switch Joy-Con (L), Joy-Con (R), and Pro Controller wireless gamepads.
/// </summary>
public class NintendoSwitchBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Nintendo Switch Controller (HID Telemetry)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    public const string NintendoVendorId = "VID_057E";
    private const string HidInterfaceGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public const ushort PidJoyConL = 0x2006;
    public const ushort PidJoyConR = 0x2007;
    public const ushort PidProController = 0x2009;
    public const ushort PidJoyConChargingGrip = 0x200E;

    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private bool _isMonitoring;
    private bool _isDisposed;

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var resultList = new List<BluetoothDeviceModel>();

        try
        {
            // 1. Scan PnP devices
            string aqs = $"(System.Devices.DeviceInstanceId:~~\"{NintendoVendorId}\")";
            var props = new[]
            {
                PnpBatteryKey,
                "System.ItemNameDisplay",
                "System.Devices.Connected"
            };

            var pnpDevices = await DeviceInformation.FindAllAsync(aqs, props, DeviceInformationKind.Device).AsTask(cancellationToken);
            foreach (var dev in pnpDevices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ushort pid = ExtractPid(dev.Id);
                if (!IsNintendoControllerPid(pid)) continue;

                string modelName = GetNintendoModelName(pid);
                int? pnpBattery = null;
                if (dev.Properties.TryGetValue(PnpBatteryKey, out var val) && val != null && int.TryParse(val.ToString(), out int b))
                {
                    pnpBattery = b;
                }

                bool isConnected = false;
                if (dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var aepVal) && aepVal is bool ac)
                {
                    isConnected = ac;
                }


                var model = new BluetoothDeviceModel
                {
                    Id = dev.Id,
                    Name = modelName,
                    ModelName = modelName,
                    DeviceType = DeviceType.Gamepad,
                    BatteryLevel = pnpBattery,
                    IsConnected = isConnected,
                    BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(dev.Id),
                    ProviderSource = "Nintendo Switch HID (PnP)",
                    LastUpdated = DateTime.Now
                };

                resultList.Add(model);
            }

            // 2. Win32 HID Interface Telemetry Reports
            string hidSelector = $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceGuid}\"";
            var hidDevices = await DeviceInformation.FindAllAsync(hidSelector).AsTask(cancellationToken);

            var nintendoHid = hidDevices
                .Where(d => d.Id.Contains(NintendoVendorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var hidDev in nintendoHid)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ushort pid = ExtractPid(hidDev.Id);
                if (!IsNintendoControllerPid(pid)) continue;

                var telemetry = await QueryHidTelemetryAsync(hidDev.Id, pid, cancellationToken);
                if (telemetry != null)
                {
                    string modelName = telemetry.ModelName;
                    var model = new BluetoothDeviceModel
                    {
                        Id = hidDev.Id,
                        Name = modelName,
                        ModelName = modelName,
                        DeviceType = DeviceType.Gamepad,
                        BatteryLevel = telemetry.BatteryLevel,
                        IsCharging = telemetry.IsCharging,
                        IsConnected = true,
                        BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(hidDev.Id),
                        ProviderSource = "Nintendo Switch HID (Telemetry)",
                        LastUpdated = DateTime.Now
                    };

                    var existingIdx = resultList.FindIndex(d => d.Matches(model));
                    if (existingIdx >= 0)
                    {
                        resultList[existingIdx] = model;
                    }
                    else
                    {
                        resultList.Add(model);
                    }

                    _devices[model.Id] = model;
                }
            }
        }
        catch
        {
            // Suppress scan error
        }

        return resultList;
    }

    private static Task<NintendoBatteryInfo?> QueryHidTelemetryAsync(string devicePath, ushort pid, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GENERIC_READ,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid) return null;

                byte[] buffer = new byte[64];
                bool success = NativeMethods.ReadFile(handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero);
                if (!success || read <= 0) return null;

                if (TryParseNintendoReport(buffer, out var level, out var charging) && level.HasValue)
                {
                    return new NintendoBatteryInfo(level.Value, charging, GetNintendoModelName(pid));
                }
            }
            catch
            {
                // Read error
            }

            return null;
        }, cancellationToken);
    }

    public void StartMonitoring()
    {
        if (_isMonitoring || _isDisposed) return;
        _isMonitoring = true;

        _monitoringCts = new CancellationTokenSource();
        _monitoringTask = Task.Run(() => RunMonitoringLoopAsync(_monitoringCts.Token));
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        try
        {
            _monitoringCts?.Cancel();
        }
        catch { }
    }

    private async Task RunMonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_isDisposed)
        {
            try
            {
                var devices = await GetDevicesAsync(token);
                foreach (var dev in devices)
                {
                    DeviceUpdated?.Invoke(this, dev);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Silently ignore transient errors
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Parses Nintendo Switch HID Input Report 0x21, 0x30, 0x31 or 0x3F bytes.
    /// Byte 2 (or Simple Report Byte 2) high nibble contains battery level step and charging status.
    /// </summary>
    public static bool TryParseNintendoReport(byte[] report, out int? batteryLevel, out bool isCharging)
    {
        batteryLevel = null;
        isCharging = false;

        if (report == null || report.Length < 3)
        {
            return false;
        }

        byte reportId = report[0];

        // Report 0x21, 0x30, 0x31: Battery info in Byte 2
        // Report 0x3F: Battery info in Byte 2
        if (reportId is 0x21 or 0x30 or 0x31 or 0x3F)
        {
            byte batByte = report[2];
            int nibble = (batByte >> 4) & 0x0F;

            isCharging = (nibble & 0x01) != 0;
            int levelStep = (nibble >> 1) & 0x07;

            batteryLevel = levelStep switch
            {
                >= 4 => 100,
                3 => 70,
                2 => 30,
                1 => 10,
                _ => 0
            };

            return true;
        }

        return false;
    }

    public static ushort ExtractPid(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return 0;

        var match = global::System.Text.RegularExpressions.Regex.Match(deviceId, @"PID[_\&]([0-9A-Fa-f]{4})", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && ushort.TryParse(match.Groups[1].Value, global::System.Globalization.NumberStyles.HexNumber, null, out ushort pid))
        {
            return pid;
        }

        return 0;
    }

    public static bool IsNintendoControllerPid(ushort pid)
    {
        return pid is PidJoyConL or PidJoyConR or PidProController or PidJoyConChargingGrip;
    }

    public static string GetNintendoModelName(ushort pid)
    {
        return pid switch
        {
            PidJoyConL => "Nintendo Joy-Con (L)",
            PidJoyConR => "Nintendo Joy-Con (R)",
            PidProController => "Nintendo Switch Pro Controller",
            PidJoyConChargingGrip => "Nintendo Joy-Con Charging Grip",
            _ => "Nintendo Switch Controller"
        };
    }

    private static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopMonitoring();
        _monitoringCts?.Dispose();
        _devices.Clear();
    }
}
