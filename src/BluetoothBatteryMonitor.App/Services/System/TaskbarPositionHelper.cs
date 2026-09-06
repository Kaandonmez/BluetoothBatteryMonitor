using System;
using System.Runtime.InteropServices;
using System.Windows;
using BluetoothBatteryMonitor.App.Helpers;

namespace BluetoothBatteryMonitor.App.Services.System;

public enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom
}

public record TaskbarInfo(TaskbarEdge Edge, Rect Bounds);

public static class TaskbarPositionHelper
{
    public static TaskbarInfo GetTaskbarInfo()
    {
        var workArea = SystemParameters.WorkArea;
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        var appbarData = new NativeMethods.APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.APPBARDATA))
        };

        IntPtr result = NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref appbarData);
        TaskbarEdge edge;

        if (result != IntPtr.Zero)
        {
            edge = appbarData.uEdge switch
            {
                NativeMethods.ABE_LEFT => TaskbarEdge.Left,
                NativeMethods.ABE_TOP => TaskbarEdge.Top,
                NativeMethods.ABE_RIGHT => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom
            };
        }
        else
        {
            // SHAppBarMessage başarısız olursa WorkArea üzerinden tespit et
            if (workArea.Top > 0) edge = TaskbarEdge.Top;
            else if (workArea.Left > 0) edge = TaskbarEdge.Left;
            else if (workArea.Right < screenWidth) edge = TaskbarEdge.Right;
            else edge = TaskbarEdge.Bottom;
        }

        // Görev çubuğu sınırlarını tamamen WPF DIP (Device Independent Pixels) cinsinden hesapla
        Rect bounds = edge switch
        {
            TaskbarEdge.Top => new Rect(0, 0, screenWidth, Math.Max(0, workArea.Top)),
            TaskbarEdge.Left => new Rect(0, 0, Math.Max(0, workArea.Left), screenHeight),
            TaskbarEdge.Right => new Rect(workArea.Right, 0, Math.Max(0, screenWidth - workArea.Right), screenHeight),
            TaskbarEdge.Bottom or _ => new Rect(0, workArea.Bottom, screenWidth, Math.Max(0, screenHeight - workArea.Bottom))
        };

        return new TaskbarInfo(edge, bounds);
    }

    /// <summary>
    /// Flyout penceresinin görev çubuğunun yanına yerleşeceği sol-üst DIP koordinatlarını hesaplar.
    /// Tüm hesaplamalar WPF DIP birimindedir ve ekran ölçekleme (DPI scaling) ile tam uyumludur.
    /// </summary>
    public static Point CalculateFlyoutPosition(double windowWidth, double windowHeight, double margin = 12)
    {
        var taskbar = GetTaskbarInfo();
        var workArea = SystemParameters.WorkArea;

        double left;
        double top;

        switch (taskbar.Edge)
        {
            case TaskbarEdge.Top:
                left = workArea.Right - windowWidth - margin;
                top = workArea.Top + margin;
                break;

            case TaskbarEdge.Left:
                left = workArea.Left + margin;
                top = workArea.Bottom - windowHeight - margin;
                break;

            case TaskbarEdge.Right:
                left = workArea.Right - windowWidth - margin;
                top = workArea.Bottom - windowHeight - margin;
                break;

            case TaskbarEdge.Bottom:
            default:
                left = workArea.Right - windowWidth - margin;
                top = workArea.Bottom - windowHeight - margin;
                break;
        }

        // Ekran sınırlarının dışına taşmasını engelle
        left = Math.Max(workArea.Left + margin, Math.Min(left, workArea.Right - windowWidth - margin));
        top = Math.Max(workArea.Top + margin, Math.Min(top, workArea.Bottom - windowHeight - margin));

        return new Point(left, top);
    }
}
