using System;
using System.Collections.Generic;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Audio;

public record AudioDeviceInfo(
    string Id,
    string Name,
    string? Description,
    bool IsDefaultPlayback,
    float VolumePercent, // 0.0 - 1.0
    bool IsMuted,
    string? DeviceInstanceId = null
);

public interface IAudioEndpointManager : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> GetPlaybackEndpoints();
    string? GetDefaultPlaybackDeviceId();
    bool SetDefaultPlaybackDevice(string deviceId);
    (float VolumePercent, bool IsMuted)? GetVolume(string? deviceId = null);
    bool SetVolume(string? deviceId, float volumeScalar); // 0.0 to 1.0
    bool SetMute(string? deviceId, bool isMuted);
    AudioDeviceInfo? FindEndpointForBluetoothDevice(BluetoothDeviceModel device);
}
