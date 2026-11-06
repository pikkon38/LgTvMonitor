using System.Text.Json;
using System.Text.Json.Serialization;

namespace LgTvMonitor;

public enum OffMode
{
    /// <summary>Full power off (ssap://system/turnOff). Turned back on with Wake-on-LAN.</summary>
    PowerOff,
    /// <summary>Panel off only; the TV stays on the network. Faster and more reliable to wake.</summary>
    ScreenOff,
}

public sealed class Config
{
    public string TvIp { get; set; } = "";
    public string TvMac { get; set; } = "";
    public string ClientKey { get; set; } = "";

    /// <summary>
    /// The TV "app" for the HDMI input the PC is on (e.g. com.webos.app.hdmi1), recorded during pairing.
    /// Automatic power-offs are skipped when the TV is showing something else (Netflix, a console...).
    /// Empty = always turn off.
    /// </summary>
    public string PcInputAppId { get; set; } = "";

    public OffMode OffMode { get; set; } = OffMode.PowerOff;

    /// <summary>Seconds the display must stay off before the TV is turned off (protects against flicker).</summary>
    public int DisplayOffDelaySeconds { get; set; } = 2;

    public bool OffWhenDisplayOff { get; set; } = true;
    public bool OnWhenDisplayOn { get; set; } = true;
    public bool OffOnShutdown { get; set; } = true;
    public bool OffOnSleep { get; set; } = true;
    public bool OnOnResume { get; set; } = true;
    public bool OnOnUnlock { get; set; } = true;
    public bool OnOnStartup { get; set; } = true;

    /// <summary>When Windows starts the app at sign-in, stay in the tray instead of opening the window.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>What the window's close button does: ask, minimize to the tray, or exit.</summary>
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;

    /// <summary>When true the tray app does nothing automatically (manual menu items still work).</summary>
    public bool Paused { get; set; }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TvIp) && !string.IsNullOrWhiteSpace(TvMac);

    // Stored under ProgramData so the boot task (runs as SYSTEM, before anyone logs in) can read it too.
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LgTvMonitor");

    public static string FilePath { get; } = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), JsonOpts) ?? new Config();
        }
        catch (Exception ex)
        {
            Log.Write($"Could not read config: {ex.Message}");
        }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public Config Clone() => JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts)!;
}

public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath { get; } = Path.Combine(Config.Dir, "log.txt");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.UserName}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never break power handling.
            }
        }
    }
}
