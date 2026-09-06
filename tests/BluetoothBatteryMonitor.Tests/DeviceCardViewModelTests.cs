using BluetoothBatteryMonitor.Models;
using BluetoothBatteryMonitor.ViewModels;
using Xunit;

namespace BluetoothBatteryMonitor.Tests;

public class DeviceCardViewModelTests
{
    [Fact]
    public void DeviceCardViewModel_InitializesCorrectly_FromBluetoothDeviceModel()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "DEV_TEST",
            Name = "AirPods Pro",
            Type = DeviceType.Earbuds,
            IsConnected = true,
            ProviderSource = "Apple AirPods Beacon",
            Battery = new BatteryInfo
            {
                HasMultipleBatteries = true,
                LeftLevel = 80,
                RightLevel = 90,
                CaseLevel = 100,
                Level = 80
            }
        };

        var vm = new DeviceCardViewModel(model);

        Assert.Equal("AirPods Pro", vm.Name);
        Assert.Equal("TWS Kulaklık", vm.DeviceTypeDisplayName);
        Assert.True(vm.IsConnected);
        Assert.Equal("Bağlı", vm.ConnectionStatusText);
        Assert.True(vm.IsTws);
        Assert.Equal("%80", vm.LeftLevelText);
        Assert.Equal("%90", vm.RightLevelText);
        Assert.Equal("%100", vm.CaseLevelText);
        Assert.Equal(80, vm.BatteryLevel);
        Assert.Equal("#22C55E", vm.StatusColor);
    }

    [Fact]
    public void DeviceCardViewModel_WhenDisconnected_ShowsDisconnectedStatusText()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "DEV_DISCONNECTED",
            Name = "Xbox Wireless Controller",
            Type = DeviceType.Gamepad,
            IsConnected = false,
            Battery = new BatteryInfo { Level = 60 }
        };

        var vm = new DeviceCardViewModel(model);

        Assert.False(vm.IsConnected);
        Assert.Equal("Bağlı Değil (Son Görülen Pil)", vm.ConnectionStatusText);
        Assert.Equal("Oyun Kolu", vm.DeviceTypeDisplayName);
    }

    [Fact]
    public void DeviceCardViewModel_WithZeroPercentBattery_InitializesHasBatteryTrueAndLevelZero()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "DEV_ZERO",
            Name = "Critical Battery Mouse",
            Type = DeviceType.Mouse,
            IsConnected = true,
            Battery = new BatteryInfo { Level = 0 }
        };

        var vm = new DeviceCardViewModel(model);

        Assert.True(vm.HasBattery);
        Assert.Equal(0, vm.BatteryLevel);
        Assert.Equal("%0", vm.BatteryLevelText);
        Assert.Equal("#EF4444", vm.StatusColor);
    }

    [Fact]
    public void BluetoothDeviceModel_Matches_UnifiesAcrossDifferentProviderIdFormatsByMac()
    {
        var pnpModel = new BluetoothDeviceModel
        {
            Id = @"BTHENUM\Dev_001BDC073185\8&1a84f37&0&BLUETOOTHDEVICE_001BDC073185",
            Name = "AirPods Pro",
            Type = DeviceType.Unknown
        };

        var beaconModel = new BluetoothDeviceModel
        {
            Id = "AirPods_001BDC073185",
            Name = "AirPods Pro (2. Nesil)",
            Type = DeviceType.Earbuds
        };

        Assert.True(pnpModel.Matches(beaconModel));
        Assert.True(beaconModel.Matches(pnpModel));
    }

    [Fact]
    public void BluetoothDeviceModel_Matches_DoesNotMatchDistinctDevicesWithGenericPlaceholderNames()
    {
        var dev1 = new BluetoothDeviceModel
        {
            Id = "DEV_AAA",
            Name = "Bluetooth Aygıtı",
            Type = DeviceType.Unknown
        };

        var dev2 = new BluetoothDeviceModel
        {
            Id = "DEV_BBB",
            Name = "Bluetooth Aygıtı",
            Type = DeviceType.Unknown
        };

        Assert.False(dev1.Matches(dev2));
    }
}
