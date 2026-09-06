extern alias MonitorApp;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using AppPnp = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.WindowsPnpBatteryProvider;
using AppLogitech = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.LogitechHidBatteryProvider;
using AppEngine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;
using AppToast = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;

namespace BluetoothBatteryMonitor.Tests;

public class AppProvidersLiveTests
{
    private readonly ITestOutputHelper _output;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(
        Microsoft.Win32.SafeHandles.SafeFileHandle HidDeviceObject,
        [System.Runtime.InteropServices.In] byte[] ReportBuffer,
        uint ReportBufferLength);

    public AppProvidersLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task WindowsPnpBatteryProvider_DiscoversDevices()
    {
        var provider = new AppPnp();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var devices = await provider.GetDevicesAsync(cts.Token);

        _output.WriteLine($"WindowsPnpBatteryProvider found {devices.Count} devices:");
        foreach (var d in devices)
        {
            _output.WriteLine($" - {d.Name} ({d.Id}): Battery={d.BatteryLevel}%, Connected={d.IsConnected}, Type={d.DeviceType}");
        }

        // We know Xbox Wireless Controller or Mifa_A20 or iPhone may be on the system
        Assert.NotNull(devices);
    }

    [Fact]
    public async Task LogitechHidBatteryProvider_DiscoversDevices()
    {
        string hidSelector = "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"";
        var hidDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(hidSelector);
        _output.WriteLine($"Total HID devices in system: {hidDevices.Count}");

        var logitechHidList = hidDevices.Where(d =>
            d.Id.Contains("VID_046D", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("K380", StringComparison.OrdinalIgnoreCase)).ToList();

        _output.WriteLine($"Filtered Logitech HID devices: {logitechHidList.Count}");
        foreach (var d in logitechHidList)
        {
            _output.WriteLine($" - Name: '{d.Name}', Id: '{d.Id}'");
        }

        var provider = new AppLogitech();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var devices = await provider.GetDevicesAsync(cts.Token);

        _output.WriteLine($"LogitechHidBatteryProvider found {devices.Count} devices:");
        foreach (var d in devices)
        {
            _output.WriteLine($" - {d.Name} ({d.Id}): Battery={d.BatteryLevel}%, Connected={d.IsConnected}, Type={d.DeviceType}");
        }

        Assert.NotNull(devices);
        if (logitechHidList.Count > 0)
        {
            Assert.NotEmpty(devices);
        }
    }

    [Fact]
    public async Task BluetoothBatteryEngine_RefreshAllDevices_RunsWithoutCrashing()
    {
        var toast = new AppToast();
        using var engine = new AppEngine(toast);

        await engine.RefreshAllDevicesAsync();

        var devices = engine.CurrentDevices;
        _output.WriteLine($"BluetoothBatteryEngine found {devices.Count} devices:");
        foreach (var d in devices)
        {
            _output.WriteLine($" - {d.Name} (Source: {d.ProviderSource}): Battery={d.BatteryLevel}% / Effective={d.EffectiveBatteryLevel}%, Connected={d.IsConnected}, IsTws={d.IsTws}");
        }

        Assert.NotNull(devices);
    }

    [Fact]
    public async Task BleGattBatteryProvider_DiscoversDevices()
    {
        var provider = new MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BleGattBatteryProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var devices = await provider.GetDevicesAsync(cts.Token);

        _output.WriteLine($"BleGattBatteryProvider found {devices.Count} devices:");
        foreach (var d in devices)
        {
            _output.WriteLine($" - {d.Name} ({d.Id}): Battery={d.BatteryLevel}%, Connected={d.IsConnected}, Type={d.DeviceType}");
        }
    }

    [Fact]
    public void Test_Matching_Mifa_vs_Other()
    {
        var mifa = new MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel
        {
            Id = @"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&0002099a_PID&0500\8&3a358462&0&F44EFD69FA6F_C00000000",
            Name = "Mifa_A20",
            BatteryLevel = 100,
            DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Speaker
        };

        var iphoneHf = new MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel
        {
            Id = @"BTHENUM\{0000111f-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&7613\8&3a358462&0&90ECEAF38843_C00000000",
            Name = "kaan- iPhone’u HF",
            BatteryLevel = 60,
            DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Generic
        };

        var iphoneBle = new MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel
        {
            Id = @"BTHLE\Dev_772f4d139dbd\8&29af66f1&0&772f4d139dbd",
            Name = "kaan- iPhone’u",
            BatteryLevel = 100,
            DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Generic
        };

        var xbox = new MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel
        {
            Id = @"BTHLE\Dev_c83f26a3ce5f\8&29af66f1&0&c83f26a3ce5f",
            Name = "Xbox Wireless Controller",
            BatteryLevel = 90,
            DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Gamepad
        };

        _output.WriteLine($"mifa matches iphoneHf: {mifa.Matches(iphoneHf)}");
        _output.WriteLine($"mifa matches iphoneBle: {mifa.Matches(iphoneBle)}");
        _output.WriteLine($"mifa matches xbox: {mifa.Matches(xbox)}");
        _output.WriteLine($"iphoneHf matches iphoneBle: {iphoneHf.Matches(iphoneBle)}");

        Assert.False(mifa.Matches(iphoneHf));
        Assert.False(mifa.Matches(iphoneBle));
        Assert.False(mifa.Matches(xbox));
        Assert.False(iphoneHf.Matches(xbox));
        Assert.True(iphoneHf.Matches(iphoneBle));
        Assert.True(iphoneBle.Matches(iphoneHf));
    }

    [Fact]
    public async Task Snapshot_LiveDevices_MatchesHardwareState()
    {
        var snapshot = await MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothConnectionChecker.CaptureSnapshotAsync();
        Assert.NotNull(snapshot);

        // Mifa_A20 is a known powered-off device
        ulong mifaMac = 0xF44EFD69FA6F;
        if (snapshot.KnownPairedMacs.Contains(mifaMac) || snapshot.ClassicConnectedByMac.ContainsKey(mifaMac))
        {
            var mifaStatus = snapshot.TryGetStatus(mifaMac, null, "Mifa_A20", MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Speaker);
            Assert.False(mifaStatus, "Kapalı olan Mifa_A20 bağlı görünmemelidir.");
        }

        // Xbox Wireless Controller is a known powered-off device
        ulong xboxMac = 0xC83F26A3CE5F;
        if (snapshot.KnownPairedMacs.Contains(xboxMac) || snapshot.AepConnectedByMac.ContainsKey(xboxMac))
        {
            var xboxStatus = snapshot.TryGetStatus(xboxMac, null, "Xbox Wireless Controller", MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Gamepad);
            Assert.False(xboxStatus, "Kapalı olan Xbox Wireless Controller bağlı görünmemelidir.");
        }
    }

    [Fact]
    public async Task InspectLiveSnapshot()
    {
        var audioManager = new MonitorApp::BluetoothBatteryMonitor.App.Services.Audio.AudioEndpointManager();
        var endpoints = audioManager.GetPlaybackEndpoints();
        _output.WriteLine($"Endpoints count: {endpoints.Count}");
        foreach (var ep in endpoints)
        {
            _output.WriteLine($"EP: Name='{ep.Name}', Id='{ep.Id}', Desc='{ep.Description}', DevInst='{ep.DeviceInstanceId}', IsDef={ep.IsDefaultPlayback}");
        }

        var snapshot = await MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothConnectionChecker.CaptureSnapshotAsync(audioManager);
        _output.WriteLine($"ActiveAudioNames: {string.Join(", ", snapshot.ActiveAudioNames)}");
        _output.WriteLine($"ConnectedByName: {string.Join(", ", snapshot.ConnectedByName.Select(kv => $"{kv.Key}={kv.Value}"))}");
        _output.WriteLine($"MacByName: {string.Join(", ", snapshot.MacByName.Select(kv => $"{kv.Key}=0x{kv.Value:X12}"))}");
        _output.WriteLine($"ClassicConnectedByMac: {string.Join(", ", snapshot.ClassicConnectedByMac.Select(kv => $"0x{kv.Key:X12}={kv.Value}"))}");
        _output.WriteLine($"AepConnectedByMac: {string.Join(", ", snapshot.AepConnectedByMac.Select(kv => $"0x{kv.Key:X12}={kv.Value}"))}");
    }
}
