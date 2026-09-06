extern alias MonitorApp;

using Xunit;
using AppFastPair = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.GoogleFastPairBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class GoogleFastPairTests
{
    [Fact]
    public void TryParseFastPairAdvertisement_3ByteTws_ParsesLeftRightCaseAndCharging()
    {
        // Standard 3-byte Fast Pair TWS packet:
        // Byte 0: Left (80%, not charging -> 0x50 = 80)
        // Byte 1: Right (85%, charging -> bit 7 (0x80) | 85 (0x55) = 0xD5)
        // Byte 2: Case (100%, charging -> bit 7 (0x80) | 100 (0x64) = 0xE4)
        byte[] payload = [0x50, 0xD5, 0xE4];

        bool success = AppFastPair.TryParseFastPairAdvertisement(payload, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.True(info.IsTws);
        Assert.Equal(80, info.LeftLevel);
        Assert.False(info.IsLeftCharging);
        Assert.Equal(85, info.RightLevel);
        Assert.True(info.IsRightCharging);
        Assert.Equal(100, info.CaseLevel);
        Assert.True(info.IsCaseCharging);
    }

    [Fact]
    public void TryParseFastPairAdvertisement_4ByteTwsWithHeader_ParsesCorrectly()
    {
        // 4-byte Fast Pair packet (Header byte e.g.: 0x03)
        byte[] payload = [0x03, 0x46, 0x4B, 0x5A]; // Left 70, Right 75, Case 90

        bool success = AppFastPair.TryParseFastPairAdvertisement(payload, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.True(info.IsTws);
        Assert.Equal(70, info.LeftLevel);
        Assert.False(info.IsLeftCharging);
        Assert.Equal(75, info.RightLevel);
        Assert.False(info.IsRightCharging);
        Assert.Equal(90, info.CaseLevel);
        Assert.False(info.IsCaseCharging);
    }

    [Fact]
    public void TryParseFastPairAdvertisement_SingleComponent_ParsesSingleBattery()
    {
        // 1-byte single Fast Pair headphone (Bit 7 = 1 charging, Level = 95 -> 0x80 | 95 = 0xDF)
        byte[] payload = [0xDF];

        bool success = AppFastPair.TryParseFastPairAdvertisement(payload, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.False(info.IsTws);
        Assert.Equal(95, info.SingleLevel);
        Assert.True(info.IsSingleCharging);
    }

    [Fact]
    public void TryParseFastPairAdvertisement_Disconnected0x7F_MapsToNull()
    {
        // Case lid closed or right earbud not inserted in ear (0x7F = 127)
        byte[] payload = [0x50, 0x7F, 0x64]; // Left 80%, Right absent (0x7F), Case 100%

        bool success = AppFastPair.TryParseFastPairAdvertisement(payload, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(80, info.LeftLevel);
        Assert.Null(info.RightLevel);
        Assert.Equal(100, info.CaseLevel);
    }

    [Fact]
    public void TryParseFastPairAdvertisement_AllUnavailable_ReturnsFalse()
    {
        byte[] payload = [0x7F, 0x7F, 0x7F]; // None of the components are present

        bool success = AppFastPair.TryParseFastPairAdvertisement(payload, out var info);

        Assert.False(success);
        Assert.Null(info);
    }

    [Fact]
    public void TryParseFastPairAdvertisement_InvalidOrEmpty_ReturnsFalse()
    {
        Assert.False(AppFastPair.TryParseFastPairAdvertisement(null!, out _));
        Assert.False(AppFastPair.TryParseFastPairAdvertisement([], out _));
    }
}
