using BluetoothBatteryMonitor.Services;
using Xunit;

namespace BluetoothBatteryMonitor.Tests;

public class LogitechHidBatteryTests
{
    [Fact]
    public void TryParseLogitechBatteryReport_ValidUnifiedBatteryReport_ParsesCorrectly()
    {
        // 20 baytlık HID++ Long Report (Feature 0x1004)
        byte[] report = new byte[20];
        report[0] = 0x11; // Long report ID
        report[1] = 0xFF; // Device index
        report[2] = 0x05; // Feature index
        report[3] = 0x00; // Function
        report[4] = 78;   // %78 Pil
        report[5] = 0x01; // Şarj oluyor (1)

        bool success = LogitechHidBatteryProvider.TryParseLogitechBatteryReport(report, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(78, info.Level);
        Assert.True(info.IsCharging);
        Assert.Equal("Şarj Ediliyor", info.StatusDescription);
    }

    [Fact]
    public void TryParseLogitechBatteryReport_Feature1000_K380Report_ParsesCorrectly()
    {
        // Logitech K380 0x1000 Feature Response (11-FF-06-0D-5A-32-00-...)
        byte[] report = new byte[20];
        report[0] = 0x11;
        report[1] = 0xFF;
        report[2] = 0x06;
        report[3] = 0x0D;
        report[4] = 0x5A; // 90%
        report[5] = 0x32; // next level
        report[6] = 0x00; // discharging

        bool success = LogitechHidBatteryProvider.TryParseLogitechBatteryReport(report, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(90, info.Level);
        Assert.False(info.IsCharging);
        Assert.Equal("Pilde", info.StatusDescription);
    }

    [Fact]
    public void TryParseLogitechBatteryReport_DischargingStatus_ParsesCorrectly()
    {
        byte[] report = new byte[20];
        report[0] = 0x11;
        report[4] = 42;   // %42
        report[5] = 0x00; // Pilde / Deşarj

        bool success = LogitechHidBatteryProvider.TryParseLogitechBatteryReport(report, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(42, info.Level);
        Assert.False(info.IsCharging);
        Assert.Equal("Pilde", info.StatusDescription);
    }

    [Fact]
    public void TryParseLogitechBatteryReport_InvalidReport_ReturnsFalse()
    {
        byte[] invalidReport = [0x01, 0x02, 0x03]; // Çok kısa ve yanlış ID
        bool success = LogitechHidBatteryProvider.TryParseLogitechBatteryReport(invalidReport, out var info);

        Assert.False(success);
        Assert.Null(info);
    }

    [Fact]
    public void TryParseLogitechBatteryReport_ZeroPercentBattery_ParsesCorrectly()
    {
        byte[] report = new byte[20];
        report[0] = 0x11;
        report[4] = 0;    // %0 Pil (Kritik tükenmiş)
        report[5] = 0x00; // Deşarj

        bool success = LogitechHidBatteryProvider.TryParseLogitechBatteryReport(report, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(0, info.Level);
        Assert.False(info.IsCharging);
    }
}
