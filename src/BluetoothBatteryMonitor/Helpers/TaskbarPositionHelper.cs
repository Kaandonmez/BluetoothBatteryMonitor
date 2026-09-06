using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;

namespace BluetoothBatteryMonitor.Helpers;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right
}

/// <summary>
/// Detects the screen position of the taskbar and system tray (Notification Area)
/// to position the Flyout window docked and aligned with the taskbar in a modern fashion.
/// </summary>
public static class TaskbarPositionHelper
{
    private const int FlyoutMargin = 12; // Distance margin from taskbar

    /// <summary>
    /// Detects the edge orientation of the taskbar on screen (Bottom, Top, Left, Right).
    /// </summary>
    public static TaskbarEdge GetTaskbarEdge()
    {
        var workArea = SystemParameters.WorkArea;
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        if (workArea.Left > 0)
        {
            return TaskbarEdge.Left;
        }
        if (workArea.Top > 0)
        {
            return TaskbarEdge.Top;
        }
        if (workArea.Right < screenWidth)
        {
            return TaskbarEdge.Right;
        }

        // Default Windows 11 and 10 position
        return TaskbarEdge.Bottom;
    }

    /// <summary>
    /// Calculates the optimal screen coordinate (X, Y) based on given Flyout window width and height.
    /// </summary>
    public static Point CalculateFlyoutPosition(double windowWidth, double windowHeight)
    {
        var workArea = SystemParameters.WorkArea;
        var edge = GetTaskbarEdge();

        double x;
        double y;

        switch (edge)
        {
            case TaskbarEdge.Top:
                x = workArea.Right - windowWidth - FlyoutMargin;
                y = workArea.Top + FlyoutMargin;
                break;

            case TaskbarEdge.Left:
                x = workArea.Left + FlyoutMargin;
                y = workArea.Bottom - windowHeight - FlyoutMargin;
                break;

            case TaskbarEdge.Right:
                x = workArea.Right - windowWidth - FlyoutMargin;
                y = workArea.Bottom - windowHeight - FlyoutMargin;
                break;

            case TaskbarEdge.Bottom:
            default:
                // Bottom-right corner (above notification area)
                x = workArea.Right - windowWidth - FlyoutMargin;
                y = workArea.Bottom - windowHeight - FlyoutMargin;
                break;
        }

        // Safety bounds check to prevent overflowing outside the screen
        x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - windowWidth));
        y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - windowHeight));

        return new Point(x, y);
    }
}
