extern alias MonitorApp;

using Xunit;
using AppSS = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.SteelSeriesBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class SteelSeriesTests
{
    [Fact]
    public void TryParseSteelSeriesReport_Report0xB0_Arctis7_ParsesLevelAndCharging()
    {
        byte[] report = new byte[8];
        report[0] = 0xB0; // Report ID
        report[1] = 0x00;
        report[2] = 85;   // Pil seviyesi: %85
        report[3] = 0x01; // Şarj ediliyor: 1

        bool success = AppSS.TryParseSteelSeriesReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(85, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseSteelSeriesReport_Report0xB0_BarStepLevels_CalculatesPercentageCorrectly()
    {
        byte[] report = new byte[8];
        report[0] = 0xB0;
        report[1] = 0x00;
        report[2] = 3;    // 3 çubuk -> %75
        report[3] = 0x00; // Deşarjda

        bool success = AppSS.TryParseSteelSeriesReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(75, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseSteelSeriesReport_Report0x00_NovaSeries_ParsesLevelCorrectly()
    {
        byte[] report = new byte[8];
        report[0] = 0x00; // Report ID 0x00
        report[1] = 0x00;
        report[2] = 92;   // %92
        report[3] = 0x01; // Şarjda

        bool success = AppSS.TryParseSteelSeriesReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(92, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseSteelSeriesReport_ShortOrInvalid_ReturnsFalse()
    {
        Assert.False(AppSS.TryParseSteelSeriesReport(null!, out _, out _));
        Assert.False(AppSS.TryParseSteelSeriesReport(new byte[2], out _, out _));
        Assert.False(AppSS.TryParseSteelSeriesReport(new byte[5] { 0xFF, 0, 0, 0, 0 }, out _, out _));
    }

    [Theory]
    [InlineData(AppSS.PidArctis7, "SteelSeries Arctis 7 Wireless Headset")]
    [InlineData(AppSS.PidArctisNova7, "SteelSeries Arctis Nova 7 Wireless Headset")]
    [InlineData(AppSS.PidArctisProWireless, "SteelSeries Arctis Pro Wireless Headset")]
    public void GetSteelSeriesModelName_KnownPids_ResolvesAppropriateName(ushort pid, string expected)
    {
        Assert.Equal(expected, AppSS.GetSteelSeriesModelName(pid));
    }

    [Fact]
    public void ExtractPid_ValidDeviceIds_ExtractsExpectedValues()
    {
        string id = @"USB\VID_1038&PID_12AD\0001";
        Assert.Equal(AppSS.PidArctis7, AppSS.ExtractPid(id));
        Assert.Equal(0, AppSS.ExtractPid("INVALID"));
    }
}
