extern alias MonitorApp;

using Xunit;
using AppPS = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.PlayStationControllerBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class PlayStationControllerTests
{
    [Fact]
    public void TryParseDualShock4Report_Report0x11_NormalBattery_ParsesLevelAndDischarging()
    {
        // 78-byte simulated DS4 Bluetooth Extended Report (0x11)
        byte[] report = new byte[78];
        report[0] = 0x11; // Report ID
        report[1] = 0xC0;
        report[2] = 0x20;
        // Index 30: Battery byte. 0x08 -> 80%, bit 4 (0x10) off -> not charging
        report[30] = 0x08;

        bool success = AppPS.TryParseDualShock4Report(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(80, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseDualShock4Report_Report0x11_ChargingState_ParsesLevelAndCharging()
    {
        byte[] report = new byte[78];
        report[0] = 0x11;
        // 0x17 -> level 7 (70%), 0x10 bit active -> charging
        report[30] = 0x17;

        bool success = AppPS.TryParseDualShock4Report(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(70, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseDualShock4Report_Report0x11_RawLevel11_Returns100PercentAndCharging()
    {
        byte[] report = new byte[78];
        report[0] = 0x11;
        // 0x1B -> raw 11 (fully charged) + charging bit (0x10)
        report[30] = 0x1B;

        bool success = AppPS.TryParseDualShock4Report(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(100, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseDualShock4Report_Report0x01_BasicReport_ParsesCorrectly()
    {
        byte[] report = new byte[64];
        report[0] = 0x01; // Report ID
        // Index 12: Battery byte
        report[12] = 0x19; // 90%, charging

        bool success = AppPS.TryParseDualShock4Report(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(90, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseDualShock4Report_ShortOrInvalidBuffer_ReturnsFalse()
    {
        Assert.False(AppPS.TryParseDualShock4Report(null!, out _, out _));
        Assert.False(AppPS.TryParseDualShock4Report(new byte[5], out _, out _));
        Assert.False(AppPS.TryParseDualShock4Report(new byte[20] { 0x11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, out _, out _));
    }

    [Fact]
    public void TryParseDualSenseReport_Report0x31_Discharging_ParsesCorrectly()
    {
        // 78-byte DualSense Bluetooth Extended Report (0x31)
        byte[] report = new byte[78];
        report[0] = 0x31; // Report ID
        // Index 53: Battery byte
        // Lower 4 bits: 0x06 -> 60%
        // Upper 4 bits: 0x00 -> discharging
        report[53] = 0x06;

        bool success = AppPS.TryParseDualSenseReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(60, level);
        Assert.False(isCharging);
    }

    [Fact]
    public void TryParseDualSenseReport_Report0x31_ChargingAndFull_ParsesChargingTrue()
    {
        byte[] report = new byte[78];
        report[0] = 0x31;
        // Lower 4 bits: 0x0A -> 10 (100%)
        // Upper 4 bits: 0x10 -> chargeStatus = 1 (charging)
        report[53] = 0x1A;

        bool success = AppPS.TryParseDualSenseReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(100, level);
        Assert.True(isCharging);

        // chargeStatus = 2 (fully charged)
        report[53] = 0x2A;
        success = AppPS.TryParseDualSenseReport(report, out level, out isCharging);
        Assert.True(success);
        Assert.Equal(100, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void TryParseDualSenseReport_Report0x01_ParsesCorrectly()
    {
        byte[] report = new byte[64];
        report[0] = 0x01;
        report[53] = 0x15; // 50%, charging

        bool success = AppPS.TryParseDualSenseReport(report, out int? level, out bool isCharging);

        Assert.True(success);
        Assert.Equal(50, level);
        Assert.True(isCharging);
    }

    [Fact]
    public void ExtractPid_ValidDeviceIds_ExtractsExpectedValues()
    {
        string id1 = @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6&Col01\9&1f78e47&0&0000";
        string id2 = @"USB\VID_054C&PID_09CC\0001";

        Assert.Equal(AppPS.PidDualSense, AppPS.ExtractPid(id1));
        Assert.Equal(AppPS.PidDualShock4V2, AppPS.ExtractPid(id2));
        Assert.Equal(0, AppPS.ExtractPid("INVALID_ID"));
    }

    [Theory]
    [InlineData(AppPS.PidDualShock4V1, "Sony DualShock 4 Wireless Controller (v1)")]
    [InlineData(AppPS.PidDualShock4V2, "Sony DualShock 4 Wireless Controller (v2)")]
    [InlineData(AppPS.PidDualSense, "Sony DualSense Wireless Controller (PS5)")]
    [InlineData(AppPS.PidDualSenseEdge, "Sony DualSense Edge Wireless Controller")]
    public void GetPlayStationModelName_KnownPids_ResolvesAppropriateName(ushort pid, string expectedName)
    {
        Assert.Equal(expectedName, AppPS.GetPlayStationModelName(pid));
    }

    [Fact]
    public void CreateDualShock4ModeSwitchReport_ProducesValid78ByteReport0x11()
    {
        var report = AppPS.CreateDualShock4ModeSwitchReport();
        Assert.Equal(78, report.Length);
        Assert.Equal(0x11, report[0]);
        Assert.Equal(0x80, report[1]);
        Assert.Equal(0x04, report[3]);
    }

    [Fact]
    public void CreateDualSenseModeSwitchReport_ProducesValid78ByteReport0x31()
    {
        var report = AppPS.CreateDualSenseModeSwitchReport();
        Assert.Equal(78, report.Length);
        Assert.Equal(0x31, report[0]);
        Assert.Equal(0x02, report[1]);
        Assert.Equal(0x03, report[2]);
    }
}
