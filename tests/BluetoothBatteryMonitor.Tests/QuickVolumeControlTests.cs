extern alias MonitorApp;

using System;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppDeviceItemViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.DeviceItemViewModel;
using AppAudioDeviceInfo = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioDeviceInfo;

namespace BluetoothBatteryMonitor.Tests;

public class QuickVolumeControlTests
{
    [Fact]
    public void AudioDevice_WithMatchingEndpoint_EnablesQuickVolumeControl()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("EP_WH1000", "WH-1000XM4", null, true, 0.65f, false));

        var device = new AppDevice
        {
            Id = "DEV_SONY",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true,
            BatteryLevel = 80
        };

        var vm = new AppDeviceItemViewModel(device, mockAudio);

        Assert.True(vm.HasAudioEndpoint);
        Assert.Equal("EP_WH1000", vm.AudioEndpointId);
        Assert.Equal(65, vm.VolumePercent);
        Assert.False(vm.IsMuted);
    }

    [Fact]
    public void NonAudioDevice_DisablesQuickVolumeControl()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("EP_DEFAULT", "Hoparlör", null, true, 0.5f, false));

        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE",
            Name = "Logitech MX Master",
            Type = AppDeviceType.Mouse,
            IsConnected = true,
            BatteryLevel = 90
        };

        var vm = new AppDeviceItemViewModel(mouse, mockAudio);

        Assert.False(vm.HasAudioEndpoint);
        Assert.Null(vm.AudioEndpointId);
    }

    [Fact]
    public void VolumeSliderChange_SynchronizesToAudioEndpoint()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("EP_JBL", "JBL Flip 5", null, true, 0.5f, false));

        var speaker = new AppDevice
        {
            Id = "DEV_JBL",
            Name = "JBL Flip 5",
            Type = AppDeviceType.Speaker,
            IsConnected = true
        };

        var vm = new AppDeviceItemViewModel(speaker, mockAudio);

        // User set volume to 80%
        vm.VolumePercent = 80;

        Assert.True(mockAudio.Volumes.ContainsKey("EP_JBL"));
        Assert.Equal(0.80f, mockAudio.Volumes["EP_JBL"], 2);
    }

    [Fact]
    public void ToggleMuteCommand_TogglesMuteStateAndSynchronizes()
    {
        var mockAudio = new MockAudioEndpointManager();
        mockAudio.Endpoints.Add(new AppAudioDeviceInfo("EP_BUDS", "Galaxy Buds2 Pro", null, true, 0.4f, false));

        var earbuds = new AppDevice
        {
            Id = "DEV_BUDS",
            Name = "Galaxy Buds2 Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true
        };

        var vm = new AppDeviceItemViewModel(earbuds, mockAudio);

        Assert.False(vm.IsMuted);

        // Mute
        vm.ToggleMuteCommand.Execute(null);

        Assert.True(vm.IsMuted);
        Assert.True(mockAudio.Mutes["EP_BUDS"]);

        // Unmute
        vm.ToggleMuteCommand.Execute(null);

        Assert.False(vm.IsMuted);
        Assert.False(mockAudio.Mutes["EP_BUDS"]);
    }
}
