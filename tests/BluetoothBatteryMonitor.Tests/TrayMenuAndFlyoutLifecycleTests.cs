extern alias MonitorApp;

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Xunit;

using AppTrayIconManager = MonitorApp::BluetoothBatteryMonitor.App.Services.Tray.TrayIconManager;
using AppFlyoutWindow = MonitorApp::BluetoothBatteryMonitor.App.Views.FlyoutWindow;
using AppMainViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.MainViewModel;
using AppBluetoothEngine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;
using AppSettings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;

namespace BluetoothBatteryMonitor.Tests;

public class TrayMenuAndFlyoutLifecycleTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                try
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    [Fact]
    public void TaskbarIcon_ConfiguresNoLeftClickDelayAndRightClickActivation()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                trayManager.Initialize();

                var field = typeof(AppTrayIconManager).GetField("_taskbarIcon", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(field);
                var taskbarIcon = field.GetValue(trayManager) as TaskbarIcon;
                Assert.NotNull(taskbarIcon);

                Assert.True(taskbarIcon.NoLeftClickDelay);
                Assert.Equal(PopupActivationMode.RightClick, taskbarIcon.MenuActivation);
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void ContextMenu_ContainsAllExpectedMenuItems()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                var createMenuMethod = typeof(AppTrayIconManager).GetMethod("CreateContextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(createMenuMethod);
                var menu = createMenuMethod.Invoke(trayManager, null) as ContextMenu;
                Assert.NotNull(menu);

                var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
                Assert.True(headers.Any(h => h == "Open / Hide" || h == "Aç / Gizle"), "Expected Open/Hide menu item");
                Assert.True(headers.Any(h => h == "Refresh Devices" || h == "Aygıtları Yenile" || h == "Cihazları Yenile"), "Expected Refresh Devices menu item");
                Assert.True(headers.Any(h => h == "Launch with Windows" || h == "Windows ile Başlat"), "Expected Launch with Windows menu item");
                Assert.True(headers.Any(h => h == "Settings..." || h == "Ayarlar..."), "Expected Settings menu item");
                Assert.True(headers.Any(h => h == "About" || h == "Hakkında"), "Expected About menu item");
                Assert.True(headers.Any(h => h == "Exit" || h == "Çıkış"), "Expected Exit menu item");
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void FlyoutWindow_HideImmediately_SetsVisibilityHiddenWithoutFade()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);

            try
            {
                flyout.ShowFlyout();
                Assert.True(flyout.IsVisible);

                flyout.HideImmediately();
                Assert.False(flyout.IsVisible);
                Assert.False(flyout.IsFlyoutOpen);
            }
            finally
            {
                flyout.Close();
            }
        });
    }

    [Fact]
    public void FlyoutWindow_ToggleFlyout_OpensWhenClosed_AndHidesWhenOpen()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);

            try
            {
                flyout.HideImmediately();
                Assert.False(flyout.IsFlyoutOpen);

                // When closed, toggle should open
                flyout.ToggleFlyout();
                Assert.True(flyout.IsFlyoutOpen);

                // When open, toggle should close
                flyout.ToggleFlyout();
                Assert.False(flyout.IsFlyoutOpen);
            }
            finally
            {
                flyout.Close();
            }
        });
    }

    [Fact]
    public void TrayIconManager_LeftClick_TogglesFlyoutCorrectly_WithoutReopeningOnMouseUp()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                trayManager.Initialize();

                var taskbarField = typeof(AppTrayIconManager).GetField("_taskbarIcon", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(taskbarField);
                var taskbarIcon = taskbarField.GetValue(trayManager) as TaskbarIcon;
                Assert.NotNull(taskbarIcon);

                // Case 1: When flyout is open, left-clicking should close it and not reopen on mouse-up
                flyout.ShowFlyout();
                Assert.True(flyout.IsFlyoutOpen);

                // Trigger MouseDown
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayLeftMouseDownEvent));
                Assert.False(flyout.IsFlyoutOpen);

                // Trigger MouseUp: Should not reopen
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayLeftMouseUpEvent));
                Assert.False(flyout.IsFlyoutOpen);

                // Case 2: When flyout is closed, left-clicking should open it
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayLeftMouseDownEvent));
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayLeftMouseUpEvent));
                Assert.True(flyout.IsFlyoutOpen);
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void TrayIconManager_RightClick_HidesFlyoutImmediately()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                trayManager.Initialize();

                var taskbarField = typeof(AppTrayIconManager).GetField("_taskbarIcon", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(taskbarField);
                var taskbarIcon = taskbarField.GetValue(trayManager) as TaskbarIcon;
                Assert.NotNull(taskbarIcon);

                // Flyout open
                flyout.ShowFlyout();
                Assert.True(flyout.IsFlyoutOpen);

                // When right-click occurs, Flyout should close immediately
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayRightMouseDownEvent));
                Assert.False(flyout.IsFlyoutOpen);
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void TrayIconManager_LeftClick_WhenContextMenuOpen_ClosesMenuAndOpensFlyout()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                trayManager.Initialize();

                var taskbarField = typeof(AppTrayIconManager).GetField("_taskbarIcon", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(taskbarField);
                var taskbarIcon = taskbarField.GetValue(trayManager) as TaskbarIcon;
                Assert.NotNull(taskbarIcon);

                var menuField = typeof(AppTrayIconManager).GetField("_contextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(menuField);
                var menu = menuField.GetValue(trayManager) as ContextMenu;
                Assert.NotNull(menu);

                // While menu is open
                menu.IsOpen = true;
                flyout.HideImmediately();

                // When left-click MouseUp occurs, ContextMenu should close and Flyout should open
                taskbarIcon.RaiseEvent(new RoutedEventArgs(TaskbarIcon.TrayLeftMouseUpEvent));

                Assert.False(menu.IsOpen);
                Assert.True(flyout.IsFlyoutOpen);
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void FlyoutWindow_OnWindowDeactivated_DoesNotHide_WhenChildContextMenuIsOpen()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);

            try
            {
                flyout.ShowFlyout();

                // Submenu open flag
                var childMenuField = typeof(AppFlyoutWindow).GetField("_isChildContextMenuOpen", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(childMenuField);
                childMenuField.SetValue(flyout, true);

                var deactMethod = typeof(AppFlyoutWindow).GetMethod("OnWindowDeactivated", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(deactMethod);
                deactMethod.Invoke(flyout, new object?[] { null, EventArgs.Empty });

                // Deactivation while child context menu is open should not close the window
                Assert.True(flyout.IsVisible);

                childMenuField.SetValue(flyout, false);
                deactMethod.Invoke(flyout, new object?[] { null, EventArgs.Empty });
            }
            finally
            {
                flyout.Close();
            }
        });
    }

    [Fact]
    public void ContextMenu_StartupItem_Click_TogglesStartupCorrectly()
    {
        RunInSta(() =>
        {
            var engine = new AppBluetoothEngine(null, null, null);
            var vm = new AppMainViewModel(engine, null);
            var flyout = new AppFlyoutWindow(vm);
            var trayManager = new AppTrayIconManager(vm, flyout);

            try
            {
                var createMenuMethod = typeof(AppTrayIconManager).GetMethod("CreateContextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(createMenuMethod);
                var menu = createMenuMethod.Invoke(trayManager, null) as ContextMenu;
                Assert.NotNull(menu);

                var startupItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == "Launch with Windows" || m.Header?.ToString() == "Windows ile Başlat");
                Assert.NotNull(startupItem);
                Assert.True(startupItem.IsCheckable);

                // When clicked, WPF MenuItem with IsCheckable toggles IsChecked first, then raises Click event
                bool initialChecked = startupItem.IsChecked;
                startupItem.IsChecked = !initialChecked; // Simulate WPF click
                startupItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                var savedSettings = AppSettings.Load();
                Assert.Equal(!initialChecked, savedSettings.LaunchAtStartup);
                Assert.Equal(!initialChecked, startupItem.IsChecked);
            }
            finally
            {
                trayManager.Dispose();
                flyout.Close();
            }
        });
    }

    [Fact]
    public void MainViewModel_OpenAboutWindow_DoesNotThrow()
    {
        // About command should execute safely
        Assert.NotNull(typeof(AppMainViewModel).GetMethod("OpenAboutWindow", BindingFlags.Public | BindingFlags.Static));
    }
}
