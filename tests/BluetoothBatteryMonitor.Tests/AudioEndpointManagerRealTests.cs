extern alias MonitorApp;

using System;
using Xunit;
using AppAudioEndpointManager = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioEndpointManager;

namespace BluetoothBatteryMonitor.Tests;

public class AudioEndpointManagerRealTests
{
    [Fact]
    public void AudioEndpointManager_GetPlaybackEndpoints_EnumeratesWithoutCrashingOnWindows()
    {
        using var manager = new AppAudioEndpointManager();
        var endpoints = manager.GetPlaybackEndpoints();

        Assert.NotNull(endpoints);

        // Windows sisteminde en azından enumerator çağrısı başarılı olmalıdır
        string? defaultId = manager.GetDefaultPlaybackDeviceId();
        if (!string.IsNullOrEmpty(defaultId))
        {
            var volumeInfo = manager.GetVolume(defaultId);
            Assert.NotNull(volumeInfo);
            Assert.InRange(volumeInfo.Value.VolumePercent, 0.0f, 1.0f);

            // Mevcut ses seviyesini ve mute durumunu koruyarak SetVolume ve SetMute testi
            bool setVolSuccess = manager.SetVolume(defaultId, volumeInfo.Value.VolumePercent);
            Assert.True(setVolSuccess);

            bool setMuteSuccess = manager.SetMute(defaultId, volumeInfo.Value.IsMuted);
            Assert.True(setMuteSuccess);

            // SetDefaultPlaybackDevice testi (halihazırdaki varsayılan aygıtı tekrar varsayılan yaparak güvenle test et)
            bool setDefaultSuccess = manager.SetDefaultPlaybackDevice(defaultId);
            Assert.True(setDefaultSuccess);
        }
    }
}
