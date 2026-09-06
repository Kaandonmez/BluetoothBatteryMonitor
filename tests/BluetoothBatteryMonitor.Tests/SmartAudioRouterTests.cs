extern alias MonitorApp;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppSettings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using AppRouter = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.SmartAudioRouter;
using AppIAudioEndpointManager = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.IAudioEndpointManager;
using AppAudioDeviceInfo = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioDeviceInfo;

namespace BluetoothBatteryMonitor.Tests;

public class MockAudioEndpointManager : AppIAudioEndpointManager
{
    public List<AppAudioDeviceInfo> Endpoints { get; } = new();
    public string? DefaultEndpointId { get; set; } = "SPEAKER_DEFAULT_ID";
    public List<string> SwitchedEndpointIds { get; } = new();
    public Dictionary<string, float> Volumes { get; } = new();
    public Dictionary<string, bool> Mutes { get; } = new();

    public IReadOnlyList<AppAudioDeviceInfo> GetPlaybackEndpoints() => Endpoints;

    public string? GetDefaultPlaybackDeviceId() => DefaultEndpointId;

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        DefaultEndpointId = deviceId;
        SwitchedEndpointIds.Add(deviceId);
        return true;
    }

    public (float VolumePercent, bool IsMuted)? GetVolume(string? deviceId = null)
    {
        string id = deviceId ?? DefaultEndpointId ?? "";
        float vol = Volumes.TryGetValue(id, out var v) ? v : 0.75f;
        bool mute = Mutes.TryGetValue(id, out var m) && m;
        return (vol, mute);
    }

    public bool SetVolume(string? deviceId, float volumeScalar)
    {
        string id = deviceId ?? DefaultEndpointId ?? "";
        Volumes[id] = volumeScalar;
        return true;
    }

    public bool SetMute(string? deviceId, bool isMuted)
    {
        string id = deviceId ?? DefaultEndpointId ?? "";
        Mutes[id] = isMuted;
        return true;
    }

    public AppAudioDeviceInfo? FindEndpointForBluetoothDevice(AppDevice device)
    {
        return Endpoints.FirstOrDefault(e =>
            e.Name.Contains(device.Name, StringComparison.OrdinalIgnoreCase) ||
            device.Name.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() { }
}

public class SmartAudioRouterTests
{
    [Fact]
    public void WhenAudioDeviceConnects_SwitchesDefaultAudioEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_HEADPHONE_ID", "Sony WH-1000XM4", null, false, 0.8f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var device = new AppDevice
        {
            Id = "DEV_SONY",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true,
            BatteryLevel = 90
        };

        router.OnDevicesUpdated(new[] { device }, settings);

        // Default output should switch to headphones
        Assert.Equal("BT_HEADPHONE_ID", mockAudio.DefaultEndpointId);
        Assert.Equal("SPEAKER_DEFAULT_ID", router.PreviousDefaultEndpointId);
        Assert.Equal("BT_HEADPHONE_ID", router.ActiveEndpointId);
    }

    [Fact]
    public void WhenAutoSwitchAudioEndpointIsDisabled_DoesNotSwitchEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_HEADPHONE_ID", "Sony WH-1000XM4", null, false, 0.8f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = false };

        var device = new AppDevice
        {
            Id = "DEV_SONY",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        router.OnDevicesUpdated(new[] { device }, settings);

        // There should be no change
        Assert.Equal("SPEAKER_DEFAULT_ID", mockAudio.DefaultEndpointId);
        Assert.Null(router.ActiveEndpointId);
    }

    [Fact]
    public void WhenNonAudioDeviceConnects_DoesNotSwitchEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE",
            Name = "MX Master 3S",
            Type = AppDeviceType.Mouse,
            IsConnected = true
        };

        router.OnDevicesUpdated(new[] { mouse }, settings);

        Assert.Equal("SPEAKER_DEFAULT_ID", mockAudio.DefaultEndpointId);
        Assert.Null(router.ActiveEndpointId);
    }

    [Fact]
    public void WhenAudioDeviceDisconnects_FallsBackToPreviousDefaultEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_HEADPHONE_ID", "Sony WH-1000XM4", null, false, 0.8f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var device = new AppDevice
        {
            Id = "DEV_SONY",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        // 1. Connected -> switched to headphones
        router.OnDevicesUpdated(new[] { device }, settings);
        Assert.Equal("BT_HEADPHONE_ID", mockAudio.DefaultEndpointId);

        // 2. Disconnected -> should fall back to previous default speaker
        device.IsConnected = false;
        router.OnDevicesUpdated(new[] { device }, settings);

        Assert.Equal("SPEAKER_DEFAULT_ID", mockAudio.DefaultEndpointId);
        Assert.Null(router.ActiveEndpointId);
    }

    [Fact]
    public void WhenTwsEarbudsPutInCase_FallsBackToPreviousDefaultEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_AIRPODS_ID", "AirPods Pro", null, false, 0.7f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var airpods = new AppDevice
        {
            Id = "DEV_AIRPODS",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true,
            IsTws = true,
            LeftBatteryLevel = 80,
            RightBatteryLevel = 85,
            CaseBatteryLevel = 100
        };

        // Connected
        router.OnDevicesUpdated(new[] { airpods }, settings);
        Assert.Equal("BT_AIRPODS_ID", mockAudio.DefaultEndpointId);

        // Placed in case (left and right earbuds stopped transmitting / their batteries became null)
        airpods.LeftBatteryLevel = null;
        airpods.RightBatteryLevel = null;
        router.OnDevicesUpdated(new[] { airpods }, settings);

        // Should revert to speaker
        Assert.Equal("SPEAKER_DEFAULT_ID", mockAudio.DefaultEndpointId);
        Assert.Null(router.ActiveEndpointId);
    }

    [Fact]
    public void WhenEndpointIsInitiallyDelayed_RoutesWhenEndpointBecomesAvailableOnNextTick()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var device = new AppDevice
        {
            Id = "DEV_SONY",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        // Tick 1: Headphones connected but Windows CoreAudio endpoint is not ready yet
        router.OnDevicesUpdated(new[] { device }, settings);
        Assert.Equal("SPEAKER_DEFAULT_ID", mockAudio.DefaultEndpointId);
        Assert.Null(router.ActiveEndpointId);

        // Tick 2: Windows CoreAudio endpoint is now ready and enumerated
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_HEADPHONE_ID", "Sony WH-1000XM4", null, false, 0.8f, false));
        router.OnDevicesUpdated(new[] { device }, settings);

        // Should now automatically switch to headphones!
        Assert.Equal("BT_HEADPHONE_ID", mockAudio.DefaultEndpointId);
        Assert.Equal("BT_HEADPHONE_ID", router.ActiveEndpointId);
        Assert.Equal("SPEAKER_DEFAULT_ID", router.PreviousDefaultEndpointId);
    }

    [Fact]
    public void WhenTwsEarbudsHaveOnlyCompositeBattery_DoesNotFalselyTriggerFallback()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("SPEAKER_DEFAULT_ID", "Dahili Hoparlör", null, true, 0.5f, false));
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("BT_WF1000_ID", "WF-1000XM4", null, false, 0.7f, false));

        using var router = new AppRouter(mockAudio);
        var settings = new AppSettings { AutoSwitchAudioEndpoint = true };

        var wf1000 = new AppDevice
        {
            Id = "DEV_SONY_TWS",
            Name = "WF-1000XM4",
            Type = AppDeviceType.Earbuds,
            IsConnected = true,
            IsTws = true,
            BatteryLevel = 75,
            LeftBatteryLevel = null, // Left/right distinction not yet received or reported as composite
            RightBatteryLevel = null,
            CaseBatteryLevel = null
        };

        // Connected
        router.OnDevicesUpdated(new[] { wf1000 }, settings);
        Assert.Equal("BT_WF1000_ID", mockAudio.DefaultEndpointId);

        // Second update (still connected with single battery)
        router.OnDevicesUpdated(new[] { wf1000 }, settings);

        // Should not mistakenly assume it was put in case and fall back to speaker!
        Assert.Equal("BT_WF1000_ID", mockAudio.DefaultEndpointId);
        Assert.Equal("BT_WF1000_ID", router.ActiveEndpointId);
    }
}
