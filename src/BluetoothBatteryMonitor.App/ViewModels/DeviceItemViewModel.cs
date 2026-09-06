using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothBatteryMonitor.App.Models;
using Wpf.Ui.Controls;

using BluetoothBatteryMonitor.App.Services.Audio;
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
    private string _connectionStatusText = "Bağlı Değil";

    [ObservableProperty]
    private string _providerSource = string.Empty;

    // TWS Alanları
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

    // Ses Kontrol Alanları (Quick Volume Control)
    [ObservableProperty]
    private bool _hasAudioEndpoint;

    [ObservableProperty]
    private string? _audioEndpointId;

    [ObservableProperty]
    private int _volumePercent = 100;

    [ObservableProperty]
    private bool _isMuted;

    // Ses Kodek Bilgisi (SBC, AAC, aptX, aptX HD, LDAC)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudioCodec))]
    [NotifyPropertyChangedFor(nameof(AudioCodecToolTip))]
    private string? _audioCodec;

    public bool HasAudioCodec => !string.IsNullOrWhiteSpace(AudioCodec);

    public string AudioCodecToolTip => AudioCodec switch
    {
        "LDAC" => "Sony LDAC (Yüksek Çözünürlüklü Ses • 990 kbps / 96 kHz)",
        "aptX HD" => "Qualcomm aptX HD (Yüksek Çözünürlüklü Ses • 576 kbps / 48 kHz)",
        "aptX" => "Qualcomm aptX (Düşük Gecikmeli Yüksek Kalite • 352 kbps)",
        "AAC" => "Advanced Audio Coding (Windows 11 / AAC HD Ses • 256 kbps)",
        "SBC" => "Subband Codec (Standart Bluetooth Ses)",
        _ => $"Aktif Ses Kodeki: {AudioCodec}"
    };

    // Cihaza Özel Pil Bildirim Eşiği
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
        ? $" • Eşik: %{CustomThreshold}"
        : string.Empty;

    public string ThresholdDisplayText => HasCustomThreshold
        ? $"Özel Eşik: %{CustomThreshold}"
        : $"Varsayılan (%{GlobalThreshold})";

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

        // Ses aygıtı eşleşmesi (Quick Volume Control)
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

        // Ses Kodek Tespiti
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

        // Cihaza Özel Pil Bildirim Eşiği
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
            LeftBatteryText = model.LeftBatteryLevel.HasValue ? $"%{model.LeftBatteryLevel}" : "--";
            IsLeftCharging = model.IsLeftCharging;

            HasRightBattery = model.RightBatteryLevel.HasValue;
            RightBatteryProgress = model.RightBatteryLevel ?? 0;
            RightBatteryText = model.RightBatteryLevel.HasValue ? $"%{model.RightBatteryLevel}" : "--";
            IsRightCharging = model.IsRightCharging;

            HasCaseBattery = model.CaseBatteryLevel.HasValue;
            CaseBatteryProgress = model.CaseBatteryLevel ?? 0;
            CaseBatteryText = model.CaseBatteryLevel.HasValue ? $"%{model.CaseBatteryLevel}" : "--";
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
            BatteryText = $"%{BatteryProgress}";
            BatteryBrush = GetBrushForLevel(BatteryProgress, model.IsCharging);
        }
        else
        {
            BatteryProgress = 0;
            BatteryText = "Bilinmiyor";
            BatteryBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }
    }

    private static Brush GetBrushForLevel(int level, bool isCharging)
    {
        if (isCharging)
            return new SolidColorBrush(Color.FromRgb(0, 164, 239)); // Mavi şarj rengi

        if (level >= 40)
            return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Yeşil (#10B981)

        if (level >= 20)
            return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Sarı/Turuncu (#F59E0B)

        return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Kırmızı (#EF4444)
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
