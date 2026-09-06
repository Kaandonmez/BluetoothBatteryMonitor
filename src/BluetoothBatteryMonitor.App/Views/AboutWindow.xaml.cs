using System.Windows;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
