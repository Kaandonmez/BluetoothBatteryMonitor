using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothBatteryMonitor.App.Models;
using Wpf.Ui.Controls;

using BluetoothBatteryMonitor.App.Services.Audio;
using BluetoothBatteryMonitor.App.Services.Localization;
using CommunityToolkit.Mvvm.Input;

namespace BluetoothBatteryMonitor.App.ViewModels;

public partial class DeviceItemViewModel : ObservableObject
{
    private readonly BluetoothDeviceModel _model;
    private readonly IAudioEndpointManager? _audioManager;
    private bool _isUpdatingVolumeInternally;

    public DeviceItemViewModel(BluetoothDeviceModel model, IAudioEndpointManager? audioManager = null)
    {
        _model = model;
        _audioManager = audioManager;
        UpdateFromModel(model);
    }

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private SymbolRegular _iconSymbol = SymbolRegular.Headphones24;

    [ObservableProperty]
    private string _batteryText = "--";

    [ObservableProperty]
    private int _batteryProgress = 0;

    [ObservableProperty]
    private Brush _batteryBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _connectionStatusText = "Not Connected";

    [ObservableProperty]
    private string _providerSource = string.Empty;

    // TWS Fields
    [ObservableProperty]
    private bool _isTws;

    [ObservableProperty]
    private string _leftBatteryText = "--";

    [ObservableProperty]
    private int _leftBatteryProgress = 0;

    [ObservableProperty]
    private bool _isLeftCharging;

    [ObservableProperty]
    private string _rightBatteryText = "--";

    [ObservableProperty]
    private int _rightBatteryProgress = 0;

    [ObservableProperty]
    private bool _isRightCharging;

    [ObservableProperty]
    private string _caseBatteryText = "--";

    [ObservableProperty]
    private int _caseBatteryProgress = 0;

    [ObservableProperty]
    private bool _isCaseCharging;

    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private bool _hasLeftBattery;

    [ObservableProperty]
    private bool _hasRightBattery;

    [ObservableProperty]
    private bool _hasCaseBattery;

    // Audio Control Fields (Quick Volume Control)
    [ObservableProperty]
    private bool _hasAudioEndpoint;

    [ObservableProperty]
    private string? _audioEndpointId;

    [ObservableProperty]
    private int _volumePercent = 100;

    [ObservableProperty]
    private bool _isMuted;

    // Audio Codec Info (SBC, AAC, aptX, aptX HD, LDAC)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudioCodec))]
    [NotifyPropertyChangedFor(nameof(AudioCodecToolTip))]
    private string? _audioCodec;

    public bool HasAudioCodec => !string.IsNullOrWhiteSpace(AudioCodec);

    public string AudioCodecToolTip => AudioCodec switch
    {
        "LDAC" => LocalizationService.GetString("Codec_LDAC"),
        "aptX HD" => LocalizationService.GetString("Codec_AptXHD"),
        "aptX" => LocalizationService.GetString("Codec_AptX"),
        "AAC" => LocalizationService.GetString("Codec_AAC"),
        "SBC" => LocalizationService.GetString("Codec_SBC"),
        _ => LocalizationService.GetString("Codec_Active", AudioCodec ?? string.Empty)
    };

    // Device Specific Battery Alert Threshold
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomThreshold))]
    [NotifyPropertyChangedFor(nameof(EffectiveThreshold))]
    [NotifyPropertyChangedFor(nameof(CustomThresholdBadgeText))]
    [NotifyPropertyChangedFor(nameof(ThresholdDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsThresholdDefault))]
    [NotifyPropertyChangedFor(nameof(IsThreshold10))]
    [NotifyPropertyChangedFor(nameof(IsThreshold15))]
    [NotifyPropertyChangedFor(nameof(IsThreshold20))]
    [NotifyPropertyChangedFor(nameof(IsThreshold25))]
    [NotifyPropertyChangedFor(nameof(IsThreshold30))]
    [NotifyPropertyChangedFor(nameof(IsThreshold40))]
    private int? _customThreshold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveThreshold))]
    [NotifyPropertyChangedFor(nameof(ThresholdDisplayText))]
    private int _globalThreshold = 20;

    public bool HasCustomThreshold => CustomThreshold.HasValue && CustomThreshold.Value > 0;

    public int EffectiveThreshold => CustomThreshold ?? GlobalThreshold;

    public bool IsThresholdDefault => !HasCustomThreshold;
    public bool IsThreshold10 => CustomThreshold == 10;
    public bool IsThreshold15 => CustomThreshold == 15;
    public bool IsThreshold20 => CustomThreshold == 20;
    public bool IsThreshold25 => CustomThreshold == 25;
    public bool IsThreshold30 => CustomThreshold == 30;
    public bool IsThreshold40 => CustomThreshold == 40;

    public string CustomThresholdBadgeText => HasCustomThreshold
        ? LocalizationService.GetString("Device_CustomThresholdBadge", CustomThreshold!)
        : string.Empty;

    public string ThresholdDisplayText => HasCustomThreshold
        ? LocalizationService.GetString("Device_ThresholdCustom", CustomThreshold!)
        : LocalizationService.GetString("Device_ThresholdDefault", GlobalThreshold);

    [RelayCommand]
    public void SetThreshold(object? parameter)
    {
        int? newThreshold = null;
        if (parameter is int intVal && intVal > 0)
        {
            newThreshold = intVal;
        }
        else if (parameter is string strVal && int.TryParse(strVal, out int parsedVal) && parsedVal > 0)
        {
            newThreshold = parsedVal;
        }

        CustomThreshold = newThreshold;

        var settings = AppSettings.Load();
        settings.SetDeviceThreshold(Id, newThreshold, _model.BluetoothAddress, Name);
    }

    partial void OnVolumePercentChanged(int value)
    {
        if (_isUpdatingVolumeInternally) return;
        if (HasAudioEndpoint && !string.IsNullOrEmpty(AudioEndpointId))
        {
            _audioManager?.SetVolume(AudioEndpointId, Math.Clamp(value, 0, 100) / 100.0f);
        }
    }

    [RelayCommand]
    public void ToggleMute()
    {
        if (HasAudioEndpoint && !string.IsNullOrEmpty(AudioEndpointId))
        {
            bool newMute = !IsMuted;
            IsMuted = newMute;
            _audioManager?.SetMute(AudioEndpointId, newMute);
        }
    }

    [RelayCommand]
    public void SetVolume(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
    }

    public void RefreshAudioVolume()
    {
        if (HasAudioEndpoint && !string.IsNullOrEmpty(AudioEndpointId) && _audioManager != null)
        {
            var volInfo = _audioManager.GetVolume(AudioEndpointId);
            if (volInfo.HasValue)
            {
                _isUpdatingVolumeInternally = true;
                try
                {
                    VolumePercent = (int)Math.Round(volInfo.Value.VolumePercent * 100);
                    IsMuted = volInfo.Value.IsMuted;
                }
                finally
                {
                    _isUpdatingVolumeInternally = false;
                }
            }
        }
    }

    public void UpdateFromModel(BluetoothDeviceModel model)
    {
        Id = model.Id;
        Name = model.Name;
        ProviderSource = model.ProviderSource;
        IsConnected = model.IsConnected;
        ConnectionStatusText = model.ConnectionStatusText;
        IsCharging = model.IsCharging;
        IconSymbol = GetSymbolForDeviceType(model.Type);

        // Audio endpoint matching (Quick Volume Control)
        bool isAudioDevice = model.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker;
        if (isAudioDevice && _audioManager != null && model.IsConnected)
        {
            var endpoint = _audioManager.FindEndpointForBluetoothDevice(model);
            if (endpoint != null)
            {
                HasAudioEndpoint = true;
                AudioEndpointId = endpoint.Id;
                _isUpdatingVolumeInternally = true;
                try
                {
                    VolumePercent = (int)Math.Round(endpoint.VolumePercent * 100);
                    IsMuted = endpoint.IsMuted;
                }
                finally
                {
                    _isUpdatingVolumeInternally = false;
                }
            }
            else
            {
                HasAudioEndpoint = false;
                AudioEndpointId = null;
            }
        }
        else
        {
            HasAudioEndpoint = false;
            AudioEndpointId = null;
        }

        // Audio Codec Detection
        if (isAudioDevice && model.IsConnected)
        {
            var ep = HasAudioEndpoint && _audioManager != null ? _audioManager.FindEndpointForBluetoothDevice(model) : null;
            if (ep != null)
            {
                string? detected = BluetoothAudioCodecDetector.Default.DetectCodec(model, ep);
                if (!string.IsNullOrEmpty(detected))
                {
                    AudioCodec = detected;
                    model.AudioCodec = detected;
                }
                else
                {
                    AudioCodec = model.AudioCodec;
                }
            }
            else if (string.IsNullOrWhiteSpace(model.AudioCodec))
            {
                AudioCodec = BluetoothAudioCodecDetector.Default.DetectCodec(model, null);
                model.AudioCodec = AudioCodec;
            }
            else
            {
                AudioCodec = model.AudioCodec;
            }
        }
        else
        {
            AudioCodec = model.AudioCodec;
        }

        // Device Specific Battery Alert Threshold
        var settings = AppSettings.Load();
        GlobalThreshold = settings.LowBatteryThreshold;
        if (settings.HasCustomThreshold(model.Id, model.BluetoothAddress))
        {
            CustomThreshold = settings.GetEffectiveLowBatteryThreshold(model.Id, model.BluetoothAddress);
        }
        else
        {
            CustomThreshold = null;
        }

        IsTws = model.IsTws;
        if (model.IsTws)
        {
            HasLeftBattery = model.LeftBatteryLevel.HasValue;
            LeftBatteryProgress = model.LeftBatteryLevel ?? 0;
            LeftBatteryText = model.LeftBatteryLevel.HasValue ? LocalizationService.FormatPercent(model.LeftBatteryLevel.Value) : "--";
            IsLeftCharging = model.IsLeftCharging;

            HasRightBattery = model.RightBatteryLevel.HasValue;
            RightBatteryProgress = model.RightBatteryLevel ?? 0;
            RightBatteryText = model.RightBatteryLevel.HasValue ? LocalizationService.FormatPercent(model.RightBatteryLevel.Value) : "--";
            IsRightCharging = model.IsRightCharging;

            HasCaseBattery = model.CaseBatteryLevel.HasValue;
            CaseBatteryProgress = model.CaseBatteryLevel ?? 0;
            CaseBatteryText = model.CaseBatteryLevel.HasValue ? LocalizationService.FormatPercent(model.CaseBatteryLevel.Value) : "--";
            IsCaseCharging = model.IsCaseCharging;
        }
        else
        {
            HasLeftBattery = false;
            HasRightBattery = false;
            HasCaseBattery = false;
        }

        int? effectiveBattery = model.EffectiveBatteryLevel;
        HasBattery = effectiveBattery.HasValue;
        if (effectiveBattery.HasValue)
        {
            BatteryProgress = Math.Clamp(effectiveBattery.Value, 0, 100);
            BatteryText = LocalizationService.FormatPercent(BatteryProgress);
            BatteryBrush = GetBrushForLevel(BatteryProgress, model.IsCharging);
        }
        else
        {
            BatteryProgress = 0;
            BatteryText = LocalizationService.GetString("Device_UnknownBattery");
            BatteryBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }
    }

    private static Brush GetBrushForLevel(int level, bool isCharging)
    {
        if (isCharging)
            return new SolidColorBrush(Color.FromRgb(0, 164, 239)); // Cyan/Blue charging color

        if (level >= 40)
            return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green (#10B981)

        if (level >= 20)
            return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber/Orange (#F59E0B)

        return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red (#EF4444)
    }

    private static SymbolRegular GetSymbolForDeviceType(DeviceType type)
    {
        return type switch
        {
            DeviceType.Headphones => SymbolRegular.Headphones24,
            DeviceType.Earbuds => SymbolRegular.HeadphonesSoundWave24,
            DeviceType.Speaker => SymbolRegular.Speaker224,
            DeviceType.Mouse => SymbolRegular.Cursor24,
            DeviceType.Keyboard => SymbolRegular.Keyboard24,
            DeviceType.Gamepad => SymbolRegular.Games24,
            DeviceType.Phone => SymbolRegular.Phone24,
            DeviceType.Watch => SymbolRegular.Clock24,
            DeviceType.Stylus => SymbolRegular.Pen24,
            _ => SymbolRegular.Bluetooth24
        };
    }
}
