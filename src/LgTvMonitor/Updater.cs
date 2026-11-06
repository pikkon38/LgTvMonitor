using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LgTvMonitor;

public sealed record UpdateInfo(Version Version, string DownloadUrl);

/// <summary>
/// Checks GitHub Releases for a newer LgTvMonitor-Setup.exe and runs it. The installer replaces the app in
/// Program Files, refreshes the startup task and reopens the app, so an update works like a fresh install.
/// </summary>
public static class Updater
{
    const string Repo = "pikkon38/LgTvMonitor";
    const string AssetName = "LgTvMonitor-Setup.exe";

    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    /// <summary>The newer version found by the last check, if any.</summary>
    public static UpdateInfo? Available { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Available"/> changes.</summary>
    public static event Action? AvailableChanged;

    static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LgTvMonitor", Current.ToString()));
        return http;
    }

    /// <summary>Asks GitHub for the latest release. Returns it if it is newer than this copy, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = NewClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        UpdateInfo? found = null;
        if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
        {
            var asset = root.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase));
            latest = Normalize(latest);
            if (asset.ValueKind == JsonValueKind.Object && latest > Current)
                found = new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!);
        }
        if (found?.Version != Available?.Version)
        {
            Available = found;
            AvailableChanged?.Invoke();
        }
        return found;
    }

    /// <summary>
    /// Downloads the installer and starts it silently (Windows asks for admin once).
    /// Returns true when the installer is running: the caller must exit so the files can be replaced.
    /// </summary>
    public static async Task<bool> DownloadAndRunAsync(UpdateInfo update)
    {
        var setup = Path.Combine(Path.GetTempPath(), $"LgTvMonitor-Setup-{update.Version.ToString(3)}.exe");
        using (var http = NewClient())
        using (var src = await http.GetStreamAsync(update.DownloadUrl))
        using (var dst = File.Create(setup))
            await src.CopyToAsync(dst);
        if (new FileInfo(setup).Length < 1_000_000)
        {
            File.Delete(setup);
            throw new InvalidOperationException("The downloaded installer looks incomplete.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // the admin prompt was declined
        }
        Log.Write($"Updating to v{update.Version.ToString(3)}");
        return true;
    }

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
