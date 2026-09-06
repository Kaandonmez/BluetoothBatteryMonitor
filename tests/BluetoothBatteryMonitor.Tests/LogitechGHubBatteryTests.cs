extern alias MonitorApp;

using System.Collections.Generic;
using Xunit;
using AppGHub = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.LogitechGHubBatteryProvider;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;

namespace BluetoothBatteryMonitor.Tests;

public class LogitechGHubBatteryTests
{
    [Fact]
    public void ParseDevicesList_ValidGProAndG502AndG915_ParsesDevicesAndBatteriesCorrectly()
    {
        string json = """
        {
            "path": "/devices/list",
            "payload": [
                {
                    "id": "dev_gpro",
                    "name": "Logitech PRO X SUPERLIGHT",
                    "model": "G Pro X Superlight",
                    "battery": {
                        "percentage": 85,
                        "charging": false,
                        "status": "discharging",
                        "millivolts": 4120
                    },
                    "connected": true
                },
                {
                    "id": "dev_g915",
                    "name": "Logitech G915 LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard",
                    "model": "G915",
                    "battery": {
                        "percentage": 92,
                        "charging": true,
                        "status": "charging",
                        "millivolts": 4210
                    },
                    "connected": true
                },
                {
                    "id": "dev_g733",
                    "name": "Logitech G733 LIGHTSPEED Wireless Gaming Headset",
                    "model": "G733",
                    "battery": {
                        "percentage": 45,
                        "charging": false,
                        "status": "discharging"
                    },
                    "connected": true
                }
            ]
        }
        """;

        var devices = AppGHub.ParseDevicesList(json);

        Assert.Equal(3, devices.Count);

        // G Pro X Superlight
        var mouse = devices[0];
        Assert.Equal("LogitechGHub_dev_gpro", mouse.Id);
        Assert.Equal("PRO X SUPERLIGHT", mouse.Name);
        Assert.Equal(85, mouse.BatteryLevel);
        Assert.False(mouse.IsCharging);
        Assert.True(mouse.IsConnected);
        Assert.Equal(AppDeviceType.Mouse, mouse.DeviceType);
        Assert.Equal("Logitech G HUB (LIGHTSPEED)", mouse.ProviderSource);

        // G915 Keyboard
        var kb = devices[1];
        Assert.Equal("LogitechGHub_dev_g915", kb.Id);
        Assert.Contains("G915", kb.Name);
        Assert.Equal(92, kb.BatteryLevel);
        Assert.True(kb.IsCharging);
        Assert.Equal(AppDeviceType.Keyboard, kb.DeviceType);

        // G733 Headset
        var headset = devices[2];
        Assert.Equal("LogitechGHub_dev_g733", headset.Id);
        Assert.Contains("G733", headset.Name);
        Assert.Equal(45, headset.BatteryLevel);
        Assert.False(headset.IsCharging);
        Assert.Equal(AppDeviceType.Headphones, headset.DeviceType);
    }

    [Fact]
    public void ParseDevicesList_AlternativeFormatWithItemsArray_ParsesSuccessfully()
    {
        string json = """
        {
            "verb": "GET",
            "path": "/devices/list",
            "result": {
                "items": [
                    {
                        "id": "0x408c",
                        "displayName": "G502 LIGHTSPEED",
                        "battery": {
                            "level": 74,
                            "isCharging": true
                        }
                    }
                ]
            }
        }
        """;

        var devices = AppGHub.ParseDevicesList(json);

        Assert.Single(devices);
        var dev = devices[0];
        Assert.Equal("LogitechGHub_0x408c", dev.Id);
        Assert.Equal("G502 LIGHTSPEED", dev.Name);
        Assert.Equal(74, dev.BatteryLevel);
        Assert.True(dev.IsCharging);
        Assert.Equal(AppDeviceType.Mouse, dev.DeviceType);
    }

    [Fact]
    public void ParseDevicesList_MalformedOrEmptyJson_ReturnsEmptyListWithoutCrashing()
    {
        Assert.Empty(AppGHub.ParseDevicesList(string.Empty));
        Assert.Empty(AppGHub.ParseDevicesList("not-json"));
        Assert.Empty(AppGHub.ParseDevicesList("{\"random\": 123}"));
    }

    [Fact]
    public void ParseBatteryStateChanged_ValidPayload_ParsesDeviceIdAndLevelAndCharging()
    {
        string json = """
        {
            "path": "/battery/state/changed",
            "payload": {
                "deviceId": "dev_gpro",
                "percentage": 88,
                "charging": true,
                "status": "charging"
            }
        }
        """;

        var result = AppGHub.ParseBatteryStateChanged(json);

        Assert.NotNull(result);
        Assert.Equal("dev_gpro", result.Value.DeviceId);
        Assert.Equal(88, result.Value.BatteryLevel);
        Assert.True(result.Value.IsCharging);
        Assert.Equal("charging", result.Value.Status);
    }

    [Fact]
    public void ParseBatteryStateChanged_MalformedJson_ReturnsNull()
    {
        Assert.Null(AppGHub.ParseBatteryStateChanged(""));
        Assert.Null(AppGHub.ParseBatteryStateChanged("not-json"));
        Assert.Null(AppGHub.ParseBatteryStateChanged("{}"));
    }

    [Theory]
    [InlineData("Logitech PRO X SUPERLIGHT", "G Pro X", AppDeviceType.Mouse)]
    [InlineData("Logitech G502 LIGHTSPEED", "G502", AppDeviceType.Mouse)]
    [InlineData("Logitech G915 LIGHTSPEED", "G915", AppDeviceType.Keyboard)]
    [InlineData("Logitech G733 LIGHTSPEED", "G733", AppDeviceType.Headphones)]
    [InlineData("PRO X Wireless Gaming Headset", "Pro X", AppDeviceType.Headphones)]
    [InlineData("Logitech Unknown Widget", "Unknown", AppDeviceType.Generic)]
    public void DetectLogitechGType_IdentifiesMiceKeyboardsHeadsetsCorrectly(string name, string model, AppDeviceType expected)
    {
        var actual = AppGHub.DetectLogitechGType(name, model);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CleanGHubName_StripsLogitechPrefixCleanly()
    {
        Assert.Equal("G502 LIGHTSPEED", AppGHub.CleanGHubName("Logitech G502 LIGHTSPEED"));
        Assert.Equal("PRO X SUPERLIGHT", AppGHub.CleanGHubName("Logitech G HUB PRO X SUPERLIGHT"));
    }

    [Fact]
    public void ProcessMessage_InitialDeviceListThenBatteryChanged_UpdatesDeviceAndFiresEvent()
    {
        var provider = new AppGHub();
        MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel? updatedDevice = null;
        provider.DeviceUpdated += (s, dev) => updatedDevice = dev;

        // 1. Initial devices list message
        string listJson = """
        {
            "path": "/devices/list",
            "payload": [
                {
                    "id": "dev_gpro",
                    "name": "Logitech PRO X SUPERLIGHT",
                    "battery": { "percentage": 80, "charging": false }
                }
            ]
        }
        """;
        provider.ProcessMessage(listJson);

        Assert.NotNull(updatedDevice);
        Assert.Equal("LogitechGHub_dev_gpro", updatedDevice.Id);
        Assert.Equal(80, updatedDevice.BatteryLevel);
        Assert.False(updatedDevice.IsCharging);

        // 2. Battery state changed event with raw deviceId "dev_gpro"
        string changeJson = """
        {
            "path": "/battery/state/changed",
            "payload": {
                "deviceId": "dev_gpro",
                "percentage": 95,
                "charging": true
            }
        }
        """;
        updatedDevice = null;
        provider.ProcessMessage(changeJson);

        Assert.NotNull(updatedDevice);
        Assert.Equal("LogitechGHub_dev_gpro", updatedDevice.Id);
        Assert.Equal(95, updatedDevice.BatteryLevel);
        Assert.True(updatedDevice.IsCharging);
    }
}
