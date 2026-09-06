using System;
using System.Collections.Concurrent;
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

public record PlayStationBatteryInfo(
    int BatteryLevel,
    bool IsCharging,
    string ModelName);

/// <summary>
/// Sony DualShock 4 ve DualSense (PS5) kablosuz oyun kollarını Bluetooth HID üzerinden
/// telemetri moduna geçirerek (DS4: Report 0x11, DualSense: Report 0x31) pil ve şarj durumunu okuyan sağlayıcı.
/// </summary>
public class PlayStationControllerBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Sony PlayStation Controller (HID Telemetry)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    public const string SonyVendorId = "VID_054C";
    private const string HidInterfaceGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public const ushort PidDualShock4V1 = 0x05C4;
    public const ushort PidDualShock4V2 = 0x09CC;
    public const ushort PidDualShock4Dongle = 0x0BA0;
    public const ushort PidDualSense = 0x0CE6;
    public const ushort PidDualSenseEdge = 0x0DF2;

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
            // 1. PnP Cihazları ve Standart Batarya Özelliklerini Tara
            string aqs = $"(System.Devices.DeviceInstanceId:~~\"{SonyVendorId}\")";
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
                if (!IsPlayStationControllerPid(pid)) continue;

                string modelName = GetPlayStationModelName(pid);
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
                    ProviderSource = "PlayStation HID (PnP)",
                    LastUpdated = DateTime.Now
                };

                resultList.Add(model);
            }

            // 2. Win32 HID Interface üzerinden Doğrudan Telemetri Raporlarını Oku
            string hidSelector = $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceGuid}\"";
            var hidDevices = await DeviceInformation.FindAllAsync(hidSelector).AsTask(cancellationToken);

            var psHidDevices = hidDevices
                .Where(d => d.Id.Contains(SonyVendorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var hidDev in psHidDevices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ushort pid = ExtractPid(hidDev.Id);
                if (!IsPlayStationControllerPid(pid)) continue;

                var telemetry = await QueryHidTelemetryBatteryAsync(hidDev.Id, pid, cancellationToken);
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
                        ProviderSource = "PlayStation HID (Telemetry)",
                        LastUpdated = DateTime.Now
                    };

                    // PnP kaydı varsa onun yerine daha hassas telemetriyi koy
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
            // Tarama istisnasını yakala
        }

        return resultList;
    }

    /// <summary>
    /// DualShock 4 kolunu Bluetooth üzerinden genişletilmiş telemetri moduna (Report 0x11) geçirecek çıkış raporunu üretir.
    /// </summary>
    public static byte[] CreateDualShock4ModeSwitchReport()
    {
        byte[] report = new byte[78];
        report[0] = 0x11; // Report ID 0x11
        report[1] = 0x80; // Telemetri veri akış bayrağı
        report[3] = 0x04; // Motor/LED yapılandırma bayrağı
        return report;
    }

    /// <summary>
    /// DualSense (PS5) kolunu Bluetooth üzerinden genişletilmiş telemetri moduna (Report 0x31) geçirecek çıkış raporunu üretir.
    /// </summary>
    public static byte[] CreateDualSenseModeSwitchReport()
    {
        byte[] report = new byte[78];
        report[0] = 0x31; // Report ID 0x31
        report[1] = 0x02; // Tag / Sequence baytı
        report[2] = 0x03; // Telemetri ve özellik bayrakları
        return report;
    }

    /// <summary>
    /// Sony PlayStation kolunu Bluetooth HID telemetri moduna geçirmek için özel çıkış raporunu gönderir.
    /// </summary>
    public static bool SwitchControllerToTelemetryMode(SafeFileHandle handle, ushort pid)
    {
        if (handle == null || handle.IsInvalid) return false;

        try
        {
            bool isDualSense = pid is PidDualSense or PidDualSenseEdge;
            byte[] switchReport = isDualSense
                ? CreateDualSenseModeSwitchReport()
                : CreateDualShock4ModeSwitchReport();

            // 1. Önce HidD_SetOutputReport ile dene
            if (NativeMethods.HidD_SetOutputReport(handle, switchReport, switchReport.Length))
            {
                return true;
            }

            // 2. WriteFile ile dene
            if (NativeMethods.WriteFile(handle, switchReport, (uint)switchReport.Length, out _, IntPtr.Zero))
            {
                return true;
            }
        }
        catch
        {
            // Mod değiştirme desteklenmiyor veya yetki yetersiz
        }

        return false;
    }

    private async Task<PlayStationBatteryInfo?> QueryHidTelemetryBatteryAsync(string devicePath, ushort pid, CancellationToken cancellationToken)
    {
        try
        {
            using var handle = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                // Salt okuma modunda tekrar dene
                using var readHandle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GENERIC_READ,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (readHandle.IsInvalid) return null;
                return await ReadTelemetryReportAsync(readHandle, pid, cancellationToken);
            }

            // Kolu Bluetooth telemetri moduna geçir (DS4: Report 0x11, DualSense: Report 0x31)
            SwitchControllerToTelemetryMode(handle, pid);

            return await ReadTelemetryReportAsync(handle, pid, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static Task<PlayStationBatteryInfo?> ReadTelemetryReportAsync(SafeFileHandle handle, ushort pid, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                byte[] buffer = new byte[78];
                bool success = NativeMethods.ReadFile(handle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero);
                if (!success || bytesRead <= 0) return null;

                bool isDualSense = pid is PidDualSense or PidDualSenseEdge;
                string modelName = GetPlayStationModelName(pid);

                if (isDualSense)
                {
                    if (TryParseDualSenseReport(buffer, out var level, out var charging) && level.HasValue)
                    {
                        return new PlayStationBatteryInfo(level.Value, charging, modelName);
                    }
                }
                else
                {
                    if (TryParseDualShock4Report(buffer, out var level, out var charging) && level.HasValue)
                    {
                        return new PlayStationBatteryInfo(level.Value, charging, modelName);
                    }
                }
            }
            catch
            {
                // Okuma hatası
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
                // İstisna sessizce geçilir
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// DualShock 4 HID Input Report 0x11 (Bluetooth Telemetri) veya 0x01 (Temel) baytlarını ayrıştırır.
    /// </summary>
    public static bool TryParseDualShock4Report(byte[] report, out int? batteryLevel, out bool isCharging)
    {
        batteryLevel = null;
        isCharging = false;

        if (report == null || report.Length < 13)
        {
            return false;
        }

        byte reportId = report[0];

        // 1. Bluetooth Extended Telemetry Report (0x11) - Uzunluk >= 31
        if (reportId == 0x11 && report.Length >= 31)
        {
            byte batByte = report[30];
            int rawLevel = batByte & 0x0F;
            isCharging = (batByte & 0x10) != 0;

            if (rawLevel <= 10)
            {
                batteryLevel = rawLevel * 10;
            }
            else if (rawLevel == 11)
            {
                // Bazı DS4 modellerinde 11 değeri kablo takılı tam şarjı (%100) simgeler
                batteryLevel = 100;
                isCharging = true;
            }
            else
            {
                batteryLevel = 100;
            }

            return true;
        }

        // 2. Standart Basic Report (0x01) - Uzunluk >= 13
        if (reportId == 0x01 && report.Length >= 13)
        {
            byte batByte = report[12];
            int rawLevel = batByte & 0x0F;
            isCharging = (batByte & 0x10) != 0;

            batteryLevel = Math.Min(100, rawLevel * 10);
            return true;
        }

        return false;
    }

    /// <summary>
    /// DualSense (PS5) HID Input Report 0x31 (Bluetooth Telemetri) veya 0x01 baytlarını ayrıştırır.
    /// </summary>
    public static bool TryParseDualSenseReport(byte[] report, out int? batteryLevel, out bool isCharging)
    {
        batteryLevel = null;
        isCharging = false;

        if (report == null || report.Length < 54)
        {
            return false;
        }

        byte reportId = report[0];

        // 1. Bluetooth Extended Report 0x31 - Batarya verisi index 53'te
        if (reportId == 0x31 && report.Length >= 54)
        {
            byte batByte = report[53];
            int rawLevel = batByte & 0x0F;
            int chargeStatus = (batByte >> 4) & 0x0F;

            batteryLevel = Math.Min(100, rawLevel * 10);
            // 0: deşarj, 1: şarj ediliyor, 2: tam dolu
            isCharging = chargeStatus is 1 or 2;

            return true;
        }

        // 2. USB veya Alternatif Report 0x01 - Batarya index 53 veya 52'de
        if (reportId == 0x01 && report.Length >= 54)
        {
            byte batByte = report[53];
            int rawLevel = batByte & 0x0F;
            int chargeStatus = (batByte >> 4) & 0x0F;

            batteryLevel = Math.Min(100, rawLevel * 10);
            isCharging = chargeStatus is 1 or 2;

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

    public static bool IsPlayStationControllerPid(ushort pid)
    {
        return pid is PidDualShock4V1 or PidDualShock4V2 or PidDualShock4Dongle or PidDualSense or PidDualSenseEdge;
    }

    public static string GetPlayStationModelName(ushort pid)
    {
        return pid switch
        {
            PidDualShock4V1 => "Sony DualShock 4 Wireless Controller (v1)",
            PidDualShock4V2 => "Sony DualShock 4 Wireless Controller (v2)",
            PidDualShock4Dongle => "Sony DualShock 4 USB Wireless Adapter",
            PidDualSense => "Sony DualSense Wireless Controller (PS5)",
            PidDualSenseEdge => "Sony DualSense Edge Wireless Controller",
            _ => "Sony PlayStation Kontrolcü"
        };
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
            byte[] lpReportBuffer,
            int ReportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

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
