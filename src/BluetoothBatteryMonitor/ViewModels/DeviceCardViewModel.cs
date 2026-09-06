using System;
using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothBatteryMonitor.Models;

namespace BluetoothBatteryMonitor.ViewModels;

/// <summary>
/// ViewModel managing the state and data of an individual device card in the flyout window.
/// </summary>
public partial class DeviceCardViewModel : ObservableObject
{
    private readonly BluetoothDeviceModel _model;

    public DeviceCardViewModel(BluetoothDeviceModel model)
    {
        _model = model;
        UpdateFromModel();
    }

    public BluetoothDeviceModel Model => _model;

    public string Id => _model.Id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _glyph = "\uE702";

    [ObservableProperty]
    private string _deviceTypeDisplayName = "Bluetooth Cihazı";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatusText = "Bağlı";

    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private int _batteryLevel;

    [ObservableProperty]
    private string _batteryLevelText = "Bilinmiyor";

    [ObservableProperty]
    private string _statusColor = "#22C55E";

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private bool _isTws;

    [ObservableProperty]
    private int? _leftLevel;

    [ObservableProperty]
    private string _leftLevelText = "-";

    [ObservableProperty]
    private bool _isLeftCharging;

    [ObservableProperty]
    private int? _rightLevel;

    [ObservableProperty]
    private string _rightLevelText = "-";

    [ObservableProperty]
    private bool _isRightCharging;

    [ObservableProperty]
    private int? _caseLevel;

    [ObservableProperty]
    private string _caseLevelText = "-";

    [ObservableProperty]
    private bool _isCaseCharging;

    [ObservableProperty]
    private string _providerSource = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    /// <summary>
    /// Updates ViewModel properties from the current data in the model.
    /// </summary>
    public void UpdateFromModel()
    {
        Name = _model.Name;
        Glyph = _model.Type.GetGlyph();
        DeviceTypeDisplayName = _model.Type.GetDisplayName();
        IsConnected = _model.IsConnected;
        ConnectionStatusText = _model.ConnectionStatusText;
        ProviderSource = _model.ProviderSource;

        var b = _model.Battery;
        int? effective = b.EffectiveLowestLevel;
        HasBattery = effective.HasValue;
        BatteryLevel = effective ?? 0;
        BatteryLevelText = effective.HasValue ? $"%{effective.Value}" : "Bilinmiyor";
        StatusColor = b.StatusColor;
        IsCharging = b.IsCharging;

        IsTws = b.HasMultipleBatteries;
        LeftLevel = b.LeftLevel;
        LeftLevelText = b.LeftLevel.HasValue ? $"%{b.LeftLevel.Value}" : "-";
        IsLeftCharging = b.IsLeftCharging;

        RightLevel = b.RightLevel;
        RightLevelText = b.RightLevel.HasValue ? $"%{b.RightLevel.Value}" : "-";
        IsRightCharging = b.IsRightCharging;

        CaseLevel = b.CaseLevel;
        CaseLevelText = b.CaseLevel.HasValue ? $"%{b.CaseLevel.Value}" : "-";
        IsCaseCharging = b.IsCaseCharging;

        LastUpdatedText = b.LastUpdated.ToString("HH:mm:ss");
    }
}
