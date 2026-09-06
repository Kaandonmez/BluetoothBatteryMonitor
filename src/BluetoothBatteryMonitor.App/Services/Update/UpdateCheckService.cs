using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothBatteryMonitor.App.Services.Update;

public record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseNotes,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? ErrorMessage = null);

public class UpdateCheckService
{
    private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/Kaandonmez/BluetoothBatteryMonitor/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateCheckService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BluetoothBatteryMonitor-DesktopApp");
        }
    }

    public static string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVerStr = GetCurrentVersion();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiLatestReleaseUrl);
            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    HasUpdate: false,
                    CurrentVersion: currentVerStr,
                    LatestVersion: currentVerStr,
                    ReleaseNotes: null,
                    ReleaseUrl: null,
                    DownloadUrl: null,
                    ErrorMessage: $"GitHub API returned {res.StatusCode}");
            }

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReleaseJson(json, currentVerStr);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                HasUpdate: false,
                CurrentVersion: currentVerStr,
                LatestVersion: currentVerStr,
                ReleaseNotes: null,
                ReleaseUrl: null,
                DownloadUrl: null,
                ErrorMessage: ex.Message);
        }
    }

    public static UpdateCheckResult ParseReleaseJson(string json, string currentVersionStr)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? (tagElem.GetString() ?? "") : "";
            string cleanTag = tagName.TrimStart('v', 'V');

            string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? (urlElem.GetString() ?? "") : "";
            string body = root.TryGetProperty("body", out var bodyElem) ? (bodyElem.GetString() ?? "") : "";

            string? bestDownloadUrl = null;
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                // Önce Setup installer ara, yoksa App.exe veya zip ara
                foreach (var item in assetsElem.EnumerateArray())
                {
                    string name = item.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string dlUrl = item.TryGetProperty("browser_download_url", out var d) ? (d.GetString() ?? "") : "";

                    if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestDownloadUrl = dlUrl;
                        break;
                    }
                    if (bestDownloadUrl == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestDownloadUrl = dlUrl;
                    }
                    else if (bestDownloadUrl == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        bestDownloadUrl = dlUrl;
                    }
                }
            }

            bool isNewer = CompareVersions(cleanTag, currentVersionStr) > 0;

            return new UpdateCheckResult(
                HasUpdate: isNewer,
                CurrentVersion: currentVersionStr,
                LatestVersion: string.IsNullOrEmpty(cleanTag) ? currentVersionStr : cleanTag,
                ReleaseNotes: body,
                ReleaseUrl: htmlUrl,
                DownloadUrl: bestDownloadUrl ?? htmlUrl);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                HasUpdate: false,
                CurrentVersion: currentVersionStr,
                LatestVersion: currentVersionStr,
                ReleaseNotes: null,
                ReleaseUrl: null,
                DownloadUrl: null,
                ErrorMessage: ex.Message);
        }
    }

    public static int CompareVersions(string versionA, string versionB)
    {
        if (Version.TryParse(NormalizeVersion(versionA), out var va) &&
            Version.TryParse(NormalizeVersion(versionB), out var vb))
        {
            return va.CompareTo(vb);
        }
        return string.Compare(versionA, versionB, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string raw)
    {
        raw = raw.Trim().TrimStart('v', 'V');
        int dotCount = 0;
        foreach (char c in raw)
        {
            if (c == '.') dotCount++;
        }
        if (dotCount == 0) return $"{raw}.0.0";
        if (dotCount == 1) return $"{raw}.0";
        return raw;
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
