extern alias MonitorApp;

using System.Collections.Generic;
using System.Linq;
using Xunit;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppEngine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;

namespace BluetoothBatteryMonitor.Tests;

public class DualModeAggregationTests
{
    [Fact]
    public void BluetoothDeviceModel_Matches_MatchesDualModeByMacAddress()
    {
        var classicDev = new AppDevice
        {
            Id = @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_LOCALMFG&0000\7&2a6bb6b&0&702605A1B2C3_00000000",
            Name = "WH-1000XM4",
            Type = AppDeviceType.Headphones,
            BluetoothAddress = 0x702605A1B2C3
        };

        var bleDev = new AppDevice
        {
            Id = @"BTHLE\DEV_702605A1B2C3\8&39e44fd&0&0014",
            Name = "LE_WH-1000XM4",
            Type = AppDeviceType.Generic,
            BluetoothAddress = 0x702605A1B2C3
        };

        Assert.True(classicDev.Matches(bleDev));
        Assert.True(bleDev.Matches(classicDev));
    }

    [Fact]
    public void BluetoothDeviceModel_CleanNameForComparison_NormalizesLePrefixesAndSuffixes()
    {
        string name1 = AppDevice.CleanNameForComparison("LE_WH-1000XM4");
        string name2 = AppDevice.CleanNameForComparison("WH-1000XM4 Stereo");
        string name3 = AppDevice.CleanNameForComparison("Bose QC45 LE");
        string name4 = AppDevice.CleanNameForComparison("Bose QC45");

        Assert.Equal("WH-1000XM4", name1);
        Assert.Equal("WH-1000XM4", name2);
        Assert.Equal("Bose QC45", name3);
        Assert.Equal("Bose QC45", name4);
    }

    [Fact]
    public void AggregateDevices_MergesClassicAudioAndBleGattIntoSingleCard()
    {
        var classicAudioNode = new AppDevice
        {
            Id = "BTHENUM_SONY_A2DP",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            BatteryLevel = null,
            IsConnected = true,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        var bleGattNode = new AppDevice
        {
            Id = "BTHLE_DEV_AABBCCDDEEFF",
            Name = "LE_Sony WH-1000XM4",
            Type = AppDeviceType.Generic,
            BatteryLevel = 78,
            IsCharging = false,
            IsConnected = true,
            ProviderSource = "GATT Pil Servisi",
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        var aggregated = AppEngine.AggregateDevices(new[] { classicAudioNode, bleGattNode });

        Assert.Single(aggregated);
        var card = aggregated.First();

        Assert.True(card.IsAggregated);
        Assert.Equal("Sony WH-1000XM4", card.Name);
        Assert.Equal(AppDeviceType.Headphones, card.Type);
        Assert.Equal(78, card.BatteryLevel);
        Assert.False(card.IsCharging);
        Assert.True(card.IsConnected);
        Assert.Contains("GATT Pil Servisi", card.ProviderSource);
    }

    [Fact]
    public void AggregateDevices_TransfersTwsBatteryDataFromBleToPrimaryCard()
    {
        var classicTwsNode = new AppDevice
        {
            Id = "BTH_AIRPODS_CLASSIC",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            IsConnected = true,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0x112233445566
        };

        var bleTwsNode = new AppDevice
        {
            Id = "BLE_AIRPODS_BEACON",
            Name = "AirPods Pro (2. Nesil)",
            Type = AppDeviceType.Earbuds,
            IsTws = true,
            LeftBatteryLevel = 85,
            RightBatteryLevel = 90,
            CaseBatteryLevel = 100,
            IsLeftCharging = false,
            IsRightCharging = false,
            IsCaseCharging = true,
            IsConnected = true,
            ProviderSource = "Apple AirPods Beacon",
            BluetoothAddress = 0x112233445566
        };

        var aggregated = AppEngine.AggregateDevices(new[] { classicTwsNode, bleTwsNode });

        Assert.Single(aggregated);
        var card = aggregated.First();

        Assert.True(card.IsAggregated);
        Assert.True(card.IsTws);
        Assert.Equal(85, card.LeftBatteryLevel);
        Assert.Equal(90, card.RightBatteryLevel);
        Assert.Equal(100, card.CaseBatteryLevel);
        Assert.True(card.IsCaseCharging);
    }

    [Fact]
    public void AggregateDevices_DoesNotMergeDistinctPhysicalDevices()
    {
        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE_001",
            Name = "Logitech MX Master 3S",
            Type = AppDeviceType.Mouse,
            BluetoothAddress = 0x111111111111
        };

        var keyboard = new AppDevice
        {
            Id = "DEV_KB_002",
            Name = "Logitech MX Mechanical",
            Type = AppDeviceType.Keyboard,
            BluetoothAddress = 0x222222222222
        };

        var aggregated = AppEngine.AggregateDevices(new[] { mouse, keyboard });

        Assert.Equal(2, aggregated.Count);
        Assert.DoesNotContain(aggregated, d => d.IsAggregated);
    }

    [Fact]
    public void BluetoothDeviceModel_Matches_ReturnsFalse_WhenDifferentMacAddressesShareSubstringName()
    {
        var dev1 = new AppDevice
        {
            Id = "DEV_AIRPODS_PRO",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0x112233445566
        };

        var dev2 = new AppDevice
        {
            Id = "DEV_AIRPODS_NORMAL",
            Name = "AirPods",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0x998877665544
        };

        // Farklı fiziksel donanım MAC adreslerine sahip cihazlar isimleri benzese dahi asla eşleşmemelidir!
        Assert.False(dev1.Matches(dev2));
        Assert.False(dev2.Matches(dev1));
    }

    [Fact]
    public void AggregateDevices_PromotesAudioDevice_WhenBleNodeProvidedBeforeClassicNode()
    {
        var bleGattNode = new AppDevice
        {
            Id = "BTHLE_DEV_AABBCCDDEEFF",
            Name = "LE_Sony WH-1000XM4",
            Type = AppDeviceType.Generic,
            BatteryLevel = 88,
            IsCharging = false,
            IsConnected = true,
            ProviderSource = "GATT Pil Servisi",
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        var classicAudioNode = new AppDevice
        {
            Id = "BTHENUM_SONY_A2DP",
            Name = "Sony WH-1000XM4",
            Type = AppDeviceType.Headphones,
            BatteryLevel = null,
            IsConnected = true,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        // BLE önce gelmiş olsa bile ses yetenekli klasik kart master olmalı
        var aggregated = AppEngine.AggregateDevices(new[] { bleGattNode, classicAudioNode });

        Assert.Single(aggregated);
        var card = aggregated.First();
        Assert.Equal("Sony WH-1000XM4", card.Name);
        Assert.Equal(AppDeviceType.Headphones, card.Type);
        Assert.Equal(88, card.BatteryLevel);
        Assert.True(card.IsAggregated);
        Assert.True(card.IsConnected);
    }

    [Fact]
    public void AggregateDevices_TripleNodeIPhone_MergesIntoSingleCardWithHighResolutionGattBattery()
    {
        // 1. Düğüm: Windows PnP / HFP (Klasik BR/EDR, kaba %60 pil)
        var hfpNode = new AppDevice
        {
            Id = @"BTHENUM\{0000111f-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&7613\8&3a358462&0&90ECEAF38843_C00000000",
            Name = "kaan- iPhone’u",
            BatteryLevel = 60,
            Type = AppDeviceType.Phone,
            ProviderSource = "Windows PnP / HFP",
            BluetoothAddress = 0x90ECEAF38843,
            IsConnected = true
        };

        // 2. Düğüm: Windows PnP (BLE, %93 pil)
        var pnpBleNode = new AppDevice
        {
            Id = @"BTHLE\Dev_772f4d139dbd\8&29af66f1&0&772f4d139dbd",
            Name = "kaan- iPhone’u",
            BatteryLevel = 93,
            Type = AppDeviceType.Phone,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0x772F4D139DBD,
            IsConnected = true
        };

        // 3. Düğüm: BLE GATT (GATT Battery Service, %93 pil)
        var bleGattNode = new AppDevice
        {
            Id = @"BluetoothLE#BluetoothLE00:1a:7d:da:71:13-77:2f:4d:13:9d:bd",
            Name = "kaan- iPhone’u",
            BatteryLevel = 93,
            Type = AppDeviceType.Generic,
            ProviderSource = "BLE GATT",
            BluetoothAddress = 0x772F4D139DBD,
            IsConnected = true
        };

        // Farklı geliş sıralarında tekilleştirmeyi test et:
        var aggregatedForward = AppEngine.AggregateDevices(new[] { hfpNode, pnpBleNode, bleGattNode });
        var aggregatedReverse = AppEngine.AggregateDevices(new[] { bleGattNode, pnpBleNode, hfpNode });

        // İleri sıralama doğrulaması
        Assert.Single(aggregatedForward);
        var cardForward = aggregatedForward.First();
        Assert.Equal("kaan- iPhone’u", cardForward.Name);
        Assert.Equal(93, cardForward.BatteryLevel);
        Assert.Equal(AppDeviceType.Phone, cardForward.Type);
        Assert.True(cardForward.IsConnected);
        Assert.True(cardForward.IsAggregated);
        Assert.Contains("Windows PnP", cardForward.ProviderSource);
        Assert.Contains("BLE GATT", cardForward.ProviderSource);

        // Geri sıralama doğrulaması
        Assert.Single(aggregatedReverse);
        var cardReverse = aggregatedReverse.First();
        Assert.Equal("kaan- iPhone’u", cardReverse.Name);
        Assert.Equal(93, cardReverse.BatteryLevel);
        Assert.Equal(AppDeviceType.Phone, cardReverse.Type);
        Assert.True(cardReverse.IsConnected);
        Assert.True(cardReverse.IsAggregated);
    }

    [Theory]
    [InlineData(60, 93, 93)] // HFP %60 ve BLE GATT %93 -> %93 seçilmeli
    [InlineData(93, 60, 93)] // BLE GATT %93 ve HFP %60 -> %93 korunmalı
    [InlineData(40, 47, 47)] // Kaba %40 ve yüksek çözünürlüklü %47 -> %47 seçilmeli
    public void AggregateDevices_BleGattHighResolution_TakesPrecedenceOverCoarseHfpBattery(int firstLevel, int secondLevel, int expectedLevel)
    {
        var first = new AppDevice
        {
            Id = firstLevel == 60 || firstLevel == 40 ? "BTHENUM_HFP" : "BTHLE_GATT",
            Name = "kaan- iPhone’u",
            BatteryLevel = firstLevel,
            ProviderSource = firstLevel == 60 || firstLevel == 40 ? "Windows PnP / HFP" : "BLE GATT",
            BluetoothAddress = firstLevel == 60 || firstLevel == 40 ? 0x90ECEAF38843UL : 0x772F4D139DBDUL,
            IsConnected = true
        };

        var second = new AppDevice
        {
            Id = secondLevel == 60 || secondLevel == 40 ? "BTHENUM_HFP" : "BTHLE_GATT",
            Name = "kaan- iPhone’u",
            BatteryLevel = secondLevel,
            ProviderSource = secondLevel == 60 || secondLevel == 40 ? "Windows PnP / HFP" : "BLE GATT",
            BluetoothAddress = secondLevel == 60 || secondLevel == 40 ? 0x90ECEAF38843UL : 0x772F4D139DBDUL,
            IsConnected = true
        };

        var aggregated = AppEngine.AggregateDevices(new[] { first, second });
        Assert.Single(aggregated);
        Assert.Equal(expectedLevel, aggregated[0].BatteryLevel);
    }

    [Fact]
    public void BluetoothDeviceModel_Matches_MatchesAdjacentMacDualModeDevices()
    {
        var classic = new AppDevice
        {
            Id = "DEV_CLASSIC",
            Name = "Bose NC 700",
            BluetoothAddress = 0xAABBCCDDEE00UL
        };

        var ble = new AppDevice
        {
            Id = "DEV_BLE",
            Name = "LE-Bose NC 700",
            BluetoothAddress = 0xAABBCCDDEE01UL // Ardışık donanımsal dual-mode adresi
        };

        Assert.True(classic.Matches(ble));
        Assert.True(ble.Matches(classic));
    }

    [Fact]
    public void AggregateDevices_DoesNotMergeSeparateMice_WithSameModelNameAndDifferentMacs()
    {
        // İki farklı fiziksel fare, aynı sağlayıcıdan (PnP) farklı donanım MAC adresleriyle
        var mouse1 = new AppDevice
        {
            Id = "DEV_MOUSE_A",
            Name = "Logitech MX Master 3S",
            Type = AppDeviceType.Mouse,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0x111111111111UL
        };

        var mouse2 = new AppDevice
        {
            Id = "DEV_MOUSE_B",
            Name = "Logitech MX Master 3S",
            Type = AppDeviceType.Mouse,
            ProviderSource = "Windows PnP",
            BluetoothAddress = 0x222222222222UL
        };

        var aggregated = AppEngine.AggregateDevices(new[] { mouse1, mouse2 });
        Assert.Equal(2, aggregated.Count);
    }

    [Fact]
    public void AggregateDevices_DoesNotMergeGenericNames_WithDifferentMacs()
    {
        var controller1 = new AppDevice
        {
            Id = "DEV_CTRL_1",
            Name = "Wireless Controller",
            Type = AppDeviceType.Gamepad,
            BluetoothAddress = 0x333333333333UL
        };

        var controller2 = new AppDevice
        {
            Id = "DEV_CTRL_2",
            Name = "Wireless Controller",
            Type = AppDeviceType.Gamepad,
            BluetoothAddress = 0x444444444444UL
        };

        var aggregated = AppEngine.AggregateDevices(new[] { controller1, controller2 });
        Assert.Equal(2, aggregated.Count);
    }

    [Fact]
    public void ExtractMacAddress_HandlesBluetoothLeFormatWithAdapterAndDevice()
    {
        string uwpId = @"BluetoothLE#BluetoothLE00:1a:7d:da:71:13-77:2f:4d:13:9d:bd";
        ulong extracted = AppDevice.ExtractMacAddress(uwpId);

        // Adaptör MAC'i (00:1a:7d:da:71:13) değil, cihazın asıl MAC'i (77:2f:4d:13:9d:bd) çıkarılmalı!
        Assert.Equal(0x772F4D139DBDUL, extracted);
    }

    [Fact]
    public void CleanNameForComparison_NormalizesApostrophesAndSmartQuotes()
    {
        string smartQuote = AppDevice.CleanNameForComparison("kaan- iPhone’u");
        string standardApostrophe = AppDevice.CleanNameForComparison("kaan- iPhone'u");

        Assert.Equal(smartQuote, standardApostrophe);
    }

    [Fact]
    public void AggregateDevices_BatteryDischargeToMultipleOfTen_UpdatesSuccessfully()
    {
        var now = DateTime.Now;
        var master = new AppDevice
        {
            Id = "DEV_BLE_01",
            Name = "iPhone",
            BatteryLevel = 93,
            ProviderSource = "BLE GATT",
            LastUpdated = now
        };

        var newerUpdate = new AppDevice
        {
            Id = "DEV_BLE_01",
            Name = "iPhone",
            BatteryLevel = 90, // 10'un tam katı olan yeni değer
            ProviderSource = "BLE GATT",
            LastUpdated = now.AddMinutes(5)
        };

        AppEngine.MergeDualModePair(master, newerUpdate);

        // %10 dilimi olsa dahi daha yeni telemetri geldiğinde pil %90 olarak güncellenmelidir!
        Assert.Equal(90, master.BatteryLevel);

        var thirdUpdate = new AppDevice
        {
            Id = "DEV_BLE_01",
            Name = "iPhone",
            BatteryLevel = 80,
            ProviderSource = "BLE GATT",
            LastUpdated = now.AddMinutes(15)
        };

        AppEngine.MergeDualModePair(master, thirdUpdate);
        Assert.Equal(80, master.BatteryLevel);
    }

    [Fact]
    public void Matches_AdjacentMacAddresses_DoNotMatch_WhenNamesAndTypesDiffer()
    {
        // Aynı üretici/partiden gelip ardışık MAC almış fare ve klavye (diff = 1 <= 3)
        var mouse = new AppDevice
        {
            Id = "DEV_MOUSE_001",
            Name = "Logitech MX Master 3S",
            Type = AppDeviceType.Mouse,
            BluetoothAddress = 0x001122334401UL
        };

        var keyboard = new AppDevice
        {
            Id = "DEV_KB_002",
            Name = "Logitech MX Mechanical Keyboard",
            Type = AppDeviceType.Keyboard,
            BluetoothAddress = 0x001122334402UL
        };

        Assert.False(mouse.Matches(keyboard));
        Assert.False(keyboard.Matches(mouse));

        var aggregated = AppEngine.AggregateDevices(new[] { mouse, keyboard });
        Assert.Equal(2, aggregated.Count);
    }

    [Fact]
    public void Matches_DifferentModelTiers_DoNotMatch_EvenWhenOneMacIsZero()
    {
        var airpods = new AppDevice
        {
            Id = "DEV_AIRPODS",
            Name = "AirPods",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0 // Bilinmeyen MAC
        };

        var airpodsPro = new AppDevice
        {
            Id = "DEV_AIRPODS_PRO",
            Name = "AirPods Pro",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0x112233445566UL
        };

        Assert.False(airpods.Matches(airpodsPro));
        Assert.False(airpodsPro.Matches(airpods));

        var pixelBuds = new AppDevice
        {
            Id = "DEV_PIXEL_BUDS",
            Name = "Pixel Buds",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0
        };

        var pixelBudsPro = new AppDevice
        {
            Id = "DEV_PIXEL_BUDS_PRO",
            Name = "Pixel Buds Pro",
            Type = AppDeviceType.Earbuds,
            BluetoothAddress = 0x223344556677UL
        };

        Assert.False(pixelBuds.Matches(pixelBudsPro));
        Assert.False(pixelBudsPro.Matches(pixelBuds));
    }

    [Fact]
    public void ExtractMacAddress_MixedSeparators_PrefersRemoteDeviceOverAdapter()
    {
        // PC adaptörü iki nokta (00:1a:7d:da:71:13), uzak cihaz tire (77-2f-4d-13-9d-bd) ile ayrılmış
        string mixedId = @"BluetoothLE#BluetoothLE00:1a:7d:da:71:13-77-2f-4d-13-9d-bd";
        ulong extracted = AppDevice.ExtractMacAddress(mixedId);

        // Her halükarda dize sonundaki uç cihazın MAC'i (77:2f:4d:13:9d:bd) dönmelidir
        Assert.Equal(0x772F4D139DBDUL, extracted);
    }

    [Fact]
    public void IsAppleOrMobileDevice_DoesNotClassifyHeadphonesAsPhones()
    {
        Assert.False(AppDevice.IsAppleOrMobileDevice("Sony WH-1000XM4 Headphones", "DEV_SONY"));
        Assert.False(AppDevice.IsAppleOrMobileDevice("Bose QuietComfort Earphones", "DEV_BOSE"));
        Assert.False(AppDevice.IsAppleOrMobileDevice("Razer Kraken Headphone", "DEV_RAZER"));

        Assert.True(AppDevice.IsAppleOrMobileDevice("kaan- iPhone’u", "DEV_IPHONE"));
        Assert.True(AppDevice.IsAppleOrMobileDevice("Samsung Galaxy S24 Ultra", "DEV_GALAXY"));
        Assert.True(AppDevice.IsAppleOrMobileDevice("Kaan'ın Telefonu", "DEV_PHONE"));
    }

    [Fact]
    public void MergeProviderSources_DoesNotContainHfp_WhenBleGattIsPresent()
    {
        string merged = AppEngine.MergeProviderSources("Windows PnP / HFP", "BLE GATT");
        Assert.Equal("Windows PnP / BLE GATT", merged);
        Assert.DoesNotContain("/ HFP", merged);
    }
}
