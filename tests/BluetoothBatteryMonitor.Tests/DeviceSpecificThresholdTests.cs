extern alias MonitorApp;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppSettings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using AppToast = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;
using AppDeviceItemViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.DeviceItemViewModel;
using AppSettingsViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.SettingsViewModel;

namespace BluetoothBatteryMonitor.Tests;

public class DeviceSpecificThresholdTests
{
    private class TestToastNotificationService : AppToast
    {
        public List<(string Name, int Level, bool IsCritical)> SentNotifications { get; } = new();

        protected internal override bool TrySendNotification(AppDevice device, int threshold, bool isCritical)
        {
            int? level = device.EffectiveBatteryLevel ?? device.BatteryLevel;
            SentNotifications.Add((device.Name, level ?? threshold, isCritical));
            return true;
        }
    }

    [Fact]
    public void AppSettings_GetEffectiveLowBatteryThreshold_UsesCustomWhenPresent()
    {
        var settings = new AppSettings
        {
            LowBatteryThreshold = 20
        };
        settings.DeviceSpecificThresholds["DEV_MOUSE"] = 10;
        settings.DeviceSpecificThresholds["001122334455"] = 15;

        // Tanımlı cihaz
        Assert.Equal(10, settings.GetEffectiveLowBatteryThreshold("DEV_MOUSE"));

        // Tanımlı MAC
        Assert.Equal(15, settings.GetEffectiveLowBatteryThreshold("ANY_ID", 0x001122334455));

        // Tanımsız cihaz -> Global eşik (20)
        Assert.Equal(20, settings.GetEffectiveLowBatteryThreshold("DEV_UNKNOWN"));
    }

    [Fact]
    public void AppSettings_SetDeviceThreshold_AddsAndRemovesCorrectly()
    {
        var settings = new AppSettings();
        string devId = "DEV_TEST_SET";

        settings.SetDeviceThreshold(devId, 25);
        Assert.True(settings.HasCustomThreshold(devId));
        Assert.Equal(25, settings.GetEffectiveLowBatteryThreshold(devId));

        // 0 veya null verildiğinde kaldırılmalı
        settings.SetDeviceThreshold(devId, 0);
        Assert.False(settings.HasCustomThreshold(devId));
        Assert.Equal(settings.LowBatteryThreshold, settings.GetEffectiveLowBatteryThreshold(devId));
    }

    [Fact]
    public void ToastNotificationService_WhenDeviceHasCustomThreshold10_DoesNotNotifyAt15()
    {
        var service = new TestToastNotificationService();
        var settings = new AppSettings
        {
            EnableNotifications = true,
            LowBatteryThreshold = 20 // Global eşik %20
        };
        // Fare için özel eşik: %10
        settings.DeviceSpecificThresholds["DEV_MOUSE"] = 10;

        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE",
            Name = "Logitech MX Master 3",
            Type = AppDeviceType.Mouse,
            BatteryLevel = 15, // Global eşiğin altında ama fare eşiğinin (%10) üstünde
            IsConnected = true
        };

        service.CheckAndNotifyBattery(mouse, settings);

        // Bildirim tetiklenmemeli
        Assert.Empty(service.SentNotifications);
    }

    [Fact]
    public void ToastNotificationService_WhenDeviceHasCustomThreshold10_NotifiesAt10()
    {
        var service = new TestToastNotificationService();
        var settings = new AppSettings
        {
            EnableNotifications = true,
            LowBatteryThreshold = 20
            // CriticalBatteryThreshold varsayılan 10 olarak kalmalıdır
        };
        settings.DeviceSpecificThresholds["DEV_MOUSE"] = 10;

        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE",
            Name = "Logitech MX Master 3",
            Type = AppDeviceType.Mouse,
            BatteryLevel = 10,
            IsConnected = true
        };

        service.CheckAndNotifyBattery(mouse, settings);

        Assert.Single(service.SentNotifications);
        Assert.Equal("Logitech MX Master 3", service.SentNotifications[0].Name);
        Assert.Equal(10, service.SentNotifications[0].Level);
        Assert.False(service.SentNotifications[0].IsCritical);
    }

    [Fact]
    public void ToastNotificationService_WhenDeviceHasHigherCustomThreshold25_NotifiesAt22()
    {
        var service = new TestToastNotificationService();
        var settings = new AppSettings
        {
            EnableNotifications = true,
            LowBatteryThreshold = 20 // Global %20
        };
        // Kulaklık için özel eşik: %25
        settings.DeviceSpecificThresholds["DEV_HEADPHONES"] = 25;

        var headphones = new AppDevice
        {
            Id = "DEV_HEADPHONES",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            BatteryLevel = 22, // Global %20'nin üstünde ama özel %25 eşiğinin altında!
            IsConnected = true
        };

        service.CheckAndNotifyBattery(headphones, settings);

        Assert.Single(service.SentNotifications);
        Assert.Equal("Sony WH-1000XM4", service.SentNotifications[0].Name);
        Assert.Equal(22, service.SentNotifications[0].Level);
    }

    [Fact]
    public void ToastNotificationService_WhenCharging_DoesNotNotifyEvenIfBatteryLow()
    {
        var service = new TestToastNotificationService();
        var settings = new AppSettings
        {
            EnableNotifications = true,
            LowBatteryThreshold = 20
        };

        var device = new AppDevice
        {
            Id = "DEV_CHARGING",
            Name = "Kulaklık",
            BatteryLevel = 5,
            IsCharging = true,
            IsConnected = true
        };

        service.CheckAndNotifyBattery(device, settings);
        Assert.Empty(service.SentNotifications);
    }

    [Fact]
    public void DeviceItemViewModel_SetThresholdCommand_UpdatesPropertiesAndSettings()
    {
        var device = new AppDevice
        {
            Id = "DEV_VM_TEST",
            Name = "Bluetooth Headset",
            Type = AppDeviceType.Headphones,
            BatteryLevel = 50,
            IsConnected = true,
            AudioCodec = "LDAC"
        };

        var vm = new AppDeviceItemViewModel(device, null);

        Assert.Equal("LDAC", vm.AudioCodec);
        Assert.True(vm.HasAudioCodec);

        // Varsayılan durumda özel eşik yok
        Assert.False(vm.HasCustomThreshold);

        // %15 olarak ayarla
        vm.SetThresholdCommand.Execute(15);

        Assert.True(vm.HasCustomThreshold);
        Assert.Equal(15, vm.CustomThreshold);
        Assert.Equal(15, vm.EffectiveThreshold);
        Assert.Contains("15%", vm.ThresholdDisplayText);
        Assert.Contains("15%", vm.CustomThresholdBadgeText);

        // Varsayılana geri al (0 veya null)
        vm.SetThresholdCommand.Execute(0);
        Assert.False(vm.HasCustomThreshold);
        Assert.Null(vm.CustomThreshold);
        Assert.Equal(vm.GlobalThreshold, vm.EffectiveThreshold);
    }

    [Fact]
    public void ToastNotificationService_WhenDeviceHasCustomThreshold10_NotifiesCriticalAt5()
    {
        var service = new TestToastNotificationService();
        var settings = new AppSettings
        {
            EnableNotifications = true,
            LowBatteryThreshold = 20,
            CriticalBatteryThreshold = 10
        };
        settings.DeviceSpecificThresholds["DEV_MOUSE"] = 10;

        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE",
            Name = "Logitech MX Master 3",
            Type = AppDeviceType.Mouse,
            BatteryLevel = 5, // %5 seviyesi kritik olmalı
            IsConnected = true
        };

        service.CheckAndNotifyBattery(mouse, settings);

        Assert.Single(service.SentNotifications);
        Assert.Equal("Logitech MX Master 3", service.SentNotifications[0].Name);
        Assert.Equal(5, service.SentNotifications[0].Level);
        Assert.True(service.SentNotifications[0].IsCritical);
    }

    [Fact]
    public void DeviceItemViewModel_ThresholdCheckmarkProperties_ReflectActiveSelection()
    {
        var device = new AppDevice
        {
            Id = "DEV_VM_CHECKMARKS",
            Name = "Headset",
            Type = AppDeviceType.Headphones,
            BatteryLevel = 80,
            IsConnected = true
        };

        var vm = new AppDeviceItemViewModel(device, null);

        // Başlangıç: varsayılan seçili
        Assert.True(vm.IsThresholdDefault);
        Assert.False(vm.IsThreshold10);
        Assert.False(vm.IsThreshold25);

        // %10 seç
        vm.SetThresholdCommand.Execute(10);
        Assert.False(vm.IsThresholdDefault);
        Assert.True(vm.IsThreshold10);
        Assert.False(vm.IsThreshold25);

        // %25 seç
        vm.SetThresholdCommand.Execute(25);
        Assert.False(vm.IsThresholdDefault);
        Assert.False(vm.IsThreshold10);
        Assert.True(vm.IsThreshold25);

        // Varsayılana dön
        vm.SetThresholdCommand.Execute(0);
        Assert.True(vm.IsThresholdDefault);
        Assert.False(vm.IsThreshold10);
        Assert.False(vm.IsThreshold25);
    }

    [Fact]
    public void SettingsViewModel_WithKnownDevices_PopulatesDevicesWithFriendlyNames()
    {
        var devices = new List<AppDevice>
        {
            new AppDevice
            {
                Id = "BTHENUM\\DEV_1",
                Name = "Logitech MX Master 3",
                Type = AppDeviceType.Mouse,
                BluetoothAddress = 0xAABBCCDDEEFF
            },
            new AppDevice
            {
                Id = "BTHENUM\\DEV_2",
                Name = "Sony WH-1000XM4",
                Type = AppDeviceType.Headphones,
                BluetoothAddress = 0x112233445566
            }
        };

        var vm = new AppSettingsViewModel(devices);

        Assert.True(vm.HasDeviceThresholds);
        Assert.Equal(2, vm.DeviceThresholds.Count);

        var mouseItem = vm.DeviceThresholds.FirstOrDefault(d => d.Name == "Logitech MX Master 3");
        Assert.NotNull(mouseItem);
        Assert.Equal("BTHENUM\\DEV_1", mouseItem.Id);
        Assert.Equal(0, mouseItem.SelectedThreshold); // Varsayılan

        var sonyItem = vm.DeviceThresholds.FirstOrDefault(d => d.Name == "Sony WH-1000XM4");
        Assert.NotNull(sonyItem);
        Assert.Equal("BTHENUM\\DEV_2", sonyItem.Id);
    }

    [Fact]
    public void SettingsViewModel_StaticOptions_AreConfiguredCorrectly()
    {
        Assert.NotEmpty(AppSettingsViewModel.AvailableThresholds);
        Assert.Contains(AppSettingsViewModel.AvailableThresholds, t => t.Value == 0);
        Assert.Contains(AppSettingsViewModel.AvailableThresholds, t => t.Value == 10);
        Assert.Contains(AppSettingsViewModel.AvailableThresholds, t => t.Value == 20);
        Assert.Contains(AppSettingsViewModel.AvailableThresholds, t => t.Value == 25);

        Assert.NotEmpty(AppSettingsViewModel.AvailableRefreshIntervals);
        Assert.Contains(AppSettingsViewModel.AvailableRefreshIntervals, r => r.Seconds == 30);
        Assert.Contains(AppSettingsViewModel.AvailableRefreshIntervals, r => r.Seconds == 60);
    }
}
