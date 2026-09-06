extern alias MonitorApp;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppApiServer = MonitorApp::BluetoothBatteryMonitor.App.Services.Api.LocalRestApiServer;
using AppAudioDeviceInfo = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioDeviceInfo;

namespace BluetoothBatteryMonitor.Tests;

public class LocalRestApiServerTests
{
    private static int _nextPort = 23300;
    private static int GetNextPort() => System.Threading.Interlocked.Increment(ref _nextPort);

    [Fact]
    public async Task GetDevices_ReturnsJsonArrayWithRequiredFields()
    {
        int port = GetNextPort();

        var devices = new List<AppDevice>
        {
            new AppDevice
            {
                Id = "DEV_SONY_TEST",
                Name = "Sony WH-1000XM4",
                Type = AppDeviceType.Headphones,
                AudioCodec = "LDAC",
                BatteryLevel = 85,
                IsCharging = false,
                IsConnected = true,
                ProviderSource = "GATT Pil Servisi"
            },
            new AppDevice
            {
                Id = "DEV_AIRPODS_TEST",
                Name = "AirPods Pro",
                Type = AppDeviceType.Earbuds,
                IsConnected = true,
                IsTws = true,
                LeftBatteryLevel = 90,
                RightBatteryLevel = 95,
                CaseBatteryLevel = 100,
                IsCaseCharging = true,
                ProviderSource = "Apple AirPods Beacon"
            }
        };

        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("EP_SONY", "Sony WH-1000XM4", null, true, 0.70f, false));

        using var server = new AppApiServer(() => devices, mockAudio, port);
        server.Start();

        if (!server.IsRunning)
        {
            // Skip test if port binding permission is not available in the environment
            return;
        }

        using var client = new HttpClient();
        var res = await client.GetAsync($"http://127.0.0.1:{port}/devices");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);

        string json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(2, array.GetArrayLength());

        var first = array[0];
        Assert.Equal("DEV_SONY_TEST", first.GetProperty("id").GetString());
        Assert.Equal("Sony WH-1000XM4", first.GetProperty("name").GetString());
        Assert.Equal("LDAC", first.GetProperty("audioCodec").GetString());
        Assert.Equal(85, first.GetProperty("batteryLevel").GetInt32());
        Assert.True(first.GetProperty("isConnected").GetBoolean());
        Assert.False(first.GetProperty("isCharging").GetBoolean());

        // Audio information
        var volume = first.GetProperty("volume");
        Assert.True(volume.GetProperty("hasAudioEndpoint").GetBoolean());
        Assert.Equal(70, volume.GetProperty("volumePercent").GetInt32());
        Assert.False(volume.GetProperty("isMuted").GetBoolean());

        // TWS information
        var second = array[1];
        Assert.True(second.GetProperty("isTws").GetBoolean());
        var tws = second.GetProperty("tws");
        Assert.Equal(90, tws.GetProperty("leftBatteryLevel").GetInt32());
        Assert.Equal(95, tws.GetProperty("rightBatteryLevel").GetInt32());
        Assert.Equal(100, tws.GetProperty("caseBatteryLevel").GetInt32());
        Assert.True(tws.GetProperty("isCaseCharging").GetBoolean());
    }

    [Fact]
    public async Task GetSummary_ReturnsAggregatedDeviceSummary()
    {
        int port = GetNextPort();

        var devices = new List<AppDevice>
        {
            new AppDevice
            {
                Id = "DEV_A",
                Name = "Cihaz A",
                Type = AppDeviceType.Headphones,
                BatteryLevel = 75,
                IsConnected = true
            },
            new AppDevice
            {
                Id = "DEV_B",
                Name = "Cihaz B",
                Type = AppDeviceType.Mouse,
                BatteryLevel = 30,
                IsConnected = true
            },
            new AppDevice
            {
                Id = "DEV_C",
                Name = "Cihaz C (Kopuk)",
                Type = AppDeviceType.Keyboard,
                BatteryLevel = 50,
                IsConnected = false
            }
        };

        using var server = new AppApiServer(() => devices, null, port);
        server.Start();

        if (!server.IsRunning) return;

        using var client = new HttpClient();
        var res = await client.GetAsync($"http://127.0.0.1:{port}/devices/summary");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        string json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("totalDevices").GetInt32());
        Assert.Equal(2, root.GetProperty("connectedDevices").GetInt32());
        Assert.Equal(30, root.GetProperty("lowestBatteryLevel").GetInt32());
    }

    [Fact]
    public async Task OptionsRequest_ReturnsNoContentWithCorsHeaders()
    {
        int port = GetNextPort();

        using var server = new AppApiServer(() => Array.Empty<AppDevice>(), null, port);
        server.Start();

        if (!server.IsRunning) return;

        using var client = new HttpClient();
        var req = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/devices");
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Contains(res.Headers, h => h.Key.Equals("Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WhenPortInUse_DoesNotThrowException()
    {
        int port = GetNextPort();

        using var server1 = new AppApiServer(() => Array.Empty<AppDevice>(), null, port);
        server1.Start();

        // Should not crash when a second listener is opened on the same port
        using var server2 = new AppApiServer(() => Array.Empty<AppDevice>(), null, port);
        var ex = Record.Exception(() => server2.Start());

        Assert.Null(ex);
        Assert.False(server2.IsRunning);
        Assert.NotNull(server2.LastError);
    }

    [Fact]
    public async Task GetDevices_SupportsQueryParam_ConnectedOnly()
    {
        int port = GetNextPort();

        var devices = new List<AppDevice>
        {
            new AppDevice
            {
                Id = "DEV_CONN",
                Name = "Bağlı Cihaz",
                IsConnected = true
            },
            new AppDevice
            {
                Id = "DEV_DISC",
                Name = "Kopuk Cihaz",
                IsConnected = false
            }
        };

        using var server = new AppApiServer(() => devices, null, port);
        server.Start();
        if (!server.IsRunning) return;

        using var client = new HttpClient();
        var res = await client.GetAsync($"http://127.0.0.1:{port}/devices?connected=true");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        string json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;

        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("DEV_CONN", array[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Restart_SwitchesToNewPortSuccessfully()
    {
        int initialPort = GetNextPort();
        int newPort = GetNextPort();

        var devices = new List<AppDevice>
        {
            new AppDevice { Id = "DEV_TEST", Name = "Test Device", BatteryLevel = 88, IsConnected = true }
        };

        using var server = new AppApiServer(() => devices, null, initialPort);
        server.Start();
        if (!server.IsRunning) return;

        using var client = new HttpClient();

        // 1. Should respond on initial port
        var res1 = await client.GetAsync($"http://127.0.0.1:{initialPort}/devices");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Should restart on new port
        server.Restart(newPort);
        Assert.Equal(newPort, server.Port);
        Assert.True(server.IsRunning);

        // 3. Should work on new port
        var res2 = await client.GetAsync($"http://127.0.0.1:{newPort}/devices");
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
    }

    [Theory]
    [InlineData(80, 23253)] // Smaller than 1024
    [InlineData(70000, 23253)] // Greater than 65535
    [InlineData(8080, 8080)] // Valid
    [InlineData(23253, 23253)] // Default
    public void ClampPort_ValidatesPortRanges(int input, int expected)
    {
        int result = AppApiServer.ClampPort(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Stop_StopsListeningImmediately()
    {
        int port = GetNextPort();
        using var server = new AppApiServer(() => Array.Empty<AppDevice>(), null, port);
        server.Start();
        if (!server.IsRunning) return;

        Assert.True(server.IsRunning);
        server.Stop();
        Assert.False(server.IsRunning);
    }
}
