using System.Collections.Generic;
using BluetoothBatteryMonitor.Models;
using BluetoothBatteryMonitor.Services;
using Xunit;

namespace BluetoothBatteryMonitor.Tests;

public class ToastNotificationServiceTests
{
    private class TestToastNotificationService : ToastNotificationService
    {
        public List<(string Name, int Level, bool IsCritical)> SentNotifications { get; } = new();

        public override bool SendNotification(string deviceName, int batteryLevel, bool isCritical)
        {
            SentNotifications.Add((deviceName, batteryLevel, isCritical));
            return true;
        }
    }

    [Fact]
    public void CheckAndNotify_WhenBatteryAbove20_DoesNotNotify()
    {
        var service = new TestToastNotificationService();
        var device = new BluetoothDeviceModel
        {
            Id = "DEV1",
            Name = "Xbox Controller",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 60 }
        };

        bool notified = service.CheckAndNotify(device);

        Assert.False(notified);
        Assert.Empty(service.SentNotifications);
    }

    [Fact]
    public void CheckAndNotify_WhenBatteryAt18_TriggersWarningNotification()
    {
        var service = new TestToastNotificationService();
        var device = new BluetoothDeviceModel
        {
            Id = "DEV1",
            Name = "Xbox Controller",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 18 }
        };

        bool notified = service.CheckAndNotify(device);

        Assert.True(notified);
        Assert.Single(service.SentNotifications);
        Assert.Equal("Xbox Controller", service.SentNotifications[0].Name);
        Assert.Equal(18, service.SentNotifications[0].Level);
        Assert.False(service.SentNotifications[0].IsCritical); // Tier 1: Warning
    }

    [Fact]
    public void CheckAndNotify_SubsequentNotificationWithinCooldown_IsSuppressed()
    {
        var service = new TestToastNotificationService();
        var device = new BluetoothDeviceModel
        {
            Id = "DEV1",
            Name = "Xbox Controller",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 18 }
        };

        service.CheckAndNotify(device);
        Assert.Single(service.SentNotifications);

        // Immediately checking again at the same level should be suppressed due to cooldown
        bool secondCheck = service.CheckAndNotify(device);
        Assert.False(secondCheck);
        Assert.Single(service.SentNotifications); // Count should not increase
    }

    [Fact]
    public void CheckAndNotify_WhenDroppingToCriticalTier10_TriggersEmergencyEvenDuringCooldown()
    {
        var service = new TestToastNotificationService();
        var device = new BluetoothDeviceModel
        {
            Id = "DEV1",
            Name = "Xbox Controller",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 18 }
        };

        // Tier 1 (18%)
        service.CheckAndNotify(device);
        Assert.Single(service.SentNotifications);

        // Tier 2 Emergency (8%): Emergency warning must be sent even if cooldown has not elapsed!
        device.Battery.Level = 8;
        bool secondCheck = service.CheckAndNotify(device);

        Assert.True(secondCheck);
        Assert.Equal(2, service.SentNotifications.Count);
        Assert.Equal(8, service.SentNotifications[1].Level);
        Assert.True(service.SentNotifications[1].IsCritical); // Tier 2: Critical Emergency
    }

    [Fact]
    public void CheckAndNotify_WhenDeviceIsCharging_DoesNotNotify()
    {
        var service = new TestToastNotificationService();
        var device = new BluetoothDeviceModel
        {
            Id = "DEV1",
            Name = "Logitech MX Master",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 10, IsCharging = true }
        };

        bool notified = service.CheckAndNotify(device);

        Assert.False(notified);
        Assert.Empty(service.SentNotifications);
    }

    [Fact]
    public void CheckAndNotify_MultipleIndependentDevices_TrackCooldownsIndependently()
    {
        var service = new TestToastNotificationService();

        var deviceA = new BluetoothDeviceModel
        {
            Id = "DEV_A",
            Name = "Kulaklık",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 15 }
        };

        var deviceB = new BluetoothDeviceModel
        {
            Id = "DEV_B",
            Name = "Fare",
            IsConnected = true,
            Battery = new BatteryInfo { Level = 14 }
        };

        service.CheckAndNotify(deviceA);
        service.CheckAndNotify(deviceB);

        Assert.Equal(2, service.SentNotifications.Count);
        Assert.Equal("Kulaklık", service.SentNotifications[0].Name);
        Assert.Equal("Fare", service.SentNotifications[1].Name);
    }
}
