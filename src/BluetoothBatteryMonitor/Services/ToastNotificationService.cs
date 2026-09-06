using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using BluetoothBatteryMonitor.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Smart service that generates Windows Toast notifications for critical battery levels (20% and 10%).
/// Includes a 30-minute cooldown per device and tier mechanism to prevent spam.
/// </summary>
public class ToastNotificationService
{
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(30);

    // Device ID -> (Last notification timestamp, Last notified tier)
    // Tier 1: 20% and below (Warning)
    // Tier 2: 10% and below (Critical Emergency)
    private readonly ConcurrentDictionary<string, (DateTime Timestamp, int Tier)> _notificationHistory = new();

    /// <summary>
    /// Cooldown duration for tests and settings.
    /// </summary>
    public TimeSpan Cooldown => _cooldown;

    /// <summary>
    /// Checks device battery status and sends a Toast notification if needed.
    /// </summary>
    /// <returns>True if notification was shown; false if suppressed due to cooldown/level.</returns>
    public bool CheckAndNotify(BluetoothDeviceModel device)
    {
        if (device == null || !device.IsConnected)
        {
            return false;
        }

        var level = device.Battery.EffectiveLowestLevel;
        if (!level.HasValue)
        {
            return false;
        }

        int currentLevel = level.Value;

        string trackingKey = device.BluetoothAddress != 0 ? device.BluetoothAddress.ToString("X12") : device.Id;

        // No warning if battery is charging or above 20%
        if (device.Battery.IsCharging || currentLevel > 20)
        {
            // If device is charged or battery level increased, reset history to prepare for next drop
            if (currentLevel > 25 && _notificationHistory.ContainsKey(trackingKey))
            {
                _notificationHistory.TryRemove(trackingKey, out _);
            }
            return false;
        }

        // Determine tier: 1 = 11%-20% (Low Battery), 2 = 10% and below (Critical Battery)
        int currentTier = currentLevel <= 10 ? 2 : 1;
        var now = DateTime.Now;

        if (_notificationHistory.TryGetValue(trackingKey, out var record))
        {
            bool isNewEmergencyTier = currentTier > record.Tier;
            bool isCooldownElapsed = (now - record.Timestamp) >= _cooldown;

            // Suppress notification if cooldown has not elapsed and not moving to a more urgent tier
            if (!isCooldownElapsed && !isNewEmergencyTier)
            {
                return false;
            }
        }

        // Update notification history
        _notificationHistory[trackingKey] = (now, currentTier);

        // Send notification
        string displayName = !string.IsNullOrWhiteSpace(device.Name) ? device.Name : "Bluetooth Cihazı";
        return SendNotification(displayName, currentLevel, currentTier == 2);
    }

    /// <summary>
    /// Displays the Windows Toast Notification on screen.
    /// </summary>
    public virtual bool SendNotification(string deviceName, int batteryLevel, bool isCritical)
    {
        try
        {
            string title = isCritical
                ? $"⚠️ {deviceName} Pili Çok Düşük (%{batteryLevel})!"
                : $"🔋 {deviceName} Pil Uyarısı (%{batteryLevel})";

            string message = isCritical
                ? $"{deviceName} pili tükenmek üzere (%{batteryLevel}). Kesintisiz kullanım için lütfen hemen şarja takın."
                : $"{deviceName} pili kritik seviyede (%{batteryLevel}). Lütfen en kısa sürede şarj edin.";

            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText("Bluetooth Pil Monitörü")
                .Show();

            Debug.WriteLine($"[ToastNotificationService] Bildirim gönderildi: {deviceName} (%{batteryLevel})");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ToastNotificationService] Toast bildirimi gösterilemedi: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clears device notification history.
    /// </summary>
    public void ResetHistory(string? deviceId = null)
    {
        if (deviceId == null)
        {
            _notificationHistory.Clear();
        }
        else
        {
            _notificationHistory.TryRemove(deviceId, out _);
        }
    }
}
