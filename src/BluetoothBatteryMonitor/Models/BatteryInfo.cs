using System;
using System.Collections.Generic;
using System.Linq;

namespace BluetoothBatteryMonitor.Models;

/// <summary>
/// Battery data and state information for a Bluetooth device.
/// Supports both standard single batteries and multiple batteries (Left, Right, Case) such as AirPods.
/// </summary>
public class BatteryInfo
{
    /// <summary>
    /// Overall or single battery percentage (0-100).
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Whether the device is charging overall.
    /// </summary>
    public bool IsCharging { get; set; }

    /// <summary>
    /// Whether multiple battery support (such as AirPods Left/Right/Case) is available.
    /// </summary>
    public bool HasMultipleBatteries { get; set; }

    /// <summary>
    /// TWS Left earbud battery (0-100).
    /// </summary>
    public int? LeftLevel { get; set; }

    /// <summary>
    /// Whether the left earbud is charging.
    /// </summary>
    public bool IsLeftCharging { get; set; }

    /// <summary>
    /// TWS Right earbud battery (0-100).
    /// </summary>
    public int? RightLevel { get; set; }

    /// <summary>
    /// Whether the right earbud is charging.
    /// </summary>
    public bool IsRightCharging { get; set; }

    /// <summary>
    /// TWS Charging Case battery (0-100).
    /// </summary>
    public int? CaseLevel { get; set; }

    /// <summary>
    /// Whether the charging case is charging.
    /// </summary>
    public bool IsCaseCharging { get; set; }

    /// <summary>
    /// Timestamp of when the battery information was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// Whether a valid battery level was read (including 0).
    /// </summary>
    public bool HasBattery => EffectiveLowestLevel.HasValue;

    /// <summary>
    /// Calculates the lowest active battery percentage among all available battery components (Left, Right, Case, or Overall).
    /// In TWS earbuds, takes the minimum of active earbud/case batteries; avoids poisoning even if Level is stale.
    /// Dynamic tray icon and critical alerts are based on this value.
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
    /// Color coding:
    /// Green: 40% and above (#107C41 / #22C55E)
    /// Yellow: 20% - 39% (#F7B500 / #EAB308)
    /// Red: &lt;20% (#E81123 / #EF4444)
    /// </summary>
    public string StatusColor
    {
        get
        {
            var lowest = EffectiveLowestLevel;
            if (!lowest.HasValue)
            {
                return "#8A8886"; // Neutral Gray
            }

            if (lowest.Value >= 40)
            {
                return "#22C55E"; // Green (Fluent Success)
            }

            if (lowest.Value >= 20)
            {
                return "#EAB308"; // Yellow / Amber (Fluent Warning)
            }

            return "#EF4444"; // Red (Fluent Critical)
        }
    }

    /// <summary>
    /// User-friendly text summary.
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
