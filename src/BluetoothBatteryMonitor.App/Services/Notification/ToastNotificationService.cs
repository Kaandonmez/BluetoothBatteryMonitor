using System;
using System.Collections.Concurrent;
using BluetoothBatteryMonitor.App.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BluetoothBatteryMonitor.App.Services.Notification;

public class ToastNotificationService
{
    // CihazId_Esik => Son bildirim gönderilme zamanı
    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTimes = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(30);

    public virtual void CheckAndNotifyBattery(BluetoothDeviceModel device, AppSettings settings)
    {
        if (!settings.EnableNotifications) return;
        if (!device.IsConnected || device.IsCharging) return;

        int? levelVal = device.EffectiveBatteryLevel ?? device.BatteryLevel;
        if (!levelVal.HasValue) return;

        int level = levelVal.Value;

        // Cihaza özel eşik tanımlıysa onu kullan, değilse global eşiği kullan
        int lowThreshold = settings.GetEffectiveLowBatteryThreshold(device.Id, device.BluetoothAddress);
        
        // Eğer cihazın düşük pil eşiği global kritik eşikten küçük veya eşitse (örn. fare için %10),
        // kritik eşik düşük eşikten daha küçük tutulmalı (örn. %5) ki %10'da önce düşük pil uyarısı gitsin.
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
                // Cooldown henüz dolmadı
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
            ? $"⚠️ Kritik Pil: {device.Name}"
            : $"🔋 Düşük Pil: {device.Name}";

        string message = isCritical
            ? $"{device.Name} pili kritik seviyede (%{displayLevel}). Lütfen hemen şarj edin!"
            : $"{device.Name} pili azaldı (%{displayLevel}). Yakında şarja takmanız gerekebilir.";

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText("Bluetooth Pil Monitörü")
                .Show();
            return true;
        }
        catch
        {
            // Bildirim izinleri kapalıysa veya bildirim servisi kullanılamıyorsa
            return false;
        }
    }
}
