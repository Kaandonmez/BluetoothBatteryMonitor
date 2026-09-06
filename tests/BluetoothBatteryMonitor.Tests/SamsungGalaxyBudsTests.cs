extern alias MonitorApp;

using Xunit;
using AppBuds = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.SamsungGalaxyBudsBatteryProvider;

namespace BluetoothBatteryMonitor.Tests;

public class SamsungGalaxyBudsTests
{
    [Fact]
    public void TryParseGalaxyBudsPacket_ExtendedStatus0x60_ParsesLeftRightCaseAndCharging()
    {
        // Galaxy Buds Extended Status paketi (0x60)
        byte[] packet = new byte[12];
        packet[0] = 0xFD; // SOM
        packet[1] = 0x08; // Uzunluk LSB
        packet[2] = 0x00; // Uzunluk MSB
        packet[3] = 0x60; // MsgId: Extended Status
        packet[4] = 0x01; // Revision
        packet[5] = 85;   // Sol Kulaklık: %85
        packet[6] = 90;   // Sağ Kulaklık: %90
        packet[7] = 0x00; // Coupled / Placement
        packet[8] = 100;  // Kutu: %100
        packet[9] = 0x05; // Şarj bitleri: bit 0 (Sol: 0x01) ve bit 2 (Kutu: 0x04) -> 0x05

        bool success = AppBuds.TryParseGalaxyBudsPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(85, info.LeftLevel);
        Assert.True(info.IsLeftCharging);
        Assert.Equal(90, info.RightLevel);
        Assert.False(info.IsRightCharging);
        Assert.Equal(100, info.CaseLevel);
        Assert.True(info.IsCaseCharging);
    }

    [Fact]
    public void TryParseGalaxyBudsPacket_BasicStatus0x61_ParsesCorrectly()
    {
        byte[] packet = new byte[10];
        packet[0] = 0xFD;
        packet[1] = 0x06;
        packet[2] = 0x00;
        packet[3] = 0x61; // MsgId: Basic Status
        packet[4] = 60;   // Sol: %60
        packet[5] = 65;   // Sağ: %65
        packet[6] = 80;   // Kutu: %80
        packet[7] = 0x02; // Sağ şarjda (bit 1 = 0x02)

        bool success = AppBuds.TryParseGalaxyBudsPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(60, info.LeftLevel);
        Assert.False(info.IsLeftCharging);
        Assert.Equal(65, info.RightLevel);
        Assert.True(info.IsRightCharging);
        Assert.Equal(80, info.CaseLevel);
        Assert.False(info.IsCaseCharging);
    }

    [Fact]
    public void TryParseGalaxyBudsPacket_WithOffset_FindsSom0xFDAndParses()
    {
        // Başında gürültü / protokol başlığı olan paket
        byte[] packet = new byte[16];
        packet[0] = 0xAA;
        packet[1] = 0xBB;
        packet[2] = 0xFD; // SOM burada başlıyor
        packet[3] = 0x08;
        packet[4] = 0x00;
        packet[5] = 0x60;
        packet[6] = 0x01;
        packet[7] = 75;  // Sol: 75
        packet[8] = 80;  // Sağ: 80
        packet[9] = 0x00;
        packet[10] = 95; // Kutu: 95
        packet[11] = 0x07; // Hepsi şarjda

        bool success = AppBuds.TryParseGalaxyBudsPacket(packet, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal(75, info.LeftLevel);
        Assert.Equal(80, info.RightLevel);
        Assert.Equal(95, info.CaseLevel);
        Assert.True(info.IsLeftCharging);
        Assert.True(info.IsRightCharging);
        Assert.True(info.IsCaseCharging);
    }

    [Fact]
    public void TryParseGalaxyBudsPacket_CorruptedOrShort_ReturnsFalse()
    {
        Assert.False(AppBuds.TryParseGalaxyBudsPacket(null!, out _));
        Assert.False(AppBuds.TryParseGalaxyBudsPacket([0xFD, 0x01, 0x00], out _));
        Assert.False(AppBuds.TryParseGalaxyBudsPacket([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07], out _)); // SOM yok
    }

    [Theory]
    [InlineData("Galaxy Buds3 Pro", "Samsung Galaxy Buds3 Pro")]
    [InlineData("Galaxy Buds3", "Samsung Galaxy Buds3")]
    [InlineData("Galaxy Buds2 Pro (F123)", "Samsung Galaxy Buds2 Pro")]
    [InlineData("Galaxy Buds2", "Samsung Galaxy Buds2")]
    [InlineData("Galaxy Buds Pro", "Samsung Galaxy Buds Pro")]
    [InlineData("Galaxy Buds Live", "Samsung Galaxy Buds Live")]
    [InlineData("Galaxy Buds+", "Samsung Galaxy Buds+")]
    [InlineData("Galaxy Buds FE", "Samsung Galaxy Buds FE")]
    public void DetectBudsModelName_VariousModels_ResolvesCorrectly(string rawName, string expectedModel)
    {
        string actual = AppBuds.DetectBudsModelName(rawName);
        Assert.Equal(expectedModel, actual);
    }

    [Theory]
    [InlineData("Galaxy Buds3 Pro", true)]
    [InlineData("Galaxy Buds3", true)]
    [InlineData("Galaxy Buds2 Pro", true)]
    [InlineData("Kaan's Galaxy Buds Live", true)]
    [InlineData("Sony WH-1000XM4", false)]
    [InlineData("Xbox Controller", false)]
    public void IsSamsungBudsDevice_ValidatesPatterns(string name, bool expected)
    {
        Assert.Equal(expected, AppBuds.IsSamsungBudsDevice(name));
    }
}
