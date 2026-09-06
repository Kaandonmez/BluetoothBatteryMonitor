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
/// Görev çubuğunun (Taskbar) ve sistem tepsisinin (Notification Area) ekran konumunu
/// tespit ederek Flyout penceresini tam görev çubuğuna yapışık ve modern şekilde konumlandırır.
/// </summary>
public static class TaskbarPositionHelper
{
    private const int FlyoutMargin = 12; // Görev çubuğundan uzaklık boşluğu

    /// <summary>
    /// Ekrandaki görev çubuğunun yönünü tespit eder (Alt, Üst, Sol, Sağ).
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

        // Varsayılan Windows 11 ve 10 konumu
        return TaskbarEdge.Bottom;
    }

    /// <summary>
    /// Verilen Flyout pencere genişlik ve yüksekliğine göre en uygun ekran koordinatını (X, Y) hesaplar.
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
                // Sağ alt köşe (Bildirim alanı üzeri)
                x = workArea.Right - windowWidth - FlyoutMargin;
                y = workArea.Bottom - windowHeight - FlyoutMargin;
                break;
        }

        // Ekran dışına taşmayı önleyici güvenlik kontrolleri
        x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - windowWidth));
        y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - windowHeight));

        return new Point(x, y);
    }
}
