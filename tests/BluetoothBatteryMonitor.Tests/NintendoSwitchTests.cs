extern alias MonitorApp;

using Xunit;
using AppNintendo = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.NintendoSwitchBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class NintendoSwitchTests
{
    [Fact]
    public void TryParseNintendoReport_Report0x30_FullBatteryDischarging_ParsesCorrectly()
    {
        byte[] report = new byte[49];
        report[0] = 0x30; // Report ID
        report[1] = 0x01; // Timer
        // Byte 2: Battery.
        // High nibble: bits 1-3 = level (4 = full, so 4 << 1 = 8), bit 0 = charging (0).
        // (8 << 4) = 0x80
        report[2] = 0x80;

        bool success = AppNintendo.TryParseNintendoReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(100, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseNintendoReport_Report0x21_MediumBatteryCharging_ParsesCorrectly()
    {
        byte[] report = new byte[49];
        report[0] = 0x21; // Report ID
        report[1] = 0x02;
        // High nibble: level step 3 (medium, 3 << 1 = 6), charging = 1 -> nibble = 7 -> 0x70
        report[2] = 0x70;

        bool success = AppNintendo.TryParseNintendoReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(70, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseNintendoReport_Report0x3F_LowBatteryDischarging_ParsesCorrectly()
    {
        byte[] report = new byte[12];
        report[0] = 0x3F; // Simple Report ID
        report[1] = 0x00;
        // High nibble: level step 2 (low, 2 << 1 = 4), charging = 0 -> nibble = 4 -> 0x40
        report[2] = 0x40;

        bool success = AppNintendo.TryParseNintendoReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(30, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseNintendoReport_Report0x30_CriticalBattery_ParsesCorrectly()
    {
        byte[] report = new byte[49];
        report[0] = 0x30;
        // High nibble: level step 1 (critical, 1 << 1 = 2), charging = 0 -> nibble = 2 -> 0x20
        report[2] = 0x20;

        bool success = AppNintendo.TryParseNintendoReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(10, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseNintendoReport_ShortOrInvalid_ReturnsFalse()
    {
        Assert.False(AppNintendo.TryParseNintendoReport(null!, out _, out _));
        Assert.False(AppNintendo.TryParseNintendoReport(new byte[2], out _, out _));
        Assert.False(AppNintendo.TryParseNintendoReport(new byte[5] { 0x01, 0, 0, 0, 0 }, out _, out _)); // Bilinmeyen ID
    }

    [Theory]
    [InlineData(AppNintendo.PidJoyConL, "Nintendo Joy-Con (L)")]
    [InlineData(AppNintendo.PidJoyConR, "Nintendo Joy-Con (R)")]
    [InlineData(AppNintendo.PidProController, "Nintendo Switch Pro Controller")]
    [InlineData(AppNintendo.PidJoyConChargingGrip, "Nintendo Joy-Con Charging Grip")]
    public void GetNintendoModelName_KnownPids_ResolvesAppropriateName(ushort pid, string expected)
    {
        Assert.Equal(expected, AppNintendo.GetNintendoModelName(pid));
    }

    [Fact]
    public void ExtractPid_ValidDeviceIds_ExtractsExpectedValues()
    {
        string id1 = @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002057e_PID&2009&Col01\9&1f78e47&0&0000";
        string id2 = @"USB\VID_057E&PID_2006\0001";

        Assert.Equal(AppNintendo.PidProController, AppNintendo.ExtractPid(id1));
        Assert.Equal(AppNintendo.PidJoyConL, AppNintendo.ExtractPid(id2));
        Assert.Equal(0, AppNintendo.ExtractPid("INVALID"));
    }
}
