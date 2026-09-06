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
            Level = 150, // Geçersiz
            LeftLevel = -5, // Geçersiz
            RightLevel = 35
        };

        Assert.Equal(35, info.EffectiveLowestLevel);
    }

    [Theory]
    [InlineData(100, "#22C55E")] // Yeşil
    [InlineData(40, "#22C55E")]  // Yeşil (sınır)
    [InlineData(39, "#EAB308")]  // Sarı
    [InlineData(20, "#EAB308")]  // Sarı (sınır)
    [InlineData(19, "#EF4444")]  // Kırmızı
    [InlineData(5, "#EF4444")]   // Kırmızı
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
        Assert.Equal("#EF4444", info.StatusColor); // %0 Kırmızı olmalı
    }

    [Fact]
    public void EffectiveLowestLevel_TwsUpdate_DoesNotGetPoisonedByOldLevel()
    {
        // Önceki seviye 40 idi ve Level alanına yazılmıştı
        var info = new BatteryInfo
        {
            HasMultipleBatteries = true,
            Level = 40,
            LeftLevel = 40,
            RightLevel = 50,
            CaseLevel = 60
        };

        Assert.Equal(40, info.EffectiveLowestLevel);

        // Şimdi kulaklıklar şarj oldu ve yeni seviyeler geldi (hepsi 80 ve üstü)
        info.LeftLevel = 80;
        info.RightLevel = 85;
        info.CaseLevel = 90;

        // Eski Level = 40 değeri yeni TWS hesaplamasını zehirlememeli!
        Assert.Equal(80, info.EffectiveLowestLevel);
    }
}
