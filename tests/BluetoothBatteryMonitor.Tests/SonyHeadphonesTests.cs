extern alias MonitorApp;

using Xunit;
using AppSony = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.SonyHeadphonesBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class SonyHeadphonesTests
{
    [Fact]
    public void TryParseSonyPacket_WH1000XM4_SingleBattery_ParsesLevelAndCharging()
    {
        // Sony MDR packet with 4-byte length header
        byte[] packet = new byte[10];
        packet[0] = 0x0C; // SOM
        packet[1] = 0x01; // SeqNo
        packet[2] = 0x00; // Length MSB
        packet[3] = 0x00;
        packet[4] = 0x00;
        packet[5] = 0x02; // Length LSB (2 byte payload)
        packet[6] = 0x02; // Function Type: Battery
        packet[7] = 80;   // Battery level: 80%
        packet[8] = 0x01; // Charging: 1
        packet[9] = 0x0C; // EOP

        bool success = AppSony.TryParseSonyPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(80, info.Level);
        Assert.True(info.IsCharging);
        Assert.False(info.IsTws);
    }

    [Fact]
    public void TryParseSonyPacket_WF1000XM4_TwsBattery_ParsesLeftRightCaseAndChargingFlags()
    {
        byte[] packet = new byte[12];
        packet[0] = 0x0C; // SOM
        packet[1] = 0x01;
        packet[2] = 0x00;
        packet[3] = 0x00;
        packet[4] = 0x00;
        packet[5] = 0x04; // Payload length 4
        packet[6] = 0x02; // Function: Battery
        packet[7] = 70;   // Left earbud: 70%
        packet[8] = 85;   // Right earbud: 85%
        packet[9] = 95;   // Case: 95%
        packet[10] = 0x05; // Charging bits: bit 0 (Left) and bit 2 (Case) -> 0x05
        packet[11] = 0x0C; // EOP

        bool success = AppSony.TryParseSonyPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.True(info.IsTws);
        Assert.Equal(70, info.LeftLevel);
        Assert.True(info.IsLeftCharging);
        Assert.Equal(85, info.RightLevel);
        Assert.False(info.IsRightCharging);
        Assert.Equal(95, info.CaseLevel);
        Assert.True(info.IsCaseCharging);
        Assert.Equal(70, info.Level); // Min(70, 85) = 70
    }

    [Fact]
    public void TryParseSonyPacket_StepLevels_CalculatesPercentageCorrectly()
    {
        // On some Sony models, battery level is reported in 0-10 steps (e.g.: 8 -> 80%)
        byte[] packet = new byte[8];
        packet[0] = 0x0C;
        packet[1] = 0x00;
        packet[2] = 0x00;
        packet[3] = 0x02; // 2-byte length format
        packet[4] = 0x02; // Function
        packet[5] = 7;    // Step 7 -> 70%
        packet[6] = 0x00; // Discharging
        packet[7] = 0x0C;

        bool success = AppSony.TryParseSonyPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(70, info.Level);
        Assert.False(info.IsCharging);
    }

    [Fact]
    public void TryParseSonyPacket_CorruptedOrShort_ReturnsFalse()
    {
        Assert.False(AppSony.TryParseSonyPacket(null!, out _));
        Assert.False(AppSony.TryParseSonyPacket([0x0C, 0x01], out _));
        Assert.False(AppSony.TryParseSonyPacket([0x01, 0x02, 0x03, 0x04, 0x05, 0x06], out _)); // No SOM
    }

    [Theory]
    [InlineData("WH-1000XM4", true)]
    [InlineData("WH-1000XM5", true)]
    [InlineData("WF-1000XM4", true)]
    [InlineData("LinkBuds S", true)]
    [InlineData("WH-CH720N", true)]
    [InlineData("Bose QC45", false)]
    public void IsSonyHeadphone_MatchesSupportedModels(string name, bool expected)
    {
        Assert.Equal(expected, AppSony.IsSonyHeadphone(name));
    }

    [Theory]
    [InlineData("WF-1000XM4", true)]
    [InlineData("LinkBuds", true)]
    [InlineData("WH-1000XM4", false)]
    [InlineData("WH-1000XM5", false)]
    public void IsTwsSonyModel_DistinguishesWFFromWH(string name, bool expected)
    {
        Assert.Equal(expected, AppSony.IsTwsSonyModel(name));
    }
}
