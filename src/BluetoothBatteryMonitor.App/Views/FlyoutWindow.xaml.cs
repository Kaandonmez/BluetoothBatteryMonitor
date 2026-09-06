using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.ViewModels;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Views;

public partial class FlyoutWindow : FluentWindow
{
    private bool _isOpening;
    private bool _isHiding;
    private int _animationGeneration;
    private bool _hasRendered;
    private bool _isChildContextMenuOpen;
    private DateTime _lastDeactivatedTime = DateTime.MinValue;

    public bool IsFlyoutOpen => Visibility == Visibility.Visible && !_isHiding;
    public DateTime LastDeactivatedTime => _lastDeactivatedTime;
    public bool DisableAutoClose { get; set; }

    public FlyoutWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app.ico", UriKind.Absolute));
        }
        catch { }

        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        SizeChanged += OnSizeChanged;

        // Track opening and closing of child context menus (e.g. device card threshold menu)
        AddHandler(System.Windows.Controls.ContextMenuService.ContextMenuOpeningEvent,
            new System.Windows.Controls.ContextMenuEventHandler((s, e) => _isChildContextMenuOpen = true));
        AddHandler(System.Windows.Controls.ContextMenuService.ContextMenuClosingEvent,
            new System.Windows.Controls.ContextMenuEventHandler((s, e) =>
            {
                _isChildContextMenuOpen = false;
                // When child menu closes, hide flyout if it lost focus (clicked outside)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    if (!_isChildContextMenuOpen && !IsActive && !IsKeyboardFocusWithin)
                    {
                        HideFlyout();
                    }
                }));
            }));

        // Support instant dismissal with Escape key
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                HideFlyout();
                e.Handled = true;
            }
        };

        // Pre-calculate window position before first render (avoid CW_USEDEFAULT)
        RepositionFlyout();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModernWindowStyling();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _hasRendered = true;
        if (IsVisible)
        {
            RepositionFlyout();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVisible && Visibility == Visibility.Visible)
        {
            RepositionFlyout();
        }
    }

    private void ApplyModernWindowStyling()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero) return;

        // Enforce Windows 11 rounded corners via DWM
        int preference = (int)NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            helper.Handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
    }

    /// <summary>
    /// Repositions the flyout anchored cleanly adjacent to the taskbar.
    /// </summary>
    public void RepositionFlyout()
    {
        double w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 416);
        double h = ActualHeight;

        if (h <= 0)
        {
            if (Content is UIElement content)
            {
                try
                {
                    content.Measure(new Size(w, double.PositiveInfinity));
                    h = content.DesiredSize.Height > 0 ? content.DesiredSize.Height : 320;
                }
                catch
                {
                    h = 320;
                }
            }
            else
            {
                h = 320;
            }
        }

        var pos = TaskbarPositionHelper.CalculateFlyoutPosition(w, h, margin: 12);
        Left = pos.X;
        Top = pos.Y;
    }

    public void ShowFlyout()
    {
        if (IsVisible && Opacity > 0.95 && !_isOpening && !_isHiding)
        {
            BringToFront();
            return;
        }

        _isHiding = false;
        _isOpening = true;
        int currentGen = ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);

        // Pre-calculate window coordinates
        RepositionFlyout();

        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }
        else
        {
            Visibility = Visibility.Visible;
        }

        DeviceScrollViewer?.ScrollToTop();
        (DataContext as MainViewModel)?.RefreshAudioVolumes();

        if (!_hasRendered || ActualHeight <= 0)
        {
            // On initial show, position and display after layout finishes measuring
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                RepositionFlyout();
                Activate();
                BringToFront();
                Focus();
                AnimateFadeIn(currentGen);
            }));
        }
        else
        {
            UpdateLayout();
            RepositionFlyout();
            Activate();
            BringToFront();
            Focus();
            AnimateFadeIn(currentGen);
        }
    }

    private void BringToFront()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(helper.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetForegroundWindow(helper.Handle);
        }
    }

    private void AnimateFadeIn(int currentGen)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (s, e) =>
        {
            if (_animationGeneration == currentGen)
            {
                _isOpening = false;
            }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HideFlyout()
    {
        if (Visibility != Visibility.Visible || _isHiding) return;

        _isHiding = true;
        _isOpening = false;
        int currentGen = ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation(Opacity > 0 ? Opacity : 1.0, 0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            if (_animationGeneration == currentGen)
            {
                Hide();
                Opacity = 1;
                _isHiding = false;
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Hides the flyout instantly without animation (used when context menu opens or tray exit clicked).
    /// </summary>
    public void HideImmediately()
    {
        _isOpening = false;
        _isHiding = false;
        ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
        Opacity = 1;
    }

    public void ToggleFlyout()
    {
        if (IsFlyoutOpen)
        {
            HideFlyout();
        }
        else
        {
            ShowFlyout();
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (DisableAutoClose) return;

        _lastDeactivatedTime = DateTime.UtcNow;

        // Do not dismiss flyout if a child context menu is currently active
        if (_isChildContextMenuOpen)
        {
            return;
        }

        HideFlyout();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        HideFlyout();
    }
}
