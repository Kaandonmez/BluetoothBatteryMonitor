extern alias MonitorApp;

using System;
using Xunit;
using AppUpdateService = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateCheckService;

namespace BluetoothBatteryMonitor.Tests;

public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.1.0", "1.0.9", 1)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("v1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("0.9.5", "1.0.0", -1)]
    public void CompareVersions_EvaluatesCorrectly(string verA, string verB, int expectedSign)
    {
        int result = AppUpdateService.CompareVersions(verA, verB);
        if (expectedSign > 0)
        {
            Assert.True(result > 0, $"{verA} should be greater than {verB}");
        }
        else if (expectedSign < 0)
        {
            Assert.True(result < 0, $"{verA} should be less than {verB}");
        }
        else
        {
            Assert.Equal(0, result);
        }
    }

    [Fact]
    public void ParseReleaseJson_WhenNewerVersionWithSetup_SelectsInstallerAndFlagsUpdate()
    {
        string sampleJson = @"
        {
            ""tag_name"": ""v1.2.0"",
            ""name"": ""Release v1.2.0: Major Updates"",
            ""html_url"": ""https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/tag/v1.2.0"",
            ""body"": ""- Added new protocol\n- Fixed notifications"",
            ""assets"": [
                {
                    ""name"": ""BluetoothBatteryMonitor.App.exe"",
                    ""browser_download_url"": ""https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/download/v1.2.0/BluetoothBatteryMonitor.App.exe""
                },
                {
                    ""name"": ""BluetoothBatteryMonitor-Setup-v1.2.0.exe"",
                    ""browser_download_url"": ""https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/download/v1.2.0/BluetoothBatteryMonitor-Setup-v1.2.0.exe""
                },
                {
                    ""name"": ""BluetoothBatteryMonitor-win-x64.zip"",
                    ""browser_download_url"": ""https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/download/v1.2.0/BluetoothBatteryMonitor-win-x64.zip""
                }
            ]
        }";

        var result = AppUpdateService.ParseReleaseJson(sampleJson, "1.0.0");

        Assert.True(result.HasUpdate);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/download/v1.2.0/BluetoothBatteryMonitor-Setup-v1.2.0.exe", result.DownloadUrl);
        Assert.Contains("Added new protocol", result.ReleaseNotes);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ParseReleaseJson_WhenSameVersion_ReportsNoUpdate()
    {
        string sampleJson = @"
        {
            ""tag_name"": ""v1.0.0"",
            ""name"": ""Release v1.0.0"",
            ""html_url"": ""https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/tag/v1.0.0"",
            ""body"": ""Initial Release"",
            ""assets"": []
        }";

        var result = AppUpdateService.ParseReleaseJson(sampleJson, "1.0.0");

        Assert.False(result.HasUpdate);
        Assert.Equal("1.0.0", result.LatestVersion);
        Assert.Equal("1.0.0", result.CurrentVersion);
    }

    [Fact]
    public void ParseReleaseJson_WhenInvalidJson_ReturnsErrorGracefully()
    {
        string invalidJson = "{ invalid json content }";

        var result = AppUpdateService.ParseReleaseJson(invalidJson, "1.0.0");

        Assert.False(result.HasUpdate);
        Assert.NotNull(result.ErrorMessage);
    }
}
