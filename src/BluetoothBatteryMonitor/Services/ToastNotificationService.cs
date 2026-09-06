using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using BluetoothBatteryMonitor.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BluetoothBatteryMonitor.Services;

/// <summary>
/// Kritik pil seviyeleri (%20 ve %10) için Windows Toast bildirimleri üreten akıllı servis.
/// Spam'i önlemek için cihaz başına 30 dakikalık cooldown ve kademe mekanizması içerir.
/// </summary>
public class ToastNotificationService
{
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(30);

    // Cihaz kimliği -> (Son bildirim zamanı, Bildirilen son kademe)
    // Kademe 1: %20 ve altı (Uyarı)
    // Kademe 2: %10 ve altı (Kritik Acil)
    private readonly ConcurrentDictionary<string, (DateTime Timestamp, int Tier)> _notificationHistory = new();

    /// <summary>
    /// Testler ve ayarlar için cooldown süresi.
    /// </summary>
    public TimeSpan Cooldown => _cooldown;

    /// <summary>
    /// Cihaz pil durumunu denetler ve gerekiyorsa Toast bildirimi gönderir.
    /// </summary>
    /// <returns>Bildirim gösterildiyse true, cooldown/seviye nedeniyle gösterilmediyse false döner.</returns>
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

        // Pil şarjdaysa veya %20'nin üzerindeyse uyarı verilmez
        if (device.Battery.IsCharging || currentLevel > 20)
        {
            // Cihaz şarj edildiyse veya pil yükseldiyse geçmişi sıfırlayarak bir sonraki düşüşe hazır hale getir
            if (currentLevel > 25 && _notificationHistory.ContainsKey(trackingKey))
            {
                _notificationHistory.TryRemove(trackingKey, out _);
            }
            return false;
        }

        // Kademe belirleme: 1 = %11-%20 arası (Düşük Pil), 2 = %10 ve altı (Kritik Pil)
        int currentTier = currentLevel <= 10 ? 2 : 1;
        var now = DateTime.Now;

        if (_notificationHistory.TryGetValue(trackingKey, out var record))
        {
            bool isNewEmergencyTier = currentTier > record.Tier;
            bool isCooldownElapsed = (now - record.Timestamp) >= _cooldown;

            // Cooldown dolmadıysa ve daha acil bir kademeye geçilmediyse bildirimi yut
            if (!isCooldownElapsed && !isNewEmergencyTier)
            {
                return false;
            }
        }

        // Bildirim kaydını güncelle
        _notificationHistory[trackingKey] = (now, currentTier);

        // Bildirimi gönder
        string displayName = !string.IsNullOrWhiteSpace(device.Name) ? device.Name : "Bluetooth Cihazı";
        return SendNotification(displayName, currentLevel, currentTier == 2);
    }

    /// <summary>
    /// Windows Toast Bildirimini ekrana basar.
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
    /// Cihaz bildirim geçmişini temizler.
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
