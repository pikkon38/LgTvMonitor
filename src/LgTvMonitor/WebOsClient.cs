using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace LgTvMonitor;

/// <summary>
/// Minimal client for the LG webOS "SSAP" second-screen websocket API
/// (the same API the LG ThinQ phone app and Home Assistant use).
/// </summary>
public sealed class WebOsClient : IAsyncDisposable
{
    public const string PowerOffUri = "ssap://system/turnOff";
    public const string ScreenOffUri = "ssap://com.webos.service.tvpower/power/turnOffScreen";
    public const string ScreenOnUri = "ssap://com.webos.service.tvpower/power/turnOnScreen";
    public const string PowerStateUri = "ssap://com.webos.service.tvpower/power/getPowerState";
    public const string ToastUri = "ssap://system.notifications/createToast";
    public const string ForegroundAppUri = "ssap://com.webos.applicationManager/getForegroundAppInfo";

    private readonly ClientWebSocket _ws;
    private int _nextId = 1;

    private WebOsClient(ClientWebSocket ws) => _ws = ws;

    /// <summary>Quick TCP probe: is the TV's API port answering?</summary>
    public static async Task<bool> IsReachableAsync(string ip, int timeoutMs = 700)
    {
        foreach (var port in new[] { 3001, 3000 })
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);
                await tcp.ConnectAsync(ip, port, cts.Token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                // try next port
            }
        }
        return false;
    }

    public static async Task<WebOsClient> ConnectAsync(string ip, CancellationToken ct, int timeoutMs = 4000)
    {
        Exception? last = null;
        // Current firmware (incl. the C-series) requires the TLS port; 3000 is a fallback for older sets.
        foreach (var uri in new[] { $"wss://{ip}:3001", $"ws://{ip}:3000" })
        {
            var ws = new ClientWebSocket();
            // The TV uses a self-signed certificate. We only ever talk to the one LAN address the user configured.
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                await ws.ConnectAsync(new Uri(uri), cts.Token).ConfigureAwait(false);
                return new WebOsClient(ws);
            }
            catch (Exception ex)
            {
                last = ex;
                ws.Dispose();
            }
        }
        throw new IOException($"Could not connect to the TV at {ip}: {last?.Message}", last);
    }

    /// <summary>
    /// Registers with the TV. With no key the TV shows an "allow this device?" prompt;
    /// the returned key must be saved and passed next time so it never asks again.
    /// </summary>
    public async Task<string> RegisterAsync(string? clientKey, CancellationToken ct, int promptTimeoutSeconds = 60)
    {
        var payload = new JsonObject
        {
            ["forcePairing"] = false,
            ["pairingType"] = "PROMPT",
            ["manifest"] = new JsonObject
            {
                ["manifestVersion"] = 1,
                ["appVersion"] = "1.1",
                ["permissions"] = new JsonArray(
                    "CONTROL_POWER", "CONTROL_DISPLAY", "CONTROL_TV_SCREEN", "READ_POWER_STATE",
                    "WRITE_NOTIFICATION_TOAST", "READ_NETWORK_STATE", "READ_SETTINGS", "READ_RUNNING_APPS",
                    "READ_INPUT_DEVICE_LIST"),
            },
        };
        if (!string.IsNullOrEmpty(clientKey))
            payload["client-key"] = clientKey;

        await SendAsync(new JsonObject { ["type"] = "register", ["id"] = "register_0", ["payload"] = payload }, ct)
            .ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(string.IsNullOrEmpty(clientKey) ? promptTimeoutSeconds : 8));
        while (true)
        {
            var msg = await ReceiveAsync(cts.Token).ConfigureAwait(false);
            var type = (string?)msg["type"];
            if ((string?)msg["id"] != "register_0") continue;
            switch (type)
            {
                case "registered":
                    return (string?)msg["payload"]?["client-key"]
                           ?? throw new InvalidOperationException("TV accepted the connection but sent no key.");
                case "error":
                    throw new InvalidOperationException($"TV refused pairing: {(string?)msg["error"]}");
                default:
                    continue; // "response" with pairingType PROMPT while the TV waits for the user
            }
        }
    }

    public async Task<JsonNode?> RequestAsync(string uri, JsonObject? payload, CancellationToken ct, int timeoutMs = 5000)
    {
        var id = $"req_{Interlocked.Increment(ref _nextId)}";
        await SendAsync(new JsonObject
        {
            ["type"] = "request",
            ["id"] = id,
            ["uri"] = uri,
            ["payload"] = payload ?? new JsonObject(),
        }, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        while (true)
        {
            var msg = await ReceiveAsync(cts.Token).ConfigureAwait(false);
            if ((string?)msg["id"] != id) continue;
            if ((string?)msg["type"] == "error")
                throw new InvalidOperationException($"{uri} failed: {(string?)msg["error"]}");
            var p = msg["payload"];
            if (p?["returnValue"] is JsonValue rv && rv.TryGetValue<bool>(out var ok) && !ok)
                throw new InvalidOperationException($"{uri} failed: {p.ToJsonString()}");
            return p;
        }
    }

    /// <summary>Fire-and-forget style send for power off: the TV often drops the socket before replying.</summary>
    public async Task SendRequestNoWaitAsync(string uri, CancellationToken ct)
    {
        await SendAsync(new JsonObject
        {
            ["type"] = "request",
            ["id"] = $"req_{Interlocked.Increment(ref _nextId)}",
            ["uri"] = uri,
            ["payload"] = new JsonObject(),
        }, ct).ConfigureAwait(false);
        // Give the frame a moment to leave before the caller closes the socket.
        await Task.Delay(300, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(JsonObject obj, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private async Task<JsonNode> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close)
                throw new IOException("The TV closed the connection.");
            ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) break;
        }
        return JsonNode.Parse(ms.ToArray()) ?? new JsonObject();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(500);
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }
        _ws.Dispose();
    }
}
