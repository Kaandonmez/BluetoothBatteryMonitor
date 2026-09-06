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

public record SteelSeriesBatteryInfo(
    int BatteryLevel,
    bool IsCharging,
    string ModelName);

/// <summary>
/// Battery provider for SteelSeries Arctis 7, 9, Pro Wireless, and Arctis Nova wireless gaming headsets.
/// Parses HID telemetry and status reports to extract battery levels and charging state.
/// </summary>
public class SteelSeriesBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "SteelSeries Headset (HID Telemetry)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    public const string SteelSeriesVendorId = "VID_1038";
    private const string HidInterfaceGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public const ushort PidArctis7 = 0x12AD;
    public const ushort PidArctis7Plus = 0x12C2;
    public const ushort PidArctis9 = 0x12B3;
    public const ushort PidArctisProWireless = 0x1252;
    public const ushort PidArctisNova7 = 0x12E0;
    public const ushort PidArctisNovaPro = 0x2202;
    public const ushort PidArctis1Wireless = 0x1280;

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
            // 1. Discover PnP Devices
            string aqs = $"(System.Devices.DeviceInstanceId:~~\"{SteelSeriesVendorId}\")";
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
                if (!IsSteelSeriesHeadsetPid(pid)) continue;

                string modelName = GetSteelSeriesModelName(pid);
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
                    DeviceType = DeviceType.Headphones,
                    BatteryLevel = pnpBattery,
                    IsConnected = isConnected,
                    BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(dev.Id),
                    ProviderSource = "SteelSeries HID (PnP)",
                    LastUpdated = DateTime.Now
                };

                resultList.Add(model);
            }

            // 2. Read direct telemetry via Win32 HID Interface
            string hidSelector = $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceGuid}\"";
            var hidDevices = await DeviceInformation.FindAllAsync(hidSelector).AsTask(cancellationToken);

            var ssHid = hidDevices
                .Where(d => d.Id.Contains(SteelSeriesVendorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var hidDev in ssHid)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ushort pid = ExtractPid(hidDev.Id);
                if (!IsSteelSeriesHeadsetPid(pid)) continue;

                var telemetry = await QueryHidTelemetryAsync(hidDev.Id, pid, cancellationToken);
                if (telemetry != null)
                {
                    string modelName = telemetry.ModelName;
                    var model = new BluetoothDeviceModel
                    {
                        Id = hidDev.Id,
                        Name = modelName,
                        ModelName = modelName,
                        DeviceType = DeviceType.Headphones,
                        BatteryLevel = telemetry.BatteryLevel,
                        IsCharging = telemetry.IsCharging,
                        IsConnected = true,
                        BluetoothAddress = BluetoothDeviceModel.ExtractMacAddress(hidDev.Id),
                        ProviderSource = "SteelSeries HID (Telemetry)",
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
            // Silently catch scan exceptions
        }

        return resultList;
    }

    private static Task<SteelSeriesBatteryInfo?> QueryHidTelemetryAsync(string devicePath, ushort pid, CancellationToken cancellationToken)
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

                if (TryParseSteelSeriesReport(buffer, out var level, out var charging) && level.HasValue)
                {
                    return new SteelSeriesBatteryInfo(level.Value, charging, GetSteelSeriesModelName(pid));
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
    /// Parses SteelSeries HID telemetry report (Report 0xB0 or 0x00).
    /// </summary>
    public static bool TryParseSteelSeriesReport(byte[] report, out int? batteryLevel, out bool isCharging)
    {
        batteryLevel = null;
        isCharging = false;

        if (report == null || report.Length < 4)
        {
            return false;
        }

        byte reportId = report[0];

        // Report 0xB0 (Arctis series battery and charging telemetry)
        if (reportId == 0xB0)
        {
            byte rawLevel = report[2];
            byte chargeByte = report[3];

            isCharging = chargeByte == 0x01 || (chargeByte & 0x01) != 0;

            if (rawLevel <= 4)
            {
                // 4-step level indicator (0-4)
                batteryLevel = rawLevel switch
                {
                    4 => 100,
                    3 => 75,
                    2 => 50,
                    1 => 25,
                    _ => 5
                };
            }
            else if (rawLevel <= 100)
            {
                batteryLevel = rawLevel;
            }

            return batteryLevel.HasValue;
        }

        // Report 0x00 (Arctis Nova series telemetry)
        if (reportId == 0x00 && report.Length >= 4)
        {
            byte rawLevel = report[2];
            byte chargeByte = report[3];

            if (rawLevel <= 100 && rawLevel > 0)
            {
                batteryLevel = rawLevel;
                isCharging = chargeByte == 0x01;
                return true;
            }
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

    public static bool IsSteelSeriesHeadsetPid(ushort pid)
    {
        return pid is PidArctis7 or PidArctis7Plus or PidArctis9 or PidArctisProWireless or PidArctisNova7 or PidArctisNovaPro or PidArctis1Wireless;
    }

    public static string GetSteelSeriesModelName(ushort pid)
    {
        return pid switch
        {
            PidArctis7 => "SteelSeries Arctis 7 Wireless Headset",
            PidArctis7Plus => "SteelSeries Arctis 7+ Wireless Headset",
            PidArctis9 => "SteelSeries Arctis 9 Wireless Headset",
            PidArctisProWireless => "SteelSeries Arctis Pro Wireless Headset",
            PidArctisNova7 => "SteelSeries Arctis Nova 7 Wireless Headset",
            PidArctisNovaPro => "SteelSeries Arctis Nova Pro Wireless Headset",
            PidArctis1Wireless => "SteelSeries Arctis 1 Wireless Headset",
            _ => "SteelSeries Wireless Headset"
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
