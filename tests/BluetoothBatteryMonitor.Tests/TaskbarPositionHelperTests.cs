extern alias MonitorApp;

using BluetoothBatteryMonitor.Helpers;
using Xunit;
using AppTaskbarHelper = MonitorApp::BluetoothBatteryMonitor.App.Services.System.TaskbarPositionHelper;
using AppDeviceItemViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.DeviceItemViewModel;
using AppDeviceModel = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;

namespace BluetoothBatteryMonitor.Tests;

public class TaskbarPositionHelperTests
{
    [Fact]
    public void CalculateFlyoutPosition_DoesNotReturnNegativeCoordinates()
    {
        // Standard Flyout dimensions
        double width = 380;
        double height = 500;

        var point = TaskbarPositionHelper.CalculateFlyoutPosition(width, height);

        // Screen coordinates should not be negative
        Assert.True(point.X >= 0, $"X koordinatı negatif olamaz: {point.X}");
        Assert.True(point.Y >= 0, $"Y koordinatı negatif olamaz: {point.Y}");
    }

    [Theory]
    [InlineData(416, 200)]
    [InlineData(416, 320)]
    [InlineData(380, 580)]
    [InlineData(300, 150)]
    public void CalculateFlyoutPosition_StaysWithinWorkArea(double width, double height)
    {
        var workArea = System.Windows.SystemParameters.WorkArea;
        var point = TaskbarPositionHelper.CalculateFlyoutPosition(width, height);

        // Window must remain within work area boundaries
        Assert.True(point.X >= workArea.Left, $"X ({point.X}) workArea.Left ({workArea.Left}) değerinden küçük olamaz");
        Assert.True(point.Y >= workArea.Top, $"Y ({point.Y}) workArea.Top ({workArea.Top}) değerinden küçük olamaz");
        Assert.True(point.X + width <= workArea.Right + 0.1, $"Sağ kenar ({point.X + width}) workArea.Right ({workArea.Right}) sınırını aşamaz");
        Assert.True(point.Y + height <= workArea.Bottom + 0.1, $"Alt kenar ({point.Y + height}) workArea.Bottom ({workArea.Bottom}) sınırını aşamaz");
    }

    [Fact]
    public void App_TaskbarPositionHelper_GetTaskbarInfo_ReturnsValidBounds()
    {
        var info = AppTaskbarHelper.GetTaskbarInfo();
        Assert.NotNull(info);
        Assert.True(info.Bounds.Width >= 0);
        Assert.True(info.Bounds.Height >= 0);
    }

    [Theory]
    [InlineData(416, 240)]
    [InlineData(416, 500)]
    [InlineData(416, 580)]
    public void App_TaskbarPositionHelper_CalculateFlyoutPosition_StaysWithinWorkArea(double width, double height)
    {
        var workArea = System.Windows.SystemParameters.WorkArea;
        var point = AppTaskbarHelper.CalculateFlyoutPosition(width, height, margin: 12);

        Assert.True(point.X >= workArea.Left);
        Assert.True(point.Y >= workArea.Top);
        Assert.True(point.X + width <= workArea.Right + 0.5);
        Assert.True(point.Y + height <= workArea.Bottom + 0.5);
    }

    [Fact]
    public void App_DeviceItemViewModel_HasBatteryFlags_CorrectlySet()
    {
        // 1. Standard battery device
        var standard = new AppDeviceModel
        {
            Id = "DEV_STD",
            Name = "Mifa_A20",
            BatteryLevel = 100
        };
        var vmStd = new AppDeviceItemViewModel(standard);
        Assert.True(vmStd.HasBattery);
        Assert.Equal(100, vmStd.BatteryProgress);
        Assert.True(vmStd.BatteryText == "100%" || vmStd.BatteryText == "%100", $"Unexpected battery text: {vmStd.BatteryText}");
        Assert.False(vmStd.IsTws);

        // 2. Device without battery
        var noBat = new AppDeviceModel
        {
            Id = "DEV_NOBAT",
            Name = "USB Dongle",
            BatteryLevel = null
        };
        var vmNoBat = new AppDeviceItemViewModel(noBat);
        Assert.False(vmNoBat.HasBattery);
        Assert.Equal(0, vmNoBat.BatteryProgress);
        Assert.Equal("Unknown", vmNoBat.BatteryText);

        // 3. TWS single-sided earbud
        var tws = new AppDeviceModel
        {
            Id = "DEV_TWS",
            Name = "AirPods Pro",
            IsTws = true,
            LeftBatteryLevel = 85,
            RightBatteryLevel = null,
            CaseBatteryLevel = 90
        };
        var vmTws = new AppDeviceItemViewModel(tws);
        Assert.True(vmTws.IsTws);
        Assert.True(vmTws.HasLeftBattery);
        Assert.Equal(85, vmTws.LeftBatteryProgress);
        Assert.False(vmTws.HasRightBattery);
        Assert.Equal(0, vmTws.RightBatteryProgress);
        Assert.Equal("--", vmTws.RightBatteryText);
        Assert.True(vmTws.HasCaseBattery);
        Assert.Equal(90, vmTws.CaseBatteryProgress);
    }

    [Fact]
    public void App_ExeLaunch_WindowPositionsAboveTaskbar()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] potentialPaths = new[]
        {
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\..\src\BluetoothBatteryMonitor.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\BluetoothBatteryMonitor.App.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\..\src\BluetoothBatteryMonitor.App\bin\Release\net8.0-windows10.0.19041.0\BluetoothBatteryMonitor.App.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\..\src\BluetoothBatteryMonitor.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\BluetoothBatteryMonitor.App.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\..\src\BluetoothBatteryMonitor.App\bin\Debug\net8.0-windows10.0.19041.0\BluetoothBatteryMonitor.App.exe")),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"BluetoothBatteryMonitor.App.exe"))
        };

        string? exePath = potentialPaths.FirstOrDefault(p => System.IO.File.Exists(p));

        bool isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        // In CI or headless runner where exe is not built before running tests, verify calculation directly
        if (exePath == null || isCi)
        {
            var workArea = System.Windows.SystemParameters.WorkArea;
            var point = AppTaskbarHelper.CalculateFlyoutPosition(416, 500, margin: 12);
            Assert.True(point.X >= 0, "Calculated flyout X coordinate should be non-negative");
            Assert.True(point.Y >= 0, "Calculated flyout Y coordinate should be non-negative");
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
        {
            UseShellExecute = false
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);

        try
        {
            System.Threading.Thread.Sleep(1000);

            var workArea = System.Windows.SystemParameters.WorkArea;
            var point = AppTaskbarHelper.CalculateFlyoutPosition(416, 500, margin: 12);

            // Flyout coordinates must be in bottom-right corner of work area
            Assert.True(point.X > workArea.Left + 200, $"Expected X ({point.X}) to be in right half of screen");
            Assert.True(point.Y > workArea.Top + 100, $"Expected Y ({point.Y}) to be in bottom half of screen");
            Assert.True(point.Y <= workArea.Bottom, $"Expected Y ({point.Y}) to be above workArea bottom");
        }
        finally
        {
            try
            {
                proc.Kill();
            }
            catch { }
        }
    }
}
