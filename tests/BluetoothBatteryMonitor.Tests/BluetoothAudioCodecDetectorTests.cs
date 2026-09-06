extern alias MonitorApp;

using System;
using System.Collections.Generic;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppAudioDeviceInfo = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioDeviceInfo;
using AppCodecDetector = MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.BluetoothAudioCodecDetector;

namespace BluetoothBatteryMonitor.Tests;

public class BluetoothAudioCodecDetectorTests
{
    [Fact]
    public void DetectCodec_WhenDeviceIsNotAudio_ReturnsNull()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE_1",
            Name = "Logitech MX Master 3",
            Type = AppDeviceType.Mouse,
            IsConnected = true
        };

        var codec = detector.DetectCodec(mouse);
        Assert.Null(codec);
    }

    [Fact]
    public void DetectCodec_WhenDeviceIsDisconnected_ReturnsNull()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var headphones = new AppDevice
        {
            Id = "DEV_HEADPHONES_1",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = false
        };

        var codec = detector.DetectCodec(headphones);
        Assert.Null(codec);
    }

    [Fact]
    public void DetectCodec_WhenAlternativeA2dpDriverRegistryHasLdac_ReturnsLdac()
    {
        var mockRegistry = new Dictionary<string, object>
        {
            [@"SOFTWARE\Alternative A2DP Driver\Codec"] = "LDAC"
        };

        var detector = new AppCodecDetector(
            registryReader: (subKey, valName) =>
            {
                string fullKey = $@"{subKey}\{valName}";
                return mockRegistry.TryGetValue(fullKey, out var val) ? val : null;
            },
            osBuildNumber: 22621
        );

        var headphones = new AppDevice
        {
            Id = "DEV_SONY_XM4",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        var codec = detector.DetectCodec(headphones);
        Assert.Equal("LDAC", codec);
    }

    [Fact]
    public void DetectCodec_WhenEndpointNameSpecifiesCodec_ExtractsCodecCorrectly()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var headphones = new AppDevice
        {
            Id = "DEV_EP_1",
            Name = "Bluetooth Headset",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        var endpointLdac = new AppAudioDeviceInfo("EP_1", "Sony WH-1000XM5 (LDAC)", null, true, 1.0f, false);
        Assert.Equal("LDAC", detector.DetectCodec(headphones, endpointLdac));

        var endpointAptxHd = new AppAudioDeviceInfo("EP_2", "B&W PX7 (aptX HD)", null, true, 1.0f, false);
        Assert.Equal("aptX HD", detector.DetectCodec(headphones, endpointAptxHd));

        var endpointAptx = new AppAudioDeviceInfo("EP_3", "Sennheiser CX 400BT (aptX)", null, true, 1.0f, false);
        Assert.Equal("aptX", detector.DetectCodec(headphones, endpointAptx));
    }

    [Fact]
    public void DetectCodec_WhenAppleAirPodsOnWindows11_ReturnsAac()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621); // Windows 11
        var airPods = new AppDevice
        {
            Id = "DEV_AIRPODS",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true
        };

        var codec = detector.DetectCodec(airPods);
        Assert.Equal("AAC", codec);
    }

    [Fact]
    public void DetectCodec_WhenAppleAirPodsOnLegacyWindows10_FallsBackToSbc()
    {
        var detector = new AppCodecDetector(osBuildNumber: 18362); // Windows 10 1903 (öncesi AAC desteklemez)
        var airPods = new AppDevice
        {
            Id = "DEV_AIRPODS",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true
        };

        var codec = detector.DetectCodec(airPods);
        Assert.Equal("SBC", codec);
    }

    [Fact]
    public void DetectCodec_WhenBluetoothAacDisabledInRegistry_FallsBackToSbcOrAptx()
    {
        var mockRegistry = new Dictionary<string, object>
        {
            [@"SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters\BluetoothAacEnable"] = 0
        };

        var detector = new AppCodecDetector(
            registryReader: (subKey, valName) =>
            {
                string fullKey = $@"{subKey}\{valName}";
                return mockRegistry.TryGetValue(fullKey, out var val) ? val : null;
            },
            osBuildNumber: 22621
        );

        var airPods = new AppDevice
        {
            Id = "DEV_AIRPODS",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true
        };

        var codec = detector.DetectCodec(airPods);
        Assert.Equal("SBC", codec);
    }

    [Fact]
    public void DetectCodec_WhenSennheiserMomentumWithAptx_ReturnsAptxOrAptxHd()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var sennheiser = new AppDevice
        {
            Id = "DEV_SENN_HD",
            Name = "Sennheiser Momentum 4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        var codec = detector.DetectCodec(sennheiser);
        Assert.Equal("aptX HD", codec);
    }

    [Fact]
    public void DetectCodec_WhenSamsungGalaxyBudsOnWindows11_ReturnsAac()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var buds = new AppDevice
        {
            Id = "DEV_GALAXY_BUDS",
            Name = "Galaxy Buds2 Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true
        };

        var codec = detector.DetectCodec(buds);
        Assert.Equal("AAC", codec);
    }

    [Fact]
    public void DetectCodec_WhenBoseQuietComfortOnWindows11_ReturnsAac()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var bose = new AppDevice
        {
            Id = "DEV_BOSE",
            Name = "Bose QuietComfort 45",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        var codec = detector.DetectCodec(bose);
        Assert.Equal("AAC", codec);
    }

    [Theory]
    [InlineData("4", "LDAC")]
    [InlineData("ldac", "LDAC")]
    [InlineData("3", "aptX HD")]
    [InlineData("aptx-hd", "aptX HD")]
    [InlineData("2", "aptX")]
    [InlineData("aptx", "aptX")]
    [InlineData("1", "AAC")]
    [InlineData("aac", "AAC")]
    [InlineData("0", "SBC")]
    [InlineData("sbc", "SBC")]
    public void NormalizeCodecName_CorrectlyMapsVariants(string input, string expected)
    {
        string result = AppCodecDetector.NormalizeCodecName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectCodec_WhenGenericDeviceHasAudioName_DetectsCodec()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var genericHeadset = new AppDevice
        {
            Id = "DEV_GENERIC_HEADSET",
            Name = "Soundcore Space Q45",
            Type = AppDeviceType.Generic, // Henüz Type atanmamış olsa bile adı ses cihazı
            IsConnected = true
        };

        var codec = detector.DetectCodec(genericHeadset);
        Assert.NotNull(codec);
        Assert.Equal("AAC", codec);
    }

    [Fact]
    public void DetectCodec_WhenEndpointHasLdac_OverridesBasicHeuristic()
    {
        var detector = new AppCodecDetector(osBuildNumber: 22621);
        var headphones = new AppDevice
        {
            Id = "DEV_SONY_OVERRIDE",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            IsConnected = true
        };

        var endpointLdac = new AppAudioDeviceInfo("EP_LDAC", "WH-1000XM4 Hands-Free", "Sony High-Resolution LDAC Audio", true, 0.8f, false);
        var codec = detector.DetectCodec(headphones, endpointLdac);
        Assert.Equal("LDAC", codec);
    }

    [Fact]
    public void IsProbableAudioDevice_IdentifiesAudioDeviceKeywords()
    {
        var dev1 = new AppDevice { Name = "AirPods Max", Type = AppDeviceType.Generic };
        var dev2 = new AppDevice { Name = "Wireless Earbuds", Type = AppDeviceType.Generic };
        var dev3 = new AppDevice { Name = "Bluetooth Speaker", Type = AppDeviceType.Generic };
        var dev4 = new AppDevice { Name = "Logitech MX Master 3", Type = AppDeviceType.Mouse };

        Assert.True(AppCodecDetector.IsProbableAudioDevice(dev1));
        Assert.True(AppCodecDetector.IsProbableAudioDevice(dev2));
        Assert.True(AppCodecDetector.IsProbableAudioDevice(dev3));
        Assert.False(AppCodecDetector.IsProbableAudioDevice(dev4));
    }
}
