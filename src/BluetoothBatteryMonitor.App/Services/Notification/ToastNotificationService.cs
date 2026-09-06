using System;
using System.Collections.Concurrent;
using BluetoothBatteryMonitor.App.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BluetoothBatteryMonitor.App.Services.Notification;

public class ToastNotificationService
{
    // DeviceId_Threshold => Last notification dispatch timestamp
    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTimes = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(30);

    public virtual void CheckAndNotifyBattery(BluetoothDeviceModel device, AppSettings settings)
    {
        if (!settings.EnableNotifications) return;
        if (!device.IsConnected || device.IsCharging) return;

        int? levelVal = device.EffectiveBatteryLevel ?? device.BatteryLevel;
        if (!levelVal.HasValue) return;

        int level = levelVal.Value;

        // Use device-specific threshold if set; otherwise fallback to global threshold
        int lowThreshold = settings.GetEffectiveLowBatteryThreshold(device.Id, device.BluetoothAddress);
        
        // If device's low battery threshold is less than or equal to global critical threshold (e.g. 10% for a mouse),
        // scale down the critical threshold proportionally so warning triggers prior to shutdown.
        int criticalThreshold = Math.Min(settings.CriticalBatteryThreshold, Math.Max(1, lowThreshold / 2));

        if (level <= criticalThreshold)
        {
            TrySendNotification(device, criticalThreshold, isCritical: true);
        }
        else if (level <= lowThreshold)
        {
            TrySendNotification(device, lowThreshold, isCritical: false);
        }
    }

    protected internal virtual bool TrySendNotification(BluetoothDeviceModel device, int threshold, bool isCritical)
    {
        string key = $"{device.Id}_{threshold}";
        var now = DateTime.UtcNow;

        if (_lastNotificationTimes.TryGetValue(key, out var lastTime))
        {
            if (now - lastTime < _cooldown)
            {
                // Cooldown period has not elapsed yet
                return false;
            }
        }

        _lastNotificationTimes[key] = now;
        return ShowToast(device, isCritical);
    }

    protected virtual bool ShowToast(BluetoothDeviceModel device, bool isCritical)
    {
        int? displayLevel = device.EffectiveBatteryLevel ?? device.BatteryLevel;

        string title = isCritical
            ? string.Format(Localization.LocalizationService.GetString("Toast_CriticalTitle"), device.Name)
            : string.Format(Localization.LocalizationService.GetString("Toast_LowTitle"), device.Name);

        string message = isCritical
            ? string.Format(Localization.LocalizationService.GetString("Toast_CriticalBody"), device.Name, displayLevel)
            : string.Format(Localization.LocalizationService.GetString("Toast_LowBody"), device.Name, displayLevel);

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText(Localization.LocalizationService.GetString("App_Title"))
                .Show();
            return true;
        }
        catch
        {
            // Suppress error if notification permissions are denied or shell service is unavailable
            return false;
        }
    }
}
