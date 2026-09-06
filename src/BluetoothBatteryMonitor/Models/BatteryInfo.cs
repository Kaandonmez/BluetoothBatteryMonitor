using System;
using System.Collections.Generic;
using System.Linq;

namespace BluetoothBatteryMonitor.Models;

/// <summary>
/// Bluetooth cihazına ait pil verileri ve durum bilgisi.
/// Hem standart tekil pilleri hem de AirPods gibi çoklu (Sol, Sağ, Kutu) pilleri destekler.
/// </summary>
public class BatteryInfo
{
    /// <summary>
    /// Genel veya tekil pil yüzdesi (0-100).
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Cihaz genelinde şarj edilip edilmediği.
    /// </summary>
    public bool IsCharging { get; set; }

    /// <summary>
    /// Çoklu pil (AirPods Sol/Sağ/Kutu gibi) desteği olup olmadığı.
    /// </summary>
    public bool HasMultipleBatteries { get; set; }

    /// <summary>
    /// TWS Sol kulaklık pili (0-100).
    /// </summary>
    public int? LeftLevel { get; set; }

    /// <summary>
    /// Sol kulaklığın şarj olup olmadığı.
    /// </summary>
    public bool IsLeftCharging { get; set; }

    /// <summary>
    /// TWS Sağ kulaklık pili (0-100).
    /// </summary>
    public int? RightLevel { get; set; }

    /// <summary>
    /// Sağ kulaklığın şarj olup olmadığı.
    /// </summary>
    public bool IsRightCharging { get; set; }

    /// <summary>
    /// TWS Şarj Kutusu pili (0-100).
    /// </summary>
    public int? CaseLevel { get; set; }

    /// <summary>
    /// Şarj kutusunun şarj olup olmadığı.
    /// </summary>
    public bool IsCaseCharging { get; set; }

    /// <summary>
    /// Pil bilgisinin en son güncellendiği zaman damgası.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// Geçerli bir pil seviyesi okunup okunmadığı (0 dahil).
    /// </summary>
    public bool HasBattery => EffectiveLowestLevel.HasValue;

    /// <summary>
    /// Mevcut tüm pil bileşenleri (Sol, Sağ, Kutu veya Genel) arasındaki en düşük aktif pil yüzdesini hesaplar.
    /// TWS kulaklıklarda aktif kulaklık/kutu pillerinin minimumunu alır; Level değeri eski kalsa dahi zehirleme yapmaz.
    /// Dynamic tray icon ve kritik uyarılar bu değeri baz alır.
    /// </summary>
    public int? EffectiveLowestLevel
    {
        get
        {
            if (HasMultipleBatteries || LeftLevel.HasValue || RightLevel.HasValue || CaseLevel.HasValue)
            {
                var twsLevels = new List<int>();

                if (LeftLevel.HasValue && LeftLevel.Value >= 0 && LeftLevel.Value <= 100)
                {
                    twsLevels.Add(LeftLevel.Value);
                }

                if (RightLevel.HasValue && RightLevel.Value >= 0 && RightLevel.Value <= 100)
                {
                    twsLevels.Add(RightLevel.Value);
                }

                if (CaseLevel.HasValue && CaseLevel.Value >= 0 && CaseLevel.Value <= 100)
                {
                    twsLevels.Add(CaseLevel.Value);
                }

                if (twsLevels.Count > 0)
                {
                    return twsLevels.Min();
                }
            }

            if (Level.HasValue && Level.Value >= 0 && Level.Value <= 100)
            {
                return Level.Value;
            }

            return null;
        }
    }

    /// <summary>
    /// Renk kodlaması:
    /// Yeşil: %40 ve üzeri (#107C41 / #22C55E)
    /// Sarı: %20 - %39 (#F7B500 / #EAB308)
    /// Kırmızı: <%20 (#E81123 / #EF4444)
    /// </summary>
    public string StatusColor
    {
        get
        {
            var lowest = EffectiveLowestLevel;
            if (!lowest.HasValue)
            {
                return "#8A8886"; // Nötr Gri
            }

            if (lowest.Value >= 40)
            {
                return "#22C55E"; // Yeşil (Fluent Success)
            }

            if (lowest.Value >= 20)
            {
                return "#EAB308"; // Sarı / Amber (Fluent Warning)
            }

            return "#EF4444"; // Kırmızı (Fluent Critical)
        }
    }

    /// <summary>
    /// Kullanıcı dostu metin özeti.
    /// </summary>
    public string BatterySummaryText
    {
        get
        {
            if (HasMultipleBatteries)
            {
                var parts = new List<string>();
                if (LeftLevel.HasValue)
                {
                    parts.Add($"Sol: %{LeftLevel.Value}{(IsLeftCharging ? "⚡" : "")}");
                }
                if (RightLevel.HasValue)
                {
                    parts.Add($"Sağ: %{RightLevel.Value}{(IsRightCharging ? "⚡" : "")}");
                }
                if (CaseLevel.HasValue)
                {
                    parts.Add($"Kutu: %{CaseLevel.Value}{(IsCaseCharging ? "⚡" : "")}");
                }

                if (parts.Count > 0)
                {
                    return string.Join(" • ", parts);
                }
            }

            if (Level.HasValue)
            {
                return $"%{Level.Value}{(IsCharging ? " ⚡" : "")}";
            }

            return "Bilinmiyor";
        }
    }
}
