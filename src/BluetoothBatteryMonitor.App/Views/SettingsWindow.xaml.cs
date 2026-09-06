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
        _viewModel = new SettingsViewModel(knownDevices);
        DataContext = _viewModel;
    }

    private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveCommand.Execute(null);
        Close();
    }
}
