using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Bluetooth;
using BluetoothBatteryMonitor.App.Services.Notification;
using BluetoothBatteryMonitor.App.ViewModels;
using BluetoothBatteryMonitor.App.Views;
using Gdi = System.Drawing;
using GdiImaging = System.Drawing.Imaging;

namespace BluetoothBatteryMonitor.App.Services;

public static class ScreenshotCaptureService
{
    public static void RunAndExit()
    {
        try
        {
            Console.WriteLine("[ScreenshotCapture] Starting screenshot capture process...");
            if (Application.Current != null)
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            System.ThemeManager.Initialize();
            BluetoothBatteryMonitor.App.Services.Localization.LocalizationService.ApplyLanguage("en");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);
            string outputDir = projectRoot != null
                ? Path.Combine(projectRoot, "docs", "screenshots")
                : Path.Combine(baseDir, "docs", "screenshots");

            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"[ScreenshotCapture] Target directory: {outputDir}");

            var sampleDevices = CreateSampleDevices();

            // 1. FlyoutWindow
            CaptureFlyoutWindow(sampleDevices, outputDir);

            // 2. SettingsWindow
            CaptureSettingsWindow(sampleDevices, outputDir);

            // 3. AboutWindow
            CaptureAboutWindow(outputDir);

            Console.WriteLine("[ScreenshotCapture] All screenshots captured successfully!");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ScreenshotCapture] Fatal error during capture: {ex}");
        }
        finally
        {
            Application.Current?.Shutdown(0);
        }
    }

    private static List<BluetoothDeviceModel> CreateSampleDevices()
    {
        return new List<BluetoothDeviceModel>
        {
            new BluetoothDeviceModel
            {
                Id = "BTHENUM\\Dev_F84E17D391A1",
                Name = "Sony WH-1000XM5",
                DeviceType = DeviceType.Headphones,
                BatteryLevel = 82,
                IsCharging = false,
                IsConnected = true,
                AudioCodec = "LDAC",
                ProviderSource = "Windows BLE GATT"
            },
            new BluetoothDeviceModel
            {
                Id = "BTHENUM\\Dev_4C79BAE12004",
                Name = "AirPods Pro (2nd Gen)",
                DeviceType = DeviceType.Earbuds,
                IsTws = true,
                LeftBatteryLevel = 90,
                IsLeftCharging = false,
                RightBatteryLevel = 85,
                IsRightCharging = false,
                CaseBatteryLevel = 75,
                IsCaseCharging = true,
                IsConnected = true,
                AudioCodec = "AAC",
                ProviderSource = "Apple BLE Beacon"
            },
            new BluetoothDeviceModel
            {
                Id = "HID\\VID_046D&PID_B023",
                Name = "Logitech MX Master 3S",
                DeviceType = DeviceType.Mouse,
                BatteryLevel = 60,
                IsCharging = false,
                IsConnected = true,
                ProviderSource = "Logitech HID++ (Bolt)"
            },
            new BluetoothDeviceModel
            {
                Id = "BTHENUM\\Dev_0017FA92C3D1",
                Name = "Xbox Wireless Controller",
                DeviceType = DeviceType.Gamepad,
                BatteryLevel = 20,
                IsCharging = false,
                IsConnected = true,
                ProviderSource = "Windows Gaming Input"
            }
        };
    }

    private static void CaptureFlyoutWindow(List<BluetoothDeviceModel> sampleDevices, string outputDir)
    {
        Console.WriteLine("[ScreenshotCapture] Capturing FlyoutWindow...");

        var dummyNotification = new ToastNotificationService();
        var dummyEngine = new BluetoothBatteryEngine(dummyNotification);
        var mainVm = new MainViewModel(dummyEngine);

        foreach (var dev in sampleDevices)
        {
            var itemVm = new DeviceItemViewModel(dev);
            if (dev.Id == "BTHENUM\\Dev_F84E17D391A1")
            {
                itemVm.HasAudioEndpoint = true;
                itemVm.AudioEndpointId = "mock_ep_sony";
                itemVm.VolumePercent = 74;
                itemVm.IsMuted = false;
                itemVm.AudioCodec = "LDAC";
                itemVm.CustomThreshold = 20;
            }
            else if (dev.Id == "BTHENUM\\Dev_4C79BAE12004")
            {
                itemVm.HasAudioEndpoint = true;
                itemVm.AudioEndpointId = "mock_ep_airpods";
                itemVm.VolumePercent = 58;
                itemVm.IsMuted = false;
                itemVm.AudioCodec = "AAC";
            }
            else if (dev.Id == "BTHENUM\\Dev_0017FA92C3D1")
            {
                itemVm.CustomThreshold = 25;
            }
            mainVm.Devices.Add(itemVm);
        }

        mainVm.LowestBatteryLevel = 20;
        mainVm.IsLowestCharging = false;
        mainVm.HasDevices = true;
        mainVm.StatusText = "4 devices connected • Synced";

        var flyout = new FlyoutWindow(mainVm)
        {
            DisableAutoClose = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Opacity = 1.0
        };

        flyout.Show();
        flyout.Activate();
        flyout.Topmost = true;

        PumpWpfEvents(400);

        string pngPath = Path.Combine(outputDir, "flyout_preview.png");
        string jpgPath = Path.Combine(outputDir, "flyout_preview.jpg");

        SaveVisualToPng(flyout, pngPath, fillDarkBackground: true);
        SaveBitmapAsJpeg(pngPath, jpgPath);

        flyout.Close();
        PumpWpfEvents(100);
    }

    private static void CaptureSettingsWindow(List<BluetoothDeviceModel> sampleDevices, string outputDir)
    {
        Console.WriteLine("[ScreenshotCapture] Capturing SettingsWindow...");

        var settingsWindow = new SettingsWindow(sampleDevices)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Height = 720
        };
        settingsWindow.Background = (global::System.Windows.Media.Brush)settingsWindow.FindResource("ApplicationBackgroundBrush");

        if (settingsWindow.DataContext is SettingsViewModel vm)
        {
            vm.SelectedLanguage = "en";
            vm.SelectedTheme = "Dark";
            vm.LaunchAtStartup = true;
            foreach (var dt in vm.DeviceThresholds)
            {
                if (dt.Name.Contains("Sony", StringComparison.OrdinalIgnoreCase))
                    dt.SelectedThreshold = 20;
                else if (dt.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase))
                    dt.SelectedThreshold = 15;
                else if (dt.Name.Contains("Logitech", StringComparison.OrdinalIgnoreCase) || dt.Name.Contains("Master", StringComparison.OrdinalIgnoreCase))
                    dt.SelectedThreshold = 10;
                else if (dt.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
                    dt.SelectedThreshold = 25;
            }
        }

        settingsWindow.Show();
        settingsWindow.Activate();
        settingsWindow.Topmost = true;

        PumpWpfEvents(400);

        string pngPath = Path.Combine(outputDir, "settings_preview.png");
        string jpgPath = Path.Combine(outputDir, "settings_preview.jpg");

        SaveVisualToPng(settingsWindow, pngPath, fillDarkBackground: true);
        SaveBitmapAsJpeg(pngPath, jpgPath);

        settingsWindow.Close();
        PumpWpfEvents(100);
    }

    private static void CaptureAboutWindow(string outputDir)
    {
        Console.WriteLine("[ScreenshotCapture] Capturing AboutWindow...");

        var aboutWindow = new AboutWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        aboutWindow.Background = (global::System.Windows.Media.Brush)aboutWindow.FindResource("ApplicationBackgroundBrush");

        aboutWindow.Show();
        aboutWindow.Activate();
        aboutWindow.Topmost = true;

        PumpWpfEvents(400);

        string pngPath = Path.Combine(outputDir, "about_preview.png");
        string jpgPath = Path.Combine(outputDir, "about_preview.jpg");

        SaveVisualToPng(aboutWindow, pngPath, fillDarkBackground: true);
        SaveBitmapAsJpeg(pngPath, jpgPath);

        aboutWindow.Close();
        PumpWpfEvents(100);
    }

    private static void PumpWpfEvents(int waitMs)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(waitMs)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void SaveVisualToPng(FrameworkElement element, string filePath, bool fillDarkBackground = false)
    {
        element.UpdateLayout();
        int width = (int)Math.Ceiling(element.ActualWidth > 0 ? element.ActualWidth : element.Width);
        int height = (int)Math.Ceiling(element.ActualHeight > 0 ? element.ActualHeight : element.Height);
        if (width <= 0) width = 500;
        if (height <= 0) height = 650;

        int margin = fillDarkBackground ? 16 : 0;
        int totalWidth = width + (margin * 2);
        int totalHeight = height + (margin * 2);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (fillDarkBackground)
            {
                // Windows 11 yumuşak pencere gölgesi çizimi (multi-pass shadow)
                for (int i = 10; i >= 1; i--)
                {
                    var shadowRect = new Rect(margin - i, margin - i + 3, width + (i * 2), height + (i * 2));
                    byte alpha = (byte)(16 - i);
                    var shadowBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                    dc.DrawRoundedRectangle(shadowBrush, null, shadowRect, 10 + i, 10 + i);
                }

                // Pencere arka planı ve 1px zarif Fluent kenarlık
                var darkBrush = new SolidColorBrush(Color.FromRgb(32, 32, 36));
                var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
                dc.DrawRoundedRectangle(darkBrush, borderPen, new Rect(margin, margin, width, height), 10, 10);
            }

            dc.DrawRectangle(new VisualBrush(element), null, new Rect(margin, margin, width, height));
        }

        var rtb = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    private static void SaveBitmapAsJpeg(string pngPath, string jpgPath)
    {
        using var srcBmp = new Gdi.Bitmap(pngPath);
        using var dstBmp = new Gdi.Bitmap(srcBmp.Width, srcBmp.Height, GdiImaging.PixelFormat.Format32bppRgb);
        using (var g = Gdi.Graphics.FromImage(dstBmp))
        {
            using var brush = new Gdi.Drawing2D.LinearGradientBrush(
                new Gdi.Point(0, 0),
                new Gdi.Point(0, srcBmp.Height),
                Gdi.Color.FromArgb(28, 30, 36),
                Gdi.Color.FromArgb(16, 18, 22));
            g.FillRectangle(brush, 0, 0, srcBmp.Width, srcBmp.Height);
            g.DrawImage(srcBmp, 0, 0);
        }

        var encoder = GdiImaging.ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == GdiImaging.ImageFormat.Jpeg.Guid);
        if (encoder != null)
        {
            var encParams = new GdiImaging.EncoderParameters(1);
            encParams.Param[0] = new GdiImaging.EncoderParameter(GdiImaging.Encoder.Quality, 96L);
            dstBmp.Save(jpgPath, encoder, encParams);
        }
        else
        {
            dstBmp.Save(jpgPath, GdiImaging.ImageFormat.Jpeg);
        }
    }


    private static string? FindProjectRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BluetoothBatteryMonitor.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
