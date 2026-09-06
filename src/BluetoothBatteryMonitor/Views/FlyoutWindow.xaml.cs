using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BluetoothBatteryMonitor.Helpers;
using BluetoothBatteryMonitor.ViewModels;

namespace BluetoothBatteryMonitor.Views;

/// <summary>
/// Flyout window docked to the taskbar with modern Windows 11 Fluent design.
/// Includes DWM rounded corners, shadow, fluid open/close animations, and auto-hide on Deactivated.
/// </summary>
public partial class FlyoutWindow : Window
{
    private bool _isRealShutdown;
    private bool _isAnimating;

    public FlyoutWindow(MainFlyoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModernWindowAttributes();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Auto-hide with smooth fade-out when user clicks outside the window
        if (IsVisible && !_isAnimating)
        {
            _ = HideWithAnimationAsync();
        }
    }

    /// <summary>
    /// Toggles the visibility of the window.
    /// </summary>
    public async Task ToggleVisibilityAsync()
    {
        if (IsVisible && Opacity > 0.5)
        {
            await HideWithAnimationAsync();
        }
        else
        {
            await ShowWithAnimationAsync();
        }
    }

    /// <summary>
    /// Calculates position based on taskbar location and displays the window with smooth animation.
    /// </summary>
    public async Task ShowWithAnimationAsync()
    {
        _isAnimating = true;

        // Clear previous animation clocks
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);

        var pos = TaskbarPositionHelper.CalculateFlyoutPosition(Width, Height);
        Left = pos.X;
        Top = pos.Y + 12; // Initial offset for subtle slide-up effect
        Opacity = 0.0;

        Show();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetForegroundWindow(hwnd);
        Activate();

        var fadeAnimation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        var slideAnimation = new DoubleAnimation(Top, pos.Y, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        BeginAnimation(OpacityProperty, fadeAnimation);
        BeginAnimation(TopProperty, slideAnimation);

        await Task.Delay(190);
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);
        Top = pos.Y;
        Opacity = 1.0;
        _isAnimating = false;
    }

    /// <summary>
    /// Hides the window with an elegant fade-out and slide-down animation.
    /// </summary>
    public async Task HideWithAnimationAsync()
    {
        if (!IsVisible) return;
        _isAnimating = true;

        var fadeAnimation = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };

        var slideAnimation = new DoubleAnimation(Top, Top + 8, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };

        BeginAnimation(OpacityProperty, fadeAnimation);
        BeginAnimation(TopProperty, slideAnimation);

        await Task.Delay(135);
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);
        Opacity = 0.0;
        Hide();
        _isAnimating = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Do not close the window unless actual application shutdown; only hide
        if (!_isRealShutdown)
        {
            e.Cancel = true;
            _ = HideWithAnimationAsync();
        }
        base.OnClosing(e);
    }

    public void ClosePermanently()
    {
        _isRealShutdown = true;
        Close();
    }

    private void ApplyModernWindowAttributes()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            // Windows 11 Rounded Corners (DWMWCP_ROUND = 2)
            int cornerPreference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Windows 11 Dark Mode DWM titlebar (Dark theme window border)
            int useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch { }
    }

    #region Win32 DWM Interop
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    #endregion
}
