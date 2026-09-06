using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public record LogitechGHubBatteryInfo(
    string DeviceId,
    string Name,
    string ModelName,
    int BatteryLevel,
    bool IsCharging,
    string StatusDescription,
    DeviceType DeviceType);

/// <summary>
/// Arka planda çalışan Logitech G HUB yazılımının yerel WebSocket API'sine (ws://127.0.0.1:9010) bağlanarak
/// LIGHTSPEED kablosuz oyuncu fareleri, klavyeleri ve kulaklıklarının (G Pro, G502, G915 vb.)
/// anlık pil ve şarj durumlarını okuyan ve dinleyen sağlayıcı.
/// </summary>
public class LogitechGHubBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Logitech G HUB (LIGHTSPEED)";
    public bool IsSupported => true;

    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated;

    private const string GHubWebSocketUrl = "ws://127.0.0.1:9010";
    private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices = new(StringComparer.OrdinalIgnoreCase);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringLoopTask;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _isMonitoring;
    private bool _isDisposed;

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        // WebSocket bağlıysa mevcut önbelleği veya anlık sorguyu dön
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                await SendQueryDevicesListAsync(_webSocket, cancellationToken);
            }
            catch
            {
                // Gönderim başarısız olursa mevcut listeyi dön
            }

            return _devices.Values.ToList();
        }

        // Bağlı değilse tek seferlik hızlı kontrol yap (G HUB çalışmıyorsa 1.2 saniye içinde döner)
        try
        {
            using var quickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            quickCts.CancelAfter(1200);

            using var tempWs = new ClientWebSocket();
            await tempWs.ConnectAsync(new Uri(GHubWebSocketUrl), quickCts.Token);

            if (tempWs.State == WebSocketState.Open)
            {
                await SendQueryDevicesListAsync(tempWs, quickCts.Token);
                var response = await ReceiveTextMessageAsync(tempWs, quickCts.Token);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var parsed = ParseDevicesList(response);
                    foreach (var dev in parsed)
                    {
                        _devices[dev.Id] = dev;
                    }
                }
            }
        }
        catch
        {
            // G HUB çalışmıyor veya port kapalı, sessizce geç
        }

        return _devices.Values.ToList();
    }

    public void StartMonitoring()
    {
        if (_isMonitoring || _isDisposed) return;
        _isMonitoring = true;

        _monitoringCts = new CancellationTokenSource();
        _monitoringLoopTask = Task.Run(() => RunMonitoringLoopAsync(_monitoringCts.Token));
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        try
        {
            _monitoringCts?.Cancel();
            _webSocket?.Abort();
        }
        catch { }
    }

    private async Task RunMonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_isDisposed)
        {
            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();

                using var connCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connCts.CancelAfter(3000);

                await _webSocket.ConnectAsync(new Uri(GHubWebSocketUrl), connCts.Token);

                if (_webSocket.State == WebSocketState.Open)
                {
                    // Cihaz listesi ve batarya değişiklik aboneliklerini gönder
                    await SendQueryDevicesListAsync(_webSocket, token);
                    await SendSubscribeBatteryChangesAsync(_webSocket, token);

                    // Gelen olayları sürekli oku
                    while (_webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        string? message = await ReceiveTextMessageAsync(_webSocket, token);
                        if (message == null) break;

                        ProcessMessage(message);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // G HUB açık değil veya bağlantı koptu, yeniden denemeden önce bekle
            }

            try
            {
                // Yeniden bağlanma aralığı (G HUB açıldığında hemen yakalamak için 20 sn)
                await Task.Delay(TimeSpan.FromSeconds(20), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task SendQueryDevicesListAsync(ClientWebSocket ws, CancellationToken token)
    {
        string requestJson = "{\"verb\":\"GET\",\"path\":\"/devices/list\"}";
        byte[] buffer = Encoding.UTF8.GetBytes(requestJson);
        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, token);
    }

    private static async Task SendSubscribeBatteryChangesAsync(ClientWebSocket ws, CancellationToken token)
    {
        string subJson = "{\"verb\":\"SUBSCRIBE\",\"path\":\"/battery/state/changed\"}";
        byte[] buffer = Encoding.UTF8.GetBytes(subJson);
        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, token);
    }

    private static async Task<string?> ReceiveTextMessageAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void ProcessMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            // 1. Cihaz listesi yanıtı kontrolü
            var devices = ParseDevicesList(json);
            if (devices.Count > 0)
            {
                foreach (var dev in devices)
                {
                    _devices[dev.Id] = dev;
                    DeviceUpdated?.Invoke(this, dev);
                }
                return;
            }

            // 2. Batarya durum değişikliği olayı kontrolü
            var state = ParseBatteryStateChanged(json);
            if (state.HasValue && !string.IsNullOrWhiteSpace(state.Value.DeviceId))
            {
                string devId = state.Value.DeviceId;
                string prefixedId = devId.StartsWith("LogitechGHub_", StringComparison.OrdinalIgnoreCase)
                    ? devId
                    : $"LogitechGHub_{devId}";

                if (_devices.TryGetValue(prefixedId, out var existing) || _devices.TryGetValue(devId, out existing))
                {
                    if (state.Value.BatteryLevel.HasValue)
                        existing.BatteryLevel = state.Value.BatteryLevel.Value;
                    existing.IsCharging = state.Value.IsCharging;
                    existing.LastUpdated = DateTime.Now;
                    DeviceUpdated?.Invoke(this, existing);
                }
            }
        }
        catch
        {
            // JSON ayrıştırma istisnasını yut
        }
    }

    /// <summary>
    /// G HUB '/devices/list' veya benzeri JSON yanıtından cihazları ve pil durumlarını ayrıştırır.
    /// </summary>
    public static List<BluetoothDeviceModel> ParseDevicesList(string json)
    {
        var result = new List<BluetoothDeviceModel>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // payload, result, items veya doğrudan dizi arayışı
            JsonElement itemsElement = default;
            bool foundArray = false;

            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsElement = root;
                foundArray = true;
            }
            else if (root.TryGetProperty("payload", out var payloadElem))
            {
                if (payloadElem.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = payloadElem;
                    foundArray = true;
                }
                else if (payloadElem.TryGetProperty("items", out var subItems) && subItems.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = subItems;
                    foundArray = true;
                }
                else if (payloadElem.TryGetProperty("devices", out var subDevs) && subDevs.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = subDevs;
                    foundArray = true;
                }
            }
            else if (root.TryGetProperty("result", out var resultElem))
            {
                if (resultElem.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = resultElem;
                    foundArray = true;
                }
                else if (resultElem.TryGetProperty("items", out var rItems) && rItems.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = rItems;
                    foundArray = true;
                }
            }

            if (!foundArray) return result;

            foreach (var item in itemsElement.EnumerateArray())
            {
                string id = GetStringProp(item, "id", "deviceId") ?? string.Empty;
                string name = GetStringProp(item, "name", "displayName", "model", "modelName") ?? "Logitech G Gaming Cihazı";
                string modelName = GetStringProp(item, "model", "modelName") ?? name;

                if (string.IsNullOrWhiteSpace(id)) continue;

                int? batteryLevel = null;
                bool isCharging = false;

                if (item.TryGetProperty("battery", out var batElem))
                {
                    batteryLevel = GetIntProp(batElem, "percentage", "level", "batteryPercentage");
                    isCharging = (GetBoolProp(batElem, "charging", "isCharging") ?? false) ||
                                 (GetStringProp(batElem, "status")?.Equals("charging", StringComparison.OrdinalIgnoreCase) ?? false);
                }
                else
                {
                    batteryLevel = GetIntProp(item, "batteryPercentage", "batteryLevel", "percentage");
                    isCharging = GetBoolProp(item, "charging", "isCharging") ?? false;
                }

                bool isConnected = GetBoolProp(item, "connected", "isConnected") ?? true;
                var devType = DetectLogitechGType(name, modelName);

                var model = new BluetoothDeviceModel
                {
                    Id = $"LogitechGHub_{id}",
                    Name = CleanGHubName(name),
                    ModelName = modelName,
                    DeviceType = devType,
                    BatteryLevel = batteryLevel,
                    IsCharging = isCharging,
                    IsConnected = isConnected,
                    ProviderSource = "Logitech G HUB (LIGHTSPEED)",
                    LastUpdated = DateTime.Now
                };

                result.Add(model);
            }
        }
        catch
        {
            // JSON hatası
        }

        return result;
    }

    /// <summary>
    /// G HUB '/battery/state/changed' olay JSON paketini ayrıştırır.
    /// </summary>
    public static (string? DeviceId, int? BatteryLevel, bool IsCharging, string? Status)? ParseBatteryStateChanged(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement target = root;
            if (root.TryGetProperty("payload", out var payload))
            {
                target = payload;
            }

            string? devId = GetStringProp(target, "deviceId", "id");
            if (string.IsNullOrWhiteSpace(devId)) return null;

            int? level = GetIntProp(target, "percentage", "level", "batteryPercentage");
            string? status = GetStringProp(target, "status");
            bool isCharging = (GetBoolProp(target, "charging", "isCharging") ?? false) ||
                             (status?.Equals("charging", StringComparison.OrdinalIgnoreCase) ?? false);

            return (devId, level, isCharging, status);
        }
        catch
        {
            return null;
        }
    }

    public static DeviceType DetectLogitechGType(string? name, string? modelName)
    {
        string combined = $"{name} {modelName}".ToLowerInvariant();

        if (combined.Contains("mouse") || combined.Contains("g502") || combined.Contains("g703") ||
            combined.Contains("g903") || combined.Contains("g305") || combined.Contains("g604") ||
            combined.Contains("superlight") || combined.Contains("g pro x superlight") || combined.Contains("g pro wireless"))
        {
            return DeviceType.Mouse;
        }

        if (combined.Contains("keyboard") || combined.Contains("g915") || combined.Contains("g815") ||
            combined.Contains("g613") || combined.Contains("g513") || combined.Contains("g715") ||
            combined.Contains("pro x keyboard"))
        {
            return DeviceType.Keyboard;
        }

        if (combined.Contains("headset") || combined.Contains("headphone") || combined.Contains("g733") ||
            combined.Contains("g935") || combined.Contains("g535") || combined.Contains("g435") ||
            combined.Contains("pro x wireless"))
        {
            return DeviceType.Headphones;
        }

        return DeviceType.Generic;
    }

    public static string CleanGHubName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Logitech G Gaming Cihazı";
        return raw.Replace("Logitech ", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("G HUB ", "", StringComparison.OrdinalIgnoreCase)
                  .Trim();
    }

    private static string? GetStringProp(JsonElement elem, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            if (elem.TryGetProperty(p, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }
        }
        return null;
    }

    private static int? GetIntProp(JsonElement elem, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            if (elem.TryGetProperty(p, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int iVal))
                {
                    return iVal;
                }
                if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out int sVal))
                {
                    return sVal;
                }
            }
        }
        return null;
    }

    private static bool? GetBoolProp(JsonElement elem, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            if (elem.TryGetProperty(p, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
                if (val.ValueKind == JsonValueKind.String && bool.TryParse(val.GetString(), out bool bVal)) return bVal;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopMonitoring();
        _webSocket?.Dispose();
        _monitoringCts?.Dispose();
        _connectLock.Dispose();
        _devices.Clear();
    }
}
