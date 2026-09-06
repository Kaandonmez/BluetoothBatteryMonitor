using BluetoothBatteryMonitor.Services;
using Xunit;

namespace BluetoothBatteryMonitor.Tests;

public class AppleAirPodsBeaconTests
{
    [Fact]
    public void TryParseAirPodsAdvertisement_ValidAirPodsProPacket_ParsesCorrectly()
    {
        // 27-byte simulated AirPods Pro beacon packet
        byte[] packet = new byte[27];
        packet[0] = 0x07; // Type 0x07 (Proximity Pairing)
        packet[1] = 0x19; // Length 25 bytes
        packet[2] = 0x0E; // Model MSB
        packet[3] = 0x20; // Model LSB -> 0x0E20 (AirPods Pro 1st Gen)
        packet[4] = 0x00;
        packet[5] = 0x00;
        packet[6] = 0x89; // Nibble 1 = 8 (80%), Nibble 2 = 9 (90%)
        packet[7] = 0x1A; // Charging bits: 0x1 (Pod A charging), Case nibble: 0xA (100%)
        packet[8] = 0x00; // isFlipped = false

        bool success = AppleAirPodsBeaconProvider.TryParseAirPodsAdvertisement(packet, out var data);

        Assert.True(success);
        Assert.NotNull(data);
        Assert.Equal("AirPods Pro (1. Nesil)", data.ModelName);
        Assert.Equal(80, data.LeftLevel);
        Assert.True(data.IsLeftCharging);
        Assert.Equal(90, data.RightLevel);
        Assert.False(data.IsRightCharging);
        Assert.Equal(100, data.CaseLevel);
        Assert.False(data.IsCaseCharging);
    }

    [Fact]
    public void TryParseAirPodsAdvertisement_WhenFlipped_SwapsLeftAndRight()
    {
        byte[] packet = new byte[27];
        packet[0] = 0x07;
        packet[1] = 0x19;
        packet[2] = 0x0F;
        packet[3] = 0x20; // AirPods 2nd Gen
        packet[6] = 0x75; // Pod A = 7 (70%), Pod B = 5 (50%)
        packet[7] = 0x28; // Pod B charging (bit 1 = 0x02), Case = 8 (80%)
        packet[8] = 0x20; // isFlipped = true (0x20 bit active)

        bool success = AppleAirPodsBeaconProvider.TryParseAirPodsAdvertisement(packet, out var data);

        Assert.True(success);
        Assert.NotNull(data);
        // When flipped: Left = Pod B (50%), Right = Pod A (70%)
        Assert.Equal(50, data.LeftLevel);
        Assert.True(data.IsLeftCharging);
        Assert.Equal(70, data.RightLevel);
        Assert.False(data.IsRightCharging);
    }

    [Fact]
    public void TryParseAirPodsAdvertisement_DisconnectedNibbles_MapsToNull()
    {
        byte[] packet = new byte[27];
        packet[0] = 0x07;
        packet[1] = 0x19;
        packet[2] = 0x13;
        packet[3] = 0x20; // AirPods 3rd Gen
        packet[6] = 0xFF; // Both buds are 15 (0x0F) -> Disconnected / Not in case
        packet[7] = 0x0F; // Case is also 15 -> Unknown

        bool success = AppleAirPodsBeaconProvider.TryParseAirPodsAdvertisement(packet, out var data);

        Assert.True(success);
        Assert.NotNull(data);
        Assert.Null(data.LeftLevel);
        Assert.Null(data.RightLevel);
        Assert.Null(data.CaseLevel);
    }

    [Fact]
    public void TryParseAirPodsAdvertisement_CorruptOrShortData_ReturnsFalseGracefully()
    {
        byte[] shortPacket = [0x07, 0x19, 0x0E];
        bool shortSuccess = AppleAirPodsBeaconProvider.TryParseAirPodsAdvertisement(shortPacket, out var shortData);
        Assert.False(shortSuccess);
        Assert.Null(shortData);

        bool nullSuccess = AppleAirPodsBeaconProvider.TryParseAirPodsAdvertisement(null!, out var nullData);
        Assert.False(nullSuccess);
        Assert.Null(nullData);
    }

    [Theory]
    [InlineData((ushort)0x0E20, "AirPods Pro (1. Nesil)")]
    [InlineData((ushort)0x200E, "AirPods Pro (1. Nesil)")] // Little-endian
    [InlineData((ushort)0x1420, "AirPods Pro (2. Nesil)")]
    [InlineData((ushort)0x2420, "AirPods Pro (2. Nesil, USB-C)")]
    [InlineData((ushort)0x0A20, "AirPods Max")]
    public void GetModelName_DualEndianness_ResolvesExpectedName(ushort modelId, string expectedName)
    {
        string actual = AppleAirPodsBeaconProvider.GetModelName(modelId);
        Assert.Equal(expectedName, actual);
    }
}
