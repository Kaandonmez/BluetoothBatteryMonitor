extern alias MonitorApp;

using Xunit;
using AppFastPair = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.GoogleFastPairBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class GoogleFastPairTests
{
    [Fact]
    public void TryParseFastPairAdvertisement_3ByteTws_ParsesLeftRightCaseAndCharging()
    {
        // 3-baytlık standart Fast Pair TWS paketi:
        // Byte 0: Sol (%80, şarjda değil -> 0x50 = 80)
        // Byte 1: Sağ (%85, şarjda -> bit 7 (0x80) | 85 (0x55) = 0xD5)
        // Byte 2: Kutu (%100, şarjda -> bit 7 (0x80) | 100 (0x64) = 0xE4)
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
        // 4-baytlık Fast Pair paketi (Başlık baytı örn: 0x03)
        byte[] payload = [0x03, 0x46, 0x4B, 0x5A]; // Sol 70, Sağ 75, Kutu 90

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
        // 1-baytlık tekli Fast Pair kulaklık (Bit 7 = 1 şarjda, Seviye = 95 -> 0x80 | 95 = 0xDF)
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
        // Kutu kapağı kapalı veya sağ kulaklık kulağa takılmamış (0x7F = 127)
        byte[] payload = [0x50, 0x7F, 0x64]; // Sol %80, Sağ yok (0x7F), Kutu %100

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
        byte[] payload = [0x7F, 0x7F, 0x7F]; // Hiçbir bileşen mevcut değil

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
