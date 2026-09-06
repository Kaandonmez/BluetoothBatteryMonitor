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

public record SonyBatteryInfo(
    int? Level,
    int? LeftLevel,
    int? RightLevel,
    int? CaseLevel,
    bool IsCharging,
    bool IsLeftCharging,
    bool IsRightCharging,
    bool IsCaseCharging,
    bool IsTws,
    string? ModelName = null);

/// <summary>
/// Sony WH-1000XM3/XM4/XM5 ve WF-1000 serisi (WF-1000XM3/XM4/XM5, LinkBuds) kablosuz kulaklıklar için
/// Bluetooth RFCOMM SPP (0x0C başlangıç baytlı MDR protokolü) paketlerini ayrıştıran sağlayıcı.
/// </summary>
public class SonyHeadphonesBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Sony Headphones Connect (RFCOMM SPP)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    // Sony MDR Frame başlangıç ve bitiş belirteci
    public const byte SomByte = 0x0C;
    public const byte AckByte = 0x0E;

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
            // 1. Eşleşmiş Sony kulaklıklarını sistemde bul
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            var sonyDevices = deviceInfos.Where(d => IsSonyHeadphone(d.Name)).ToList();

            foreach (var info in sonyDevices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Önbellekte varsa önce onu kontrol et
                if (_devices.TryGetValue(info.Id, out var cached))
                {
                    var live = await TryQuerySonyRfcommBatteryAsync(info.Id, cancellationToken);
                    if (live != null)
                    {
                        cached.IsConnected = true;
                        ApplySonyBatteryInfo(cached, live);
                    }
                    else
                    {
                        cached.IsConnected = false;
                    }
                    resultList.Add(cached);
                    continue;
                }

                bool isTws = IsTwsSonyModel(info.Name);
                var devType = isTws ? DeviceType.Earbuds : DeviceType.Headphones;
                ulong mac = BluetoothDeviceModel.ExtractMacAddress(info.Id);

                var devModel = new BluetoothDeviceModel
                {
                    Id = info.Id,
                    Name = info.Name,
                    ModelName = info.Name,
                    DeviceType = devType,
                    IsConnected = false,
                    IsTws = isTws,
                    BluetoothAddress = mac,
                    ProviderSource = "Sony Headphones Connect (RFCOMM SPP)",
                    LastUpdated = DateTime.Now
                };

                // PnP üzerinden temel batarya fallback'i
                if (info.Properties.TryGetValue(PnpBatteryKey, out var pnpVal) && pnpVal != null &&
                    int.TryParse(pnpVal.ToString(), out int bLevel))
                {
                    devModel.BatteryLevel = bLevel;
                }

                // RFCOMM SPP üzerinden canlı durum sorgusunu dene
                var liveBattery = await TryQuerySonyRfcommBatteryAsync(info.Id, cancellationToken);
                if (liveBattery != null)
                {
                    devModel.IsConnected = true;
                    ApplySonyBatteryInfo(devModel, liveBattery);
                }

                _devices[devModel.Id] = devModel;
                resultList.Add(devModel);
            }
        }
        catch
        {
            // İstisnayı yakala
        }

        return resultList;
    }

    private async Task<SonyBatteryInfo?> TryQuerySonyRfcommBatteryAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1500);

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

            if (rfcommServices.Services.Count == 0) return null;

            var service = rfcommServices.Services[0];
            using var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cts.Token);

            // Sony MDR Pil Sorgu Paketi (SOM 0x0C, Seq 0x00, Length 0x0001, Command 0x02)
            using var writer = new DataWriter(socket.OutputStream);
            byte[] cmd = [SomByte, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x02, SomByte];
            writer.WriteBytes(cmd);
            await writer.StoreAsync().AsTask(cts.Token);

            using var reader = new DataReader(socket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            var loadTask = reader.LoadAsync(64).AsTask(cts.Token);
            uint bytesRead = await loadTask;
            if (bytesRead >= 6)
            {
                byte[] buffer = new byte[bytesRead];
                reader.ReadBytes(buffer);

                if (TryParseSonyPacket(buffer, out var info))
                {
                    return info;
                }
            }
        }
        catch
        {
            // RFCOMM bağlantı hatası
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
                // İstisna yutulur
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
    /// Sony MDR RFCOMM SPP (0x0C başlangıçlı) paketini ayrıştırır.
    /// Tekli kulaklıklar (WH-1000 serisi) veya TWS (WF-1000 serisi) pil formatlarını destekler.
    /// </summary>
    public static bool TryParseSonyPacket(byte[] packet, out SonyBatteryInfo? info)
    {
        info = null;
        if (packet == null || packet.Length < 6)
        {
            return false;
        }

        // 0x0C SOM baytını ara
        int somIndex = -1;
        for (int i = 0; i < packet.Length - 4; i++)
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
        if (remaining < 6) return false;

        // Byte 0: 0x0C
        // Byte 1: SeqNo
        // Format 1: 4-bayt büyük sonlu (Big-endian) uzunluk -> Byte 2-5, ardından Function Type Byte 6
        // Format 2: 2-bayt uzunluk -> Byte 2-3, ardından Function Type Byte 4
        int funcIndex;
        int payloadIndex;

        if (remaining >= 8 && packet[offset + 2] == 0x00 && packet[offset + 3] == 0x00)
        {
            // 4-bayt uzunluk
            funcIndex = offset + 6;
            payloadIndex = offset + 7;
        }
        else
        {
            // 2-bayt uzunluk
            funcIndex = offset + 4;
            payloadIndex = offset + 5;
        }

        if (funcIndex >= packet.Length) return false;

        byte funcType = packet[funcIndex];

        // Sony Batarya Fonksiyon Kodları: 0x02, 0x03, 0x22, 0x23, 0x04
        bool isBatteryFunc = funcType is 0x02 or 0x03 or 0x22 or 0x23 or 0x04;
        if (!isBatteryFunc && funcType != 0x00)
        {
            // Fonksiyon kodu tam eşleşmese de kalan yükü analiz et
            payloadIndex = funcIndex;
        }

        int payloadLength = packet.Length - payloadIndex;
        if (payloadLength <= 0) return false;

        // TWS (WF-1000 serisi) mi yoksa Tekli Kulaklık (WH-1000 serisi) mi?
        // TWS paketlerinde genelde Sol, Sağ, Kutu için 3 ayrı pil ve şarj durumu yer alır (payload >= 4)
        if (payloadLength >= 4)
        {
            byte bLeft = packet[payloadIndex];
            byte bRight = packet[payloadIndex + 1];
            byte bCase = packet[payloadIndex + 2];
            byte chargeByte = packet[payloadIndex + 3];

            // Eğer Sol ve Sağ geçerli yüzde ise (0-100)
            if (bLeft <= 100 && bRight <= 100)
            {
                int? left = bLeft;
                int? right = bRight;
                int? caseLvl = bCase <= 100 ? bCase : null;

                bool leftChg = (chargeByte & 0x01) != 0;
                bool rightChg = (chargeByte & 0x02) != 0;
                bool caseChg = (chargeByte & 0x04) != 0;

                int? mainLevel = Math.Min(left.Value, right.Value);

                info = new SonyBatteryInfo(
                    mainLevel,
                    left,
                    right,
                    caseLvl,
                    leftChg || rightChg || caseChg,
                    leftChg,
                    rightChg,
                    caseChg,
                    true);

                return true;
            }
        }

        // Tekli Kulaklık (WH-1000 serisi) formatı:
        // payloadIndex: Pil seviyesi (0-100 veya 0..10 adım)
        // payloadIndex + 1: Şarj durumu (0=deşarj, 1=şarjda)
        byte rawLevel = packet[payloadIndex];
        int level;
        if (rawLevel <= 10)
        {
            level = rawLevel * 10;
        }
        else if (rawLevel <= 100)
        {
            level = rawLevel;
        }
        else
        {
            return false;
        }

        bool isCharging = false;
        if (payloadIndex + 1 < packet.Length)
        {
            byte chg = packet[payloadIndex + 1];
            isCharging = chg == 1 || (chg & 0x01) != 0;
        }

        info = new SonyBatteryInfo(
            level,
            null,
            null,
            null,
            isCharging,
            false,
            false,
            false,
            false);

        return true;
    }

    public static void ApplySonyBatteryInfo(BluetoothDeviceModel model, SonyBatteryInfo info)
    {
        if (info.IsTws)
        {
            model.IsTws = true;
            model.LeftBatteryLevel = info.LeftLevel;
            model.IsLeftCharging = info.IsLeftCharging;
            model.RightBatteryLevel = info.RightLevel;
            model.IsRightCharging = info.IsRightCharging;
            model.CaseBatteryLevel = info.CaseLevel;
            model.IsCaseCharging = info.IsCaseCharging;
            model.IsCharging = info.IsCharging;
            model.BatteryLevel = model.EffectiveBatteryLevel;
        }
        else
        {
            model.BatteryLevel = info.Level;
            model.IsCharging = info.IsCharging;
        }

        model.ProviderSource = "Sony Headphones Connect (RFCOMM SPP)";
        model.LastUpdated = DateTime.Now;
    }

    public static bool IsSonyHeadphone(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("wh-1000") ||
               lower.Contains("wf-1000") ||
               lower.Contains("wi-1000") ||
               lower.Contains("linkbuds") ||
               lower.Contains("wh-ch") ||
               lower.Contains("wh-xb") ||
               lower.Contains("mdr-");
    }

    public static bool IsTwsSonyModel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("wf-1000") || lower.Contains("linkbuds");
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
