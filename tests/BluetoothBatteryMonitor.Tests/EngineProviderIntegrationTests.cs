extern alias MonitorApp;

using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AppEngine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;
using AppToast = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;

namespace BluetoothBatteryMonitor.Tests;

public class EngineProviderIntegrationTests
{
    [Fact]
    public void BluetoothBatteryEngine_HasAllProvidersRegistered()
    {
        var toast = new AppToast();
        using var engine = new AppEngine(toast);

        var providers = engine.Providers;

        Assert.Equal(11, providers.Count);

        var names = providers.Select(p => p.Name).ToList();

        Assert.Contains(names, n => n.Contains("GATT"));
        Assert.Contains(names, n => n.Contains("AirPods"));
        Assert.Contains(names, n => n.Contains("PnP"));
        Assert.Contains(names, n => n.Contains("HID++"));
        Assert.Contains(names, n => n.Contains("G HUB"));
        Assert.Contains(names, n => n.Contains("PlayStation"));
        Assert.Contains(names, n => n.Contains("Galaxy Buds"));
        Assert.Contains(names, n => n.Contains("Sony"));
        Assert.Contains(names, n => n.Contains("Google Fast Pair"));
        Assert.Contains(names, n => n.Contains("Nintendo Switch"));
        Assert.Contains(names, n => n.Contains("SteelSeries"));
    }

    [Fact]
    public async Task BluetoothBatteryEngine_RefreshAllDevicesAsync_ExecutesWithoutException()
    {
        var toast = new AppToast();
        using var engine = new AppEngine(toast);

        // In a live environment, running all providers in parallel should not throw any exception
        var exception = await Record.ExceptionAsync(async () =>
        {
            await engine.RefreshAllDevicesAsync();
        });

        Assert.Null(exception);
    }
}
