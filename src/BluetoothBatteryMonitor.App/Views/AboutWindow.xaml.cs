using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using BluetoothBatteryMonitor.App.Services.Localization;
using BluetoothBatteryMonitor.App.Services.Update;

namespace BluetoothBatteryMonitor.App.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app.ico", UriKind.Absolute));
        }
        catch { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = LocalizationService.GetString("Update_Checking");
            try
            {
                var service = new UpdateCheckService();
                var result = await service.CheckForUpdatesAsync();
                if (result.HasUpdate)
                {
                    btn.Content = LocalizationService.GetString("Update_Available", result.LatestVersion);
                    UpdateCheckService.OpenUrl(result.DownloadUrl ?? result.ReleaseUrl ?? "https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/latest");
                }
                else if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    btn.Content = LocalizationService.GetString("Update_Error");
                }
                else
                {
                    btn.Content = LocalizationService.GetString("Update_UpToDate", result.CurrentVersion);
                }
            }
            catch
            {
                btn.Content = LocalizationService.GetString("Update_Error");
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
