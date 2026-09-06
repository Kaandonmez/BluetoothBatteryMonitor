using System.Collections.Generic;
using System.Windows;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.ViewModels;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(IEnumerable<BluetoothDeviceModel>? knownDevices = null)
    {
        InitializeComponent();
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app.ico", UriKind.Absolute));
        }
        catch { }
        _viewModel = new SettingsViewModel(knownDevices);
        DataContext = _viewModel;
    }

    private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveCommand.Execute(null);
        Close();
    }
}
