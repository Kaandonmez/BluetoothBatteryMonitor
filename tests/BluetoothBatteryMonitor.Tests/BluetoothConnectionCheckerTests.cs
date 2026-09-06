extern alias MonitorApp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using AppDeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using AppChecker = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothConnectionChecker;
using AppEngine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;
using AppToast = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;
using AppItemVM = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.DeviceItemViewModel;
using AppAirPods = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.AppleAirPodsBeaconProvider;

namespace BluetoothBatteryMonitor.Tests;

public class BluetoothConnectionCheckerTests
{
    private readonly ITestOutputHelper _output;

    public BluetoothConnectionCheckerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BluetoothDeviceModel_DefaultIsConnected_IsFalse()
    {
        var model = new AppDevice();
        Assert.False(model.IsConnected, "BluetoothDeviceModel varsayılan olarak IsConnected = false olmalıdır.");
        Assert.Equal("Disconnected", model.ConnectionStatusText);

        model.IsConnected = true;
        Assert.Equal("Connected", model.ConnectionStatusText);
    }

    [Fact]
    public void DeviceItemViewModel_DisplaysCorrectConnectionStatus()
    {
        var disconnectedModel = new AppDevice
        {
            Id = "DEV_01",
            Name = "Mifa_A20",
            BatteryLevel = 100,
            IsConnected = false,
            DeviceType = AppDeviceType.Speaker
        };

        var vmDisconnected = new AppItemVM(disconnectedModel);
        Assert.False(vmDisconnected.IsConnected);
        Assert.Equal("Disconnected", vmDisconnected.ConnectionStatusText);

        var connectedModel = new AppDevice
        {
            Id = "DEV_02",
            Name = "Keyboard K380",
            BatteryLevel = 90,
            IsConnected = true,
            DeviceType = AppDeviceType.Keyboard
        };

        var vmConnected = new AppItemVM(connectedModel);
        Assert.True(vmConnected.IsConnected);
        Assert.Equal("Connected", vmConnected.ConnectionStatusText);
    }

    [Fact]
    public void BluetoothConnectionSnapshot_AccuratelyEvaluatesConnections()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();

        ulong connectedMac = 0xF473359EFDC6;
        ulong disconnectedMac = 0xF44EFD69FA6F;

        snapshot.ClassicConnectedByMac[connectedMac] = true;
        snapshot.AepConnectedByMac[connectedMac] = true;

        snapshot.ClassicConnectedByMac[disconnectedMac] = false;
        snapshot.AepConnectedByMac[disconnectedMac] = false;

        var statusConnected = snapshot.TryGetStatus(connectedMac, "BTHENUM_01", "Keyboard K380", AppDeviceType.Keyboard);
        Assert.True(statusConnected.HasValue && statusConnected.Value, "Bağlı cihaz true dönmelidir.");

        var statusDisconnected = snapshot.TryGetStatus(disconnectedMac, "BTHENUM_02", "Mifa_A20", AppDeviceType.Speaker);
        Assert.True(statusDisconnected.HasValue && !statusDisconnected.Value, "Bağlı olmayan cihaz false dönmelidir.");
    }

    [Fact]
    public void MergeDualModePair_BothDisconnected_RemainsDisconnected()
    {
        var master = new AppDevice
        {
            Id = "BTHENUM_DEV1",
            Name = "Mifa_A20",
            IsConnected = false,
            BatteryLevel = 100,
            BluetoothAddress = 0xF44EFD69FA6F
        };

        var secondary = new AppDevice
        {
            Id = "BTHLE_DEV1",
            Name = "Mifa_A20",
            IsConnected = false,
            BatteryLevel = 100,
            BluetoothAddress = 0xF44EFD69FA6F
        };

        AppEngine.MergeDualModePair(master, secondary);
        Assert.False(master.IsConnected, "Her iki düğüm de bağlı değilse birleşik cihaz bağlı olmamalıdır.");
    }

    [Fact]
    public void MergeDualModePair_OneConnected_BecomesConnected()
    {
        var master = new AppDevice
        {
            Id = "BTHENUM_DEV1",
            Name = "WH-1000XM4",
            IsConnected = false,
            BatteryLevel = 80,
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        var secondary = new AppDevice
        {
            Id = "BTHLE_DEV1",
            Name = "LE_WH-1000XM4",
            IsConnected = true,
            BatteryLevel = 80,
            BluetoothAddress = 0xAABBCCDDEEFF
        };

        AppEngine.MergeDualModePair(master, secondary);
        Assert.True(master.IsConnected, "Düğümlerden biri bağlıysa birleşik cihaz bağlı görünmelidir.");
    }

    [Fact]
    public async Task BluetoothBatteryEngine_LiveRefresh_SetsAccurateConnectionState()
    {
        var toast = new AppToast();
        using var engine = new AppEngine(toast);

        await engine.RefreshAllDevicesAsync();
        var devices = engine.CurrentDevices;

        _output.WriteLine($"Engine found {devices.Count} devices:");
        foreach (var d in devices)
        {
            _output.WriteLine($" - {d.Name} (MAC: {d.BluetoothAddress:X12}, Source: {d.ProviderSource}): Connected={d.IsConnected}, Battery={d.EffectiveBatteryLevel}%");
        }

        // Mifa_A20 or Xbox Wireless Controller is turned off on this system and should not appear connected!
        var mifa = devices.FirstOrDefault(d => d.Name.Contains("Mifa", StringComparison.OrdinalIgnoreCase));
        if (mifa != null)
        {
            Assert.False(mifa.IsConnected, $"Mifa_A20 fiilen bağlı olmadığı halde Connected={mifa.IsConnected} görünüyor!");
        }

        var xbox = devices.FirstOrDefault(d => d.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase));
        if (xbox != null)
        {
            Assert.False(xbox.IsConnected, $"Xbox Controller fiilen bağlı olmadığı halde Connected={xbox.IsConnected} görünüyor!");
        }

        // Connected devices must always appear above disconnected devices in the list
        bool seenDisconnected = false;
        foreach (var d in devices)
        {
            if (!d.IsConnected)
            {
                seenDisconnected = true;
            }
            else if (seenDisconnected)
            {
                Assert.Fail($"Bağlı cihaz ({d.Name}), bağlı olmayan cihazların altında listelendi! Sıralama hatalı.");
            }
        }
    }

    [Fact]
    public void BluetoothConnectionSnapshot_NameMatching_ResolvesMacAndStatus()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        ulong k380Mac = 0xF473359EFDC6;
        snapshot.ConnectedByName["Keyboard K380"] = true;
        snapshot.MacByName["Keyboard K380"] = k380Mac;
        snapshot.ClassicConnectedByMac[k380Mac] = true;

        var k380Device = new AppDevice
        {
            Id = "\\\\?\\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b342&Col07#9&3a9385c0&0&0006#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            Name = "Logitech Keyboard K380",
            BluetoothAddress = 0,
            DeviceType = AppDeviceType.Keyboard,
            ProviderSource = "Logitech HID++"
        };

        bool isConn = snapshot.IsConnected(k380Device);
        Assert.True(isConn, "Cihaz adı üzerinden K380 bağlı tespit edilmelidir.");
        Assert.Equal(k380Mac, k380Device.BluetoothAddress);
    }

    [Fact]
    public void UpdateConnectionStatuses_NonPnpProviders_MarksDisconnectedWhenOffline()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        ulong activeMac = 0x112233445566;
        snapshot.ClassicConnectedByMac[activeMac] = true;

        var devices = new List<AppDevice>
        {
            new AppDevice { Id = "HID_01", Name = "Logitech K380", IsConnected = true, ProviderSource = "Logitech HID++", BluetoothAddress = 0x998877665544 },
            new AppDevice { Id = "PS_01", Name = "DualSense", IsConnected = true, ProviderSource = "PlayStation HID (Telemetry)", BluetoothAddress = 0x1234567890AB },
            new AppDevice { Id = "SONY_01", Name = "WH-1000XM4", IsConnected = true, ProviderSource = "Sony Headphones Connect (RFCOMM SPP)", BluetoothAddress = 0xAA2233445566 },
            new AppDevice { Id = "ACTIVE_01", Name = "Active Speaker", IsConnected = false, ProviderSource = "Windows PnP / HFP", BluetoothAddress = activeMac }
        };

        AppChecker.UpdateConnectionStatuses(devices, snapshot);

        Assert.False(devices[0].IsConnected, "Bağlantısı olmayan Logitech aygıtı IsConnected=false olmalıdır.");
        Assert.False(devices[1].IsConnected, "Bağlantısı olmayan PlayStation aygıtı IsConnected=false olmalıdır.");
        Assert.False(devices[2].IsConnected, "Bağlantısı olmayan Sony aygıtı IsConnected=false olmalıdır.");
        Assert.True(devices[3].IsConnected, "Aktif aygıt IsConnected=true olmalıdır.");
    }

    [Fact]
    public void TryGetStatus_KnownDisconnectedMac_NeverOverriddenByNameMatch()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        ulong mifaMac = 0xF44EFD69FA6F;

        // Windows Bluetooth knows this MAC address and confirms it is disconnected
        snapshot.ClassicConnectedByMac[mifaMac] = false;
        snapshot.AepConnectedByMac[mifaMac] = false;
        snapshot.KnownPairedMacs.Add(mifaMac);

        // However, suppose "Mifa_A20" remained true in the name matching table due to another device or cache
        snapshot.ConnectedByName["Mifa_A20"] = true;

        var status = snapshot.TryGetStatus(mifaMac, "BTHENUM_MIFA", "Mifa_A20", AppDeviceType.Speaker);

        // Definite negative: If hardware MAC address is disconnected, name similarity must NEVER make it true!
        Assert.False(status, "Donanım MAC adresi bağlı olmayan cihaz, ConnectedByName eşleşmesi olsa dahi false dönmelidir!");
    }

    [Fact]
    public void UpdateConnectionStatuses_DualModeDevice_ChecksSecondaryMacAddress()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        ulong classicMac = 0x90ECEAF38843; // Classic HFP node (disconnected)
        ulong bleMac = 0x772F4D139DBD;     // BLE GATT node (connected)

        snapshot.ClassicConnectedByMac[classicMac] = false;
        snapshot.AepConnectedByMac[classicMac] = false;
        snapshot.KnownPairedMacs.Add(classicMac);

        snapshot.AepConnectedByMac[bleMac] = true;
        snapshot.KnownPairedMacs.Add(bleMac);

        var phoneDevice = new AppDevice
        {
            Id = "BTHENUM_IPHONE_CLASSIC",
            Name = "kaan- iPhone'u",
            BluetoothAddress = classicMac,
            SecondaryBluetoothAddress = bleMac,
            DeviceType = AppDeviceType.Phone,
            IsConnected = false
        };

        AppChecker.UpdateConnectionStatuses(new[] { phoneDevice }, snapshot);

        Assert.True(phoneDevice.IsConnected, "Dual-mode cihazın BLE adresi bağlıysa birleşik cihaz bağlı kalmalıdır!");
    }

    [Fact]
    public void TryGetStatus_GenericName_NeverMatchesOrPollutes()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();

        // A generic name (e.g., "Bluetooth Device" or "Wireless Controller")
        snapshot.ConnectedByName["Wireless Controller"] = true;

        var disconnectedController = new AppDevice
        {
            Id = "DEV_UNKNOWN_GAMEPAD",
            Name = "Wireless Controller",
            BluetoothAddress = 0,
            DeviceType = AppDeviceType.Gamepad,
            IsConnected = false
        };

        var status = snapshot.TryGetStatus(0, disconnectedController.Id, disconnectedController.Name, disconnectedController.DeviceType);

        // Generic names must never be considered "connected" through ConnectedByName matching
        Assert.False(status, "Generic isimler ConnectedByName üzerinden bağlı gösterilmemelidir!");
    }

    [Fact]
    public void MergeDualModePair_PreservesSecondaryBluetoothAddress()
    {
        var master = new AppDevice
        {
            Id = "BTHENUM_DEV1",
            Name = "WH-1000XM4",
            BluetoothAddress = 0x112233445566,
            DeviceType = AppDeviceType.Headphones,
            IsConnected = false
        };

        var secondary = new AppDevice
        {
            Id = "BTHLE_DEV1",
            Name = "LE_WH-1000XM4",
            BluetoothAddress = 0x998877665544,
            DeviceType = AppDeviceType.Generic,
            IsConnected = true
        };

        AppEngine.MergeDualModePair(master, secondary);

        Assert.Equal((ulong)0x112233445566, master.BluetoothAddress);
        Assert.Equal((ulong)0x998877665544, master.SecondaryBluetoothAddress);
        Assert.True(master.IsConnected);
    }

    [Fact]
    public void CurrentDevices_StableSort_DoesNotChangeOrderByBatteryFluctuations()
    {
        var toast = new AppToast();
        using var engine = new AppEngine(toast);

        var dev1 = new AppDevice { Id = "1", Name = "AirPods Pro", Type = AppDeviceType.Earbuds, IsConnected = true, BatteryLevel = 100, IsTws = true, LeftBatteryLevel = 100, RightBatteryLevel = 100, CaseBatteryLevel = 90 };
        var dev2 = new AppDevice { Id = "2", Name = "iPhone", Type = AppDeviceType.Phone, IsConnected = true, BatteryLevel = 50 };
        var dev3 = new AppDevice { Id = "3", Name = "K380", Type = AppDeviceType.Keyboard, IsConnected = true, BatteryLevel = 80 };

        engine.MergeOrAddDevice(dev1);
        engine.MergeOrAddDevice(dev2);
        engine.MergeOrAddDevice(dev3);

        var list1 = engine.CurrentDevices;
        Assert.Equal("AirPods Pro", list1[0].Name);
        Assert.Equal("iPhone", list1[1].Name);
        Assert.Equal("K380", list1[2].Name);

        // Even if case battery drops to 20% or earbud battery changes, list order must never jump
        dev1.CaseBatteryLevel = 20;
        var list2 = engine.CurrentDevices;
        Assert.Equal("AirPods Pro", list2[0].Name);
        Assert.Equal("iPhone", list2[1].Name);
        Assert.Equal("K380", list2[2].Name);
    }

    [Fact]
    public void ParseAirPodsData_RejectsNearbyInfo0x10Packet_DoesNotProduce10Or30Percent()
    {
        // 0x10 (Nearby Info) packet broadcast by iOS devices (Byte 5 = 0x13 -> was mistaken for 10% and 30%)
        byte[] nearbyPacket = new byte[10];
        nearbyPacket[0] = 0x10; // Nearby Info
        nearbyPacket[1] = 0x08;
        nearbyPacket[2] = 0x00;
        nearbyPacket[3] = 0x55;
        nearbyPacket[4] = 0x20;
        nearbyPacket[5] = 0x13; // 0x1 and 0x3 nibbles
        nearbyPacket[6] = 0x00;

        var result = AppAirPods.ParseAirPodsData(0x112233445566, nearbyPacket);

        Assert.Null(result); // Must be rejected unconditionally and not parsed as AirPods!
    }

    [Fact]
    public void ParseAirPodsData_Valid0x07Packet_Parses100PercentAccurately()
    {
        byte[] validPacket = new byte[27];
        validPacket[0] = 0x07; // Proximity Pairing
        validPacket[1] = 0x19; // 25 bytes
        validPacket[2] = 0x01; // Prefix
        validPacket[3] = 0x0E; // AirPods Pro (0x0E20)
        validPacket[4] = 0x20;
        validPacket[5] = 0x00; // Status (isFlipped = false)
        validPacket[6] = 0xAA; // Pod A = 10 (100%), Pod B = 10 (100%)
        validPacket[7] = 0x09; // Case = 9 (90%), no charging
        validPacket[8] = 0x01; // Lid counter

        var result = AppAirPods.ParseAirPodsData(0x112233445566, validPacket);

        Assert.NotNull(result);
        Assert.Equal("AirPods Pro", result.Name);
        Assert.Equal(100, result.LeftBatteryLevel);
        Assert.Equal(100, result.RightBatteryLevel);
        Assert.Equal(90, result.CaseBatteryLevel);
        Assert.Equal(100, result.BatteryLevel);
    }

    [Fact]
    public void ParseAirPodsData_Smoothing_PreservesValidBatteryWhenPacketHasNA()
    {
        var existing = new AppDevice
        {
            Id = "AirPods_11:22:33:44:55:66",
            Name = "AirPods Pro",
            LeftBatteryLevel = 100,
            RightBatteryLevel = 100,
            CaseBatteryLevel = 80,
            LastUpdated = DateTime.Now
        };

        // Transient packet with 0xFF (15 = N/A) arriving when case lid opens or closes
        byte[] transientPacket = new byte[27];
        transientPacket[0] = 0x07;
        transientPacket[1] = 0x19;
        transientPacket[2] = 0x01;
        transientPacket[3] = 0x0E;
        transientPacket[4] = 0x20;
        transientPacket[5] = 0x00;
        transientPacket[6] = 0xFF; // Both earbuds are 15 (N/A)
        transientPacket[7] = 0x0F; // Case is also 15 (N/A)

        var result = AppAirPods.ParseAirPodsData(0x112233445566, transientPacket, existing);

        Assert.NotNull(result);
        Assert.Equal(100, result.LeftBatteryLevel);
        Assert.Equal(100, result.RightBatteryLevel);
        Assert.Equal(80, result.CaseBatteryLevel);
    }

    [Fact]
    public void ParseAirPodsData_AntiSpike_ProtectsAgainstSudden10PercentDrop()
    {
        var existing = new AppDevice
        {
            Id = "AirPods_11:22:33:44:55:66",
            Name = "AirPods Pro",
            LeftBatteryLevel = 100,
            RightBatteryLevel = 100,
            CaseBatteryLevel = 80,
            LastUpdated = DateTime.Now
        };

        // Abnormal noise packet (sudden drop from 100% to 10%)
        byte[] spikePacket = new byte[27];
        spikePacket[0] = 0x07;
        spikePacket[1] = 0x19;
        spikePacket[2] = 0x01;
        spikePacket[3] = 0x0E;
        spikePacket[4] = 0x20;
        spikePacket[5] = 0x00;
        spikePacket[6] = 0x13; // Pod A = 1 (10%), Pod B = 3 (30%)
        spikePacket[7] = 0x08; // Case = 8 (80%)

        var result = AppAirPods.ParseAirPodsData(0x112233445566, spikePacket, existing);

        Assert.NotNull(result);
        Assert.Equal(100, result.LeftBatteryLevel);
        Assert.Equal(100, result.RightBatteryLevel);
    }

    [Fact]
    public void IsConnected_AirPodsBeacon_WhenInCaseCharging_ReturnsFalse()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();

        var airPods = new AppDevice
        {
            Id = "AirPods_11:22:33:44:55:66",
            Name = "AirPods Pro",
            IsTws = true,
            ProviderSource = "Apple Beacon (BLE)",
            IsLeftCharging = true,
            IsRightCharging = true,
            IsConnected = true // Even if erroneously set to true, should return false because it is charging in the case
        };

        bool isConnected = snapshot.IsConnected(airPods);
        Assert.False(isConnected);
    }

    [Fact]
    public void IsConnected_AirPodsBeacon_WhenActiveInAudio_ReturnsTrue_AndClearsChargingFlags()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        snapshot.ActiveAudioNames.Add("Kulaklıklar (AirPods Pro - Find My)");
        snapshot.ActiveAudioNames.Add("AirPods Pro - Find My");
        snapshot.ActiveAudioNames.Add("AirPods Pro");

        var airPods = new AppDevice
        {
            Id = "AirPods_7E:FA:82:14:61:21",
            Name = "AirPods Pro (2. Nesil)",
            IsTws = true,
            ProviderSource = "Apple Beacon (BLE)",
            IsLeftCharging = true,
            IsRightCharging = true,
            IsConnected = false
        };

        bool isConnected = snapshot.IsConnected(airPods);
        Assert.True(isConnected, "Windows'ta aktif ses akışı olan AirPods bağlı olarak işaretlenmelidir.");
        Assert.False(airPods.IsLeftCharging, "Aktif kullanımda kulaklık şarj bayrağı temizlenmelidir.");
        Assert.False(airPods.IsRightCharging, "Aktif kullanımda kulaklık şarj bayrağı temizlenmelidir.");
    }

    [Fact]
    public void IsConnected_AirPodsBeacon_WhenPairedInWin32_ReturnsTrue()
    {
        var snapshot = new AppChecker.BluetoothConnectionSnapshot();
        ulong classicMac = 0xEC73793D67B2;
        snapshot.MacByName["AirPods Pro"] = classicMac;
        snapshot.ClassicConnectedByMac[classicMac] = true;

        var airPods = new AppDevice
        {
            Id = "AirPods_7E:FA:82:14:61:21",
            Name = "AirPods Pro (2. Nesil)",
            IsTws = true,
            ProviderSource = "Apple Beacon (BLE)",
            IsLeftCharging = true,
            IsRightCharging = true,
            IsConnected = false
        };

        bool isConnected = snapshot.IsConnected(airPods);
        Assert.True(isConnected, "Win32 Classic Bluetooth'ta bağlı olan AirPods bağlı dönmelidir.");
        Assert.Equal(classicMac, airPods.SecondaryBluetoothAddress);
    }
}

