using System.Text.Json.Nodes;

namespace LgTvMonitor;

/// <summary>
/// High-level "turn the TV on/off" with retries. Only one operation runs at a time and
/// a newer request cancels an older one (e.g. mouse moved while the TV was being turned off).
/// </summary>
public sealed class TvController
{
    private readonly Func<Config> _config;
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private Task _currentTask = Task.CompletedTask;

    public TvController(Func<Config> config) => _config = config;

    public Task PowerOnAsync(string reason) => Start(ct => DoPowerOnAsync(reason, ct));

    /// <param name="automatic">Automatic offs are skipped when the TV is showing something other than the PC.</param>
    public Task PowerOffAsync(string reason, bool automatic = true) => Start(ct => DoPowerOffAsync(reason, automatic, ct));

    public void CancelPending()
    {
        lock (_gate) _current?.Cancel();
    }

    private Task Start(Func<CancellationToken, Task> op)
    {
        lock (_gate)
        {
            _current?.Cancel();
            var cts = new CancellationTokenSource();
            _current = cts;
            var previous = _currentTask;
            _currentTask = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); } catch { }
                if (cts.IsCancellationRequested) return;
                try { await op(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { Log.Write("  (superseded by a newer request)"); }
                catch (Exception ex) { Log.Write($"  failed: {ex.Message}"); }
            });
            return _currentTask;
        }
    }

    private async Task DoPowerOnAsync(string reason, CancellationToken ct)
    {
        var cfg = _config();
        Log.Write($"TV ON  <- {reason}");
        if (!cfg.IsConfigured) { Log.Write("  not configured yet"); return; }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        int attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            if (await WebOsClient.IsReachableAsync(cfg.TvIp).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(cfg.ClientKey)) { Log.Write("  TV is reachable (not paired, assuming on)"); return; }
                try
                {
                    await using var tv = await WebOsClient.ConnectAsync(cfg.TvIp, ct).ConfigureAwait(false);
                    await tv.RegisterAsync(cfg.ClientKey, ct).ConfigureAwait(false);
                    var ps = await tv.RequestAsync(WebOsClient.PowerStateUri, null, ct).ConfigureAwait(false);
                    var state = (string?)ps?["state"] ?? "";
                    var processing = (string?)ps?["processing"] ?? "";
                    Log.Write($"  power state: {state} {processing}".TrimEnd());

                    if (state is "Screen Off" or "Screen Saver" && processing == "")
                    {
                        await tv.RequestAsync(WebOsClient.ScreenOnUri, new JsonObject { ["standbyMode"] = "active" }, ct)
                            .ConfigureAwait(false);
                        Log.Write("  screen turned on");
                        return;
                    }
                    if (state == "Active" && processing == "")
                    {
                        Log.Write("  TV is on");
                        return;
                    }
                    // Still shutting down / standby: keep waking it.
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Log.Write($"  SSAP: {ex.Message}");
                }
            }
            Network.SendMagicPacket(cfg.TvMac, cfg.TvIp);
            await Task.Delay(attempt < 3 ? 1000 : 2000, ct).ConfigureAwait(false);
        }
        Log.Write("  gave up waiting for the TV to answer (it may still be on; check 'Turn on via Wi-Fi/LAN')");
    }

    private async Task DoPowerOffAsync(string reason, bool automatic, CancellationToken ct)
    {
        var cfg = _config();
        Log.Write($"TV OFF <- {reason}");
        if (!cfg.IsConfigured || string.IsNullOrEmpty(cfg.ClientKey))
        {
            Log.Write("  not paired yet; open Setup");
            return;
        }
        if (!await WebOsClient.IsReachableAsync(cfg.TvIp, 1000).ConfigureAwait(false))
        {
            Log.Write("  TV not reachable (already off?)");
            return;
        }

        await using var tv = await WebOsClient.ConnectAsync(cfg.TvIp, ct, 3000).ConfigureAwait(false);
        await tv.RegisterAsync(cfg.ClientKey, ct).ConfigureAwait(false);

        if (automatic && !string.IsNullOrEmpty(cfg.PcInputAppId))
        {
            try
            {
                var fg = await tv.RequestAsync(WebOsClient.ForegroundAppUri, null, ct, 2000).ConfigureAwait(false);
                var appId = (string?)fg?["appId"] ?? "";
                if (appId != "" && !string.Equals(appId, cfg.PcInputAppId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Write($"  skipped: TV is showing '{appId}', not the PC input ({cfg.PcInputAppId})");
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write($"  could not read current input ({ex.Message}); turning off anyway");
            }
        }

        if (cfg.OffMode == OffMode.ScreenOff)
        {
            await tv.RequestAsync(WebOsClient.ScreenOffUri, new JsonObject { ["standbyMode"] = "active" }, ct)
                .ConfigureAwait(false);
            Log.Write("  screen turned off");
        }
        else
        {
            await tv.SendRequestNoWaitAsync(WebOsClient.PowerOffUri, ct).ConfigureAwait(false);
            Log.Write("  power off sent");
        }
    }

    /// <summary>Pairs with the TV (TV shows a prompt) and returns the key and the input currently on screen.</summary>
    public static async Task<(string key, string? foregroundApp)> PairAsync(string ip, string? existingKey, CancellationToken ct)
    {
        await using var tv = await WebOsClient.ConnectAsync(ip, ct).ConfigureAwait(false);
        var key = await tv.RegisterAsync(existingKey, ct).ConfigureAwait(false);
        string? app = null;
        try
        {
            var fg = await tv.RequestAsync(WebOsClient.ForegroundAppUri, null, ct, 2000).ConfigureAwait(false);
            app = (string?)fg?["appId"];
        }
        catch { }
        try
        {
            await tv.RequestAsync(WebOsClient.ToastUri, new JsonObject { ["message"] = "LG TV Monitor is paired with this PC" }, ct, 2000)
                .ConfigureAwait(false);
        }
        catch { }
        return (key, app);
    }
}
