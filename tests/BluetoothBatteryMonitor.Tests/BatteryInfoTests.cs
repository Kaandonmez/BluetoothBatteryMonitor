using BluetoothBatteryMonitor.Models;
using Xunit;

namespace BluetoothBatteryMonitor.Tests;

public class BatteryInfoTests
{
    [Fact]
    public void EffectiveLowestLevel_WhenNoBatteries_ReturnsNull()
    {
        var info = new BatteryInfo();
        Assert.Null(info.EffectiveLowestLevel);
    }

    [Fact]
    public void EffectiveLowestLevel_WithSingleBattery_ReturnsLevel()
    {
        var info = new BatteryInfo { Level = 75 };
        Assert.Equal(75, info.EffectiveLowestLevel);
    }

    [Fact]
    public void EffectiveLowestLevel_WithTwsBatteries_ReturnsMinimum()
    {
        var info = new BatteryInfo
        {
            HasMultipleBatteries = true,
            LeftLevel = 80,
            RightLevel = 45,
            CaseLevel = 90
        };

        Assert.Equal(45, info.EffectiveLowestLevel);
    }

    [Fact]
    public void EffectiveLowestLevel_IgnoresInvalidValues()
    {
        var info = new BatteryInfo
        {
            Level = 150, // Invalid
            LeftLevel = -5, // Invalid
            RightLevel = 35
        };

        Assert.Equal(35, info.EffectiveLowestLevel);
    }

    [Theory]
    [InlineData(100, "#22C55E")] // Green
    [InlineData(40, "#22C55E")]  // Green (boundary)
    [InlineData(39, "#EAB308")]  // Yellow
    [InlineData(20, "#EAB308")]  // Yellow (boundary)
    [InlineData(19, "#EF4444")]  // Red
    [InlineData(5, "#EF4444")]   // Red
    public void StatusColor_ReturnsExpectedColorCode(int level, string expectedColor)
    {
        var info = new BatteryInfo { Level = level };
        Assert.Equal(expectedColor, info.StatusColor);
    }

    [Fact]
    public void StatusColor_WhenNull_ReturnsNeutralGray()
    {
        var info = new BatteryInfo { Level = null };
        Assert.Equal("#8A8886", info.StatusColor);
    }

    [Fact]
    public void BatterySummaryText_SingleBattery_FormatsCorrectly()
    {
        var info = new BatteryInfo { Level = 85, IsCharging = true };
        Assert.Equal("%85 ⚡", info.BatterySummaryText);
    }

    [Fact]
    public void BatterySummaryText_TwsBatteries_FormatsAllComponents()
    {
        var info = new BatteryInfo
        {
            HasMultipleBatteries = true,
            LeftLevel = 80,
            IsLeftCharging = false,
            RightLevel = 90,
            IsRightCharging = true,
            CaseLevel = 100,
            IsCaseCharging = true
        };

        string summary = info.BatterySummaryText;
        Assert.Contains("Sol: %80", summary);
        Assert.Contains("Sağ: %90⚡", summary);
        Assert.Contains("Kutu: %100⚡", summary);
    }

    [Fact]
    public void EffectiveLowestLevel_WithZeroPercent_ReturnsZero()
    {
        var info = new BatteryInfo { Level = 0 };
        Assert.Equal(0, info.EffectiveLowestLevel);
        Assert.True(info.HasBattery);
        Assert.Equal("#EF4444", info.StatusColor); // 0% should be Red
    }

    [Fact]
    public void EffectiveLowestLevel_TwsUpdate_DoesNotGetPoisonedByOldLevel()
    {
        // Previous level was 40 and was stored in Level property
        var info = new BatteryInfo
        {
            HasMultipleBatteries = true,
            Level = 40,
            LeftLevel = 40,
            RightLevel = 50,
            CaseLevel = 60
        };

        Assert.Equal(40, info.EffectiveLowestLevel);

        // Now earbuds charged and new levels arrived (all 80 and above)
        info.LeftLevel = 80;
        info.RightLevel = 85;
        info.CaseLevel = 90;

        // Old Level = 40 value should not poison the new TWS calculation!
        Assert.Equal(80, info.EffectiveLowestLevel);
    }
}
