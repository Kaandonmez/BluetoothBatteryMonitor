using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public record SamsungBudsBatteryInfo(
    int? LeftLevel,
    int? RightLevel,
    int? CaseLevel,
    bool IsLeftCharging,
    bool IsRightCharging,
    bool IsCaseCharging,
    string? ModelName = null);

/// <summary>
/// Battery provider for Samsung Galaxy Buds series (Buds, Buds+, Buds Live, Buds Pro, Buds2, Buds FE).
/// Parses 0xFD-prefixed status frames over RFCOMM SPP (Serial Port Profile) to report Left, Right,
/// and Case battery levels along with independent charging statuses.
/// </summary>
public class SamsungGalaxyBudsBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Samsung Galaxy Buds (RFCOMM SPP)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    // Samsung Galaxy Buds SOM (Start of Message)
    public const byte SomByte = 0xFD;
    public const byte MsgIdExtendedStatus = 0x60;
    public const byte MsgIdBasicStatus = 0x61;
    public const byte MsgIdBatteryStatus = 0x62;

    private const string PnpBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly Guid RfcommSppUuid = new("00001101-0000-1000-8000-00805F9B34FB");

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
            // 1. Discover paired Samsung Buds devices in system
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            var budsInfos = deviceInfos.Where(d => IsSamsungBudsDevice(d.Name)).ToList();

            foreach (var info in budsInfos)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Check cache for existing rich TWS model scanned via SPP
                if (_devices.TryGetValue(info.Id, out var cached) && cached.IsTws)
                {
                    var live = await TryQueryRfcommBatteryAsync(info.Id, cancellationToken);
                    if (live != null)
                    {
                        cached.IsConnected = true;
                        ApplyBudsBatteryInfo(cached, live);
                    }
                    else
                    {
                        cached.IsConnected = false;
                    }
                    resultList.Add(cached);
                    continue;
                }

                // Create device model
                string modelName = DetectBudsModelName(info.Name);
                ulong mac = BluetoothDeviceModel.ExtractMacAddress(info.Id);

                var devModel = new BluetoothDeviceModel
                {
                    Id = info.Id,
                    Name = info.Name,
                    ModelName = modelName,
                    DeviceType = DeviceType.Earbuds,
                    IsConnected = false,
                    IsTws = true,
                    BluetoothAddress = mac,
                    ProviderSource = "Samsung Galaxy Buds (RFCOMM SPP)",
                    LastUpdated = DateTime.Now
                };

                // Fallback to basic battery level via PnP
                if (info.Properties.TryGetValue(PnpBatteryKey, out var pnpVal) && pnpVal != null &&
                    int.TryParse(pnpVal.ToString(), out int bLevel))
                {
                    devModel.BatteryLevel = bLevel;
                }

                // Attempt to query real-time status packet over RFCOMM
                var liveBattery = await TryQueryRfcommBatteryAsync(info.Id, cancellationToken);
                if (liveBattery != null)
                {
                    devModel.IsConnected = true;
                    ApplyBudsBatteryInfo(devModel, liveBattery);
                }

                _devices[devModel.Id] = devModel;
                resultList.Add(devModel);
            }
        }
        catch
        {
            // Ignore transient scan exception
        }

        return resultList;
    }

    private async Task<SamsungBudsBatteryInfo?> TryQueryRfcommBatteryAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1500); // Short timeout for Bluetooth socket connection

            using var bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceId).AsTask(cts.Token);
            if (bluetoothDevice == null) return null;

            var rfcommServices = await bluetoothDevice.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(RfcommSppUuid),
                BluetoothCacheMode.Uncached).AsTask(cts.Token);

            if (rfcommServices.Services.Count == 0)
            {
                rfcommServices = await bluetoothDevice.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(RfcommSppUuid),
                    BluetoothCacheMode.Cached).AsTask(cts.Token);
            }

            if (rfcommServices.Services.Count == 0)
            {
                // Query all available RFCOMM services on device if standard SPP UUID is not found
                rfcommServices = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(cts.Token);
            }

            if (rfcommServices.Services.Count == 0) return null;

            var service = rfcommServices.Services[0];
            using var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cts.Token);

            // Send Buds status request packet: 0xFD 0x00 0x00 0x60 (SOM + Length + MsgId)
            using var writer = new DataWriter(socket.OutputStream);
            byte[] req = [SomByte, 0x00, 0x00, MsgIdExtendedStatus];
            writer.WriteBytes(req);
            await writer.StoreAsync().AsTask(cts.Token);

            using var reader = new DataReader(socket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            var loadTask = reader.LoadAsync(64).AsTask(cts.Token);
            uint bytesRead = await loadTask;
            if (bytesRead >= 7)
            {
                byte[] buffer = new byte[bytesRead];
                reader.ReadBytes(buffer);

                if (TryParseGalaxyBudsPacket(buffer, out var info))
                {
                    return info;
                }
            }
        }
        catch
        {
            // RFCOMM connection or read error
        }

        return null;
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
                await Task.Delay(TimeSpan.FromSeconds(25), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Parses Samsung Galaxy Buds 0xFD-prefixed RFCOMM SPP status packets.
    /// Decodes Extended Status (0x60), Basic Status (0x61), or Battery Status (0x62) formats.
    /// </summary>
    public static bool TryParseGalaxyBudsPacket(byte[] packet, out SamsungBudsBatteryInfo? info)
    {
        info = null;
        if (packet == null || packet.Length < 7)
        {
            return false;
        }

        // Search for Start of Message (SOM) byte (0xFD)
        int somIndex = -1;
        for (int i = 0; i < packet.Length - 6; i++)
        {
            if (packet[i] == SomByte)
            {
                somIndex = i;
                break;
            }
        }

        if (somIndex == -1) return false;

        int offset = somIndex;
        int remaining = packet.Length - offset;
        if (remaining < 7) return false;

        // Byte 0: 0xFD
        // Byte 1-2: Length (little-endian)
        // Byte 3: MsgId
        byte msgId = packet[offset + 3];

        int? leftLevel = null;
        int? rightLevel = null;
        int? caseLevel = null;
        bool isLeftCharging = false;
        bool isRightCharging = false;
        bool isCaseCharging = false;

        if (msgId == MsgIdExtendedStatus && remaining >= 10)
        {
            // 0x60: MSG_ID_EXTENDED_STATUS_UPDATED
            // offset + 4: Revision / Model info
            // offset + 5: Left earbud battery (0 - 100)
            // offset + 6: Right earbud battery (0 - 100)
            // offset + 7: Coupled / Wearing state
            // offset + 8: Case battery (0 - 100)
            // offset + 9: Charging bits (bit 0: left, bit 1: right, bit 2: case)
            byte rawLeft = packet[offset + 5];
            byte rawRight = packet[offset + 6];
            byte rawCase = packet[offset + 8];
            byte chargeBits = packet[offset + 9];

            if (rawLeft <= 100) leftLevel = rawLeft;
            if (rawRight <= 100) rightLevel = rawRight;
            if (rawCase <= 100) caseLevel = rawCase;

            isLeftCharging = (chargeBits & 0x01) != 0;
            isRightCharging = (chargeBits & 0x02) != 0;
            isCaseCharging = (chargeBits & 0x04) != 0;
        }
        else if (msgId == MsgIdBasicStatus && remaining >= 8)
        {
            // 0x61: MSG_ID_STATUS_UPDATED
            byte rawLeft = packet[offset + 4];
            byte rawRight = packet[offset + 5];
            byte rawCase = packet[offset + 6];
            byte chargeBits = packet[offset + 7];

            if (rawLeft <= 100) leftLevel = rawLeft;
            if (rawRight <= 100) rightLevel = rawRight;
            if (rawCase <= 100) caseLevel = rawCase;

            isLeftCharging = (chargeBits & 0x01) != 0;
            isRightCharging = (chargeBits & 0x02) != 0;
            isCaseCharging = (chargeBits & 0x04) != 0;
        }
        else if (msgId == MsgIdBatteryStatus && remaining >= 7)
        {
            // 0x62: MSG_ID_BATTERY_STATUS
            byte rawLeft = packet[offset + 4];
            byte rawRight = packet[offset + 5];
            byte rawCase = packet[offset + 6];

            if (rawLeft <= 100) leftLevel = rawLeft;
            if (rawRight <= 100) rightLevel = rawRight;
            if (rawCase <= 100) caseLevel = rawCase;
        }
        else
        {
            // Generic Buds payload scan: safely look for valid battery values (0-100)
            if (remaining >= 9)
            {
                byte b1 = packet[offset + 4];
                byte b2 = packet[offset + 5];
                byte b3 = packet[offset + 7];

                if (b1 <= 100 && b2 <= 100)
                {
                    leftLevel = b1;
                    rightLevel = b2;
                    if (b3 <= 100) caseLevel = b3;

                    byte c = packet[offset + 8];
                    isLeftCharging = (c & 0x01) != 0;
                    isRightCharging = (c & 0x02) != 0;
                    isCaseCharging = (c & 0x04) != 0;
                }
            }
        }

        if (!leftLevel.HasValue && !rightLevel.HasValue && !caseLevel.HasValue)
        {
            return false;
        }

        info = new SamsungBudsBatteryInfo(
            leftLevel,
            rightLevel,
            caseLevel,
            isLeftCharging,
            isRightCharging,
            isCaseCharging);

        return true;
    }

    public static void ApplyBudsBatteryInfo(BluetoothDeviceModel model, SamsungBudsBatteryInfo info)
    {
        model.IsTws = true;
        model.LeftBatteryLevel = info.LeftLevel;
        model.IsLeftCharging = info.IsLeftCharging;
        model.RightBatteryLevel = info.RightLevel;
        model.IsRightCharging = info.IsRightCharging;
        model.CaseBatteryLevel = info.CaseLevel;
        model.IsCaseCharging = info.IsCaseCharging;
        model.IsCharging = info.IsLeftCharging || info.IsRightCharging || info.IsCaseCharging;
        model.BatteryLevel = model.EffectiveBatteryLevel;
        model.ProviderSource = "Samsung Galaxy Buds (RFCOMM SPP)";
        model.LastUpdated = DateTime.Now;
    }

    public static bool IsSamsungBudsDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("galaxy buds") ||
               lower.Contains("buds+") ||
               lower.Contains("buds live") ||
               lower.Contains("buds pro") ||
               lower.Contains("buds2") ||
               lower.Contains("buds3") ||
               lower.Contains("buds fe");
    }

    public static string DetectBudsModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Samsung Galaxy Buds";
        string lower = name.ToLowerInvariant();

        if (lower.Contains("buds3 pro")) return "Samsung Galaxy Buds3 Pro";
        if (lower.Contains("buds3")) return "Samsung Galaxy Buds3";
        if (lower.Contains("buds2 pro")) return "Samsung Galaxy Buds2 Pro";
        if (lower.Contains("buds2")) return "Samsung Galaxy Buds2";
        if (lower.Contains("buds pro")) return "Samsung Galaxy Buds Pro";
        if (lower.Contains("buds live")) return "Samsung Galaxy Buds Live";
        if (lower.Contains("buds+")) return "Samsung Galaxy Buds+";
        if (lower.Contains("buds fe")) return "Samsung Galaxy Buds FE";
        if (lower.Contains("buds")) return "Samsung Galaxy Buds";

        return "Samsung Galaxy Buds";
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
