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
            // Eğer özellik kapatılmışsa ve aktif yönlendirmemiz varsa geri al
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

        // 1. Yeni bağlanan veya henüz yönlendirilmemiş ses aygıtı var mı?
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
                        // İlk kez geçiş yapılıyorsa önceki varsayılanı hatırla
                        if (string.IsNullOrEmpty(_previousDefaultEndpointId))
                        {
                            _previousDefaultEndpointId = currentDefault;
                        }

                        if (_endpointManager.SetDefaultPlaybackDevice(endpoint.Id))
                        {
                            _activeAudioDeviceId = dev.Id;
                            _activeEndpointId = endpoint.Id;
                            _hadActiveEarbuds = dev.IsTws && (dev.LeftBatteryLevel.HasValue || dev.RightBatteryLevel.HasValue);
                            RouteChanged?.Invoke(this, $"Varsayılan ses çıkışı '{endpoint.Name}' olarak ayarlandı.");
                        }
                    }
                    else
                    {
                        // Halihazırda varsayılan bu aygıt
                        _activeAudioDeviceId = dev.Id;
                        _activeEndpointId = endpoint.Id;
                        _hadActiveEarbuds = dev.IsTws && (dev.LeftBatteryLevel.HasValue || dev.RightBatteryLevel.HasValue);
                    }

                    _knownConnectedDeviceIds.Add(dev.Id);
                }
                // Eğer endpoint henüz Windows tarafından enumerate edilmediyse (gecikmeli açılış),
                // _knownConnectedDeviceIds'e eklenmez ve sonraki tick'te tekrar taranır.
            }
        }

        // 2. Bağlantısı kopan veya kutuya konan ses aygıtı var mı?
        if (!string.IsNullOrEmpty(_activeAudioDeviceId))
        {
            var activeDev = audioDevices.FirstOrDefault(d => d.Id == _activeAudioDeviceId);
            bool isDisconnected = activeDev == null || !activeDev.IsConnected;

            if (activeDev != null && activeDev.IsTws && (activeDev.LeftBatteryLevel.HasValue || activeDev.RightBatteryLevel.HasValue))
            {
                _hadActiveEarbuds = true;
            }

            // TWS cihazlar kutusuna konduğunda:
            // - Kutu seviyesi varken kulaklıkların ikisi birden yayını kesmişse,
            // - veya her iki kulaklık kutuda şarj alıyorsa,
            // - veya daha önce kulaklık pilleri varken şimdi tamamen null olduysa ve ana pil de yoksa.
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

        // 3. Bağlantısı kopan cihazları bilinenler kümesinden çıkar
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
                string name = prevEndpoint?.Name ?? "Önceki Aygıt";
                RouteChanged?.Invoke(this, $"Varsayılan ses çıkışı önceki cihaza ('{name}') geri döndürüldü.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SmartAudioRouter] Fallback hatası: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Uygulama sonlandırılırken fallback yap
        FallbackToPreviousEndpoint();
    }
}
