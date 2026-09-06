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
/// Connects to the local WebSocket API of running Logitech G HUB software (ws://127.0.0.1:9010)
/// to read and monitor real-time battery and charging status for LIGHTSPEED wireless gaming mice,
/// keyboards, and headsets (G Pro, G502, G915, etc.).
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
        // If WebSocket is connected, return cached or queried list
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                await SendQueryDevicesListAsync(_webSocket, cancellationToken);
            }
            catch
            {
                // If sending query fails, return current list
            }

            return _devices.Values.ToList();
        }

        // If not connected, perform one-shot fast check (returns in 1.2s if G HUB is not running)
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
            // G HUB is not running or port is closed, ignore silently
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
                    // Send device list and battery state change subscriptions
                    await SendQueryDevicesListAsync(_webSocket, token);
                    await SendSubscribeBatteryChangesAsync(_webSocket, token);

                    // Continuously read incoming events
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
                // G HUB is not open or disconnected, wait before retrying
            }

            try
            {
                // Reconnection delay (20s interval to quickly detect when G HUB opens)
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
            // 1. Check device list response
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

            // 2. Check battery state changed event
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
            // Suppress JSON parsing exception
        }
    }

    /// <summary>
    /// Parses devices and battery states from G HUB '/devices/list' or similar JSON responses.
    /// </summary>
    public static List<BluetoothDeviceModel> ParseDevicesList(string json)
    {
        var result = new List<BluetoothDeviceModel>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Search for payload, result, items, or direct array
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
                string name = GetStringProp(item, "name", "displayName", "model", "modelName") ?? "Logitech G Gaming Device";
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
            // JSON parsing error
        }

        return result;
    }

    /// <summary>
    /// Parses G HUB '/battery/state/changed' event JSON packet.
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
        if (string.IsNullOrWhiteSpace(raw)) return "Logitech G Gaming Device";
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
