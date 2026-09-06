using System;
using System.Collections.Generic;
using System.Linq;
using BluetoothBatteryMonitor.App.Models;
using Debug = global::System.Diagnostics.Debug;

namespace BluetoothBatteryMonitor.App.Services.Audio;

public class SmartAudioRouter : IDisposable
{
    private readonly IAudioEndpointManager _endpointManager;
    private string? _previousDefaultEndpointId;
    private string? _activeAudioDeviceId;
    private string? _activeEndpointId;
    private readonly HashSet<string> _knownConnectedDeviceIds = new();
    private bool _hadActiveEarbuds;
    private bool _isDisposed;

    public string? PreviousDefaultEndpointId => _previousDefaultEndpointId;
    public string? ActiveAudioDeviceId => _activeAudioDeviceId;
    public string? ActiveEndpointId => _activeEndpointId;

    public event EventHandler<string>? RouteChanged;

    public SmartAudioRouter(IAudioEndpointManager endpointManager)
    {
        _endpointManager = endpointManager ?? throw new ArgumentNullException(nameof(endpointManager));
    }

    public void OnDevicesUpdated(IReadOnlyList<BluetoothDeviceModel> devices, AppSettings settings)
    {
        if (_isDisposed || devices == null) return;

        if (!settings.AutoSwitchAudioEndpoint)
        {
            // If feature is disabled and an active override exists, revert to previous endpoint
            if (!string.IsNullOrEmpty(_activeEndpointId))
            {
                FallbackToPreviousEndpoint();
                _activeAudioDeviceId = null;
                _activeEndpointId = null;
                _hadActiveEarbuds = false;
            }
            return;
        }

        var audioDevices = devices
            .Where(d => d.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker)
            .ToList();

        // 1. Check for newly connected or not yet routed audio devices
        foreach (var dev in audioDevices)
        {
            if (!dev.IsConnected) continue;

            bool isNewlyConnected = !_knownConnectedDeviceIds.Contains(dev.Id);
            bool needsRouting = isNewlyConnected || (string.IsNullOrEmpty(_activeEndpointId) && dev.Id != _activeAudioDeviceId);

            if (needsRouting)
            {
                var endpoint = _endpointManager.FindEndpointForBluetoothDevice(dev);
                if (endpoint != null)
                {
                    var currentDefault = _endpointManager.GetDefaultPlaybackDeviceId();
                    if (!string.Equals(currentDefault, endpoint.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        // Remember previous default endpoint when switching for the first time
                        if (string.IsNullOrEmpty(_previousDefaultEndpointId))
                        {
                            _previousDefaultEndpointId = currentDefault;
                        }

                        if (_endpointManager.SetDefaultPlaybackDevice(endpoint.Id))
                        {
                            _activeAudioDeviceId = dev.Id;
                            _activeEndpointId = endpoint.Id;
                            _hadActiveEarbuds = dev.IsTws && (dev.LeftBatteryLevel.HasValue || dev.RightBatteryLevel.HasValue);
                            RouteChanged?.Invoke(this, $"Default audio endpoint routed to '{endpoint.Name}'.");
                        }
                    }
                    else
                    {
                        // Already set as current default endpoint
                        _activeAudioDeviceId = dev.Id;
                        _activeEndpointId = endpoint.Id;
                        _hadActiveEarbuds = dev.IsTws && (dev.LeftBatteryLevel.HasValue || dev.RightBatteryLevel.HasValue);
                    }

                    _knownConnectedDeviceIds.Add(dev.Id);
                }
                // If endpoint has not been enumerated by Windows yet (delayed startup),
                // it is not added to _knownConnectedDeviceIds and will be rescanned on next tick.
            }
        }

        // 2. Check for disconnected or case-docked audio devices
        if (!string.IsNullOrEmpty(_activeAudioDeviceId))
        {
            var activeDev = audioDevices.FirstOrDefault(d => d.Id == _activeAudioDeviceId);
            bool isDisconnected = activeDev == null || !activeDev.IsConnected;

            if (activeDev != null && activeDev.IsTws && (activeDev.LeftBatteryLevel.HasValue || activeDev.RightBatteryLevel.HasValue))
            {
                _hadActiveEarbuds = true;
            }

            // When TWS devices are placed in charging case:
            // - If case level exists while both earbuds stop transmitting,
            // - or both earbuds are actively charging inside the case,
            // - or earbuds previously reported levels but are now null with no main battery.
            bool isTwsInCase = activeDev != null && activeDev.IsTws &&
                               ((activeDev.CaseBatteryLevel.HasValue && !activeDev.LeftBatteryLevel.HasValue && !activeDev.RightBatteryLevel.HasValue) ||
                                (activeDev.IsLeftCharging && activeDev.IsRightCharging && activeDev.CaseBatteryLevel.HasValue) ||
                                (_hadActiveEarbuds && !activeDev.LeftBatteryLevel.HasValue && !activeDev.RightBatteryLevel.HasValue && !activeDev.BatteryLevel.HasValue));

            if (isDisconnected || isTwsInCase)
            {
                FallbackToPreviousEndpoint();
                _activeAudioDeviceId = null;
                _activeEndpointId = null;
                _hadActiveEarbuds = false;
            }
        }

        // 3. Purge disconnected devices from known active set
        foreach (var dev in audioDevices)
        {
            if (!dev.IsConnected)
            {
                _knownConnectedDeviceIds.Remove(dev.Id);
            }
        }
    }

    public bool FallbackToPreviousEndpoint()
    {
        if (string.IsNullOrEmpty(_previousDefaultEndpointId)) return false;

        string targetId = _previousDefaultEndpointId;
        _previousDefaultEndpointId = null;

        try
        {
            bool success = _endpointManager.SetDefaultPlaybackDevice(targetId);
            if (success)
            {
                var endpoints = _endpointManager.GetPlaybackEndpoints();
                var prevEndpoint = endpoints.FirstOrDefault(e =>
                    string.Equals(e.Id, targetId, StringComparison.OrdinalIgnoreCase));
                string name = prevEndpoint?.Name ?? "Previous Device";
                RouteChanged?.Invoke(this, $"Default audio endpoint restored to '{name}'.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SmartAudioRouter] Fallback error: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Restore endpoint when shutting down
        FallbackToPreviousEndpoint();
    }
}
