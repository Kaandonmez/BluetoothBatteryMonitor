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
/// Modern Windows 11 Fluent tasarımına sahip, görev çubuğuna yapışık Flyout penceresi.
/// DWM yuvarlatılmış köşeler, gölge, akıcı açılış/kapanış animasyonu ve Deactivated oto-gizleme içerir.
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
        // Kullanıcı pencerenin dışına tıkladığında yumuşak fade-out ile otomatik gizle
        if (IsVisible && !_isAnimating)
        {
            _ = HideWithAnimationAsync();
        }
    }

    /// <summary>
    /// Pencerenin görünürlüğünü açar veya kapatır (Toggle).
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
    /// Pencereyi görev çubuğu konumuna göre hesaplayıp akıcı bir animasyonla açar.
    /// </summary>
    public async Task ShowWithAnimationAsync()
    {
        _isAnimating = true;

        // Önceki animasyon saatlerini temizle
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);

        var pos = TaskbarPositionHelper.CalculateFlyoutPosition(Width, Height);
        Left = pos.X;
        Top = pos.Y + 12; // Hafif aşağıdan yukarı kayma efekti için başlangıç ofseti
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
    /// Zarif bir fade-out ve aşağı kayma ile pencereyi gizler.
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
        // Gerçek uygulama kapanışı değilse pencereyi kapatma, sadece gizle
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
            // Windows 11 Yuvarlatılmış Köşeler (DWMWCP_ROUND = 2)
            int cornerPreference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Windows 11 Dark Mode DWM başlığı (Koyu tema pencere çerçevesi)
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
