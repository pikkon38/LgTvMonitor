using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;

namespace LgTvMonitor;

/// <summary>
/// Hidden top-level window that receives the Windows notifications we care about:
/// display on/off, sleep/resume, shutdown, lock/unlock. (Must be top-level, not message-only,
/// or it won't get WM_QUERYENDSESSION.)
/// </summary>
public sealed class PowerEventWindow : NativeWindow, IDisposable
{
    public event Action<bool>? DisplayStateChanged;   // true = on
    public event Action? Suspending;
    public event Action? Resumed;
    public event Action? Unlocked;
    public event Action? Locked;
    /// <summary>Another copy of the app was started: show the window.</summary>
    public event Action? ShowRequested;

    /// <summary>Broadcast by a second copy of the app so the running one shows its window.</summary>
    public static readonly int ShowMessage = RegisterWindowMessage("LgTvMonitor.Show");
    /// <summary>Raised when Windows is shutting down (not logoff). Arg: true if it's a restart.</summary>
    public event Func<bool, Task>? ShuttingDown;

    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION = 0x0016;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMESUSPEND = 0x0007;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint ENDSESSION_LOGOFF = 0x80000000;
    private const uint ENDSESSION_CLOSEAPP = 0x00000001; // an installer (Restart Manager) wants us closed: not a shutdown
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;

    private static Guid GUID_CONSOLE_DISPLAY_STATE = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private IntPtr _powerNotify;
    private Task? _shutdownTask;
    private DateTime _lastResume = DateTime.MinValue;

    public PowerEventWindow()
    {
        CreateHandle(new CreateParams { Caption = "LgTvMonitorPowerEvents" });
        _powerNotify = RegisterPowerSettingNotification(Handle, ref GUID_CONSOLE_DISPLAY_STATE, 0);
        if (_powerNotify == IntPtr.Zero) Log.Write("RegisterPowerSettingNotification failed");
        if (!WTSRegisterSessionNotification(Handle, 0)) Log.Write("WTSRegisterSessionNotification failed");
        // Ask to be among the first apps notified at shutdown, while the network is certainly still up.
        SetProcessShutdownParameters(0x3FF, 0);
    }

    protected override void WndProc(ref Message m)
    {
        try
        {
            if (m.Msg == ShowMessage && ShowMessage != 0)
            {
                ShowRequested?.Invoke();
                return;
            }
            switch (m.Msg)
            {
                case WM_POWERBROADCAST:
                    HandlePower(ref m);
                    break;

                case WM_WTSSESSION_CHANGE:
                    if ((int)m.WParam == WTS_SESSION_UNLOCK) Unlocked?.Invoke();
                    else if ((int)m.WParam == WTS_SESSION_LOCK) Locked?.Invoke();
                    break;

                case WM_QUERYENDSESSION:
                    if (((uint)(long)m.LParam & (ENDSESSION_LOGOFF | ENDSESSION_CLOSEAPP)) == 0 && ShuttingDown != null && _shutdownTask == null)
                    {
                        ShutdownBlockReasonCreate(Handle, "Turning off the TV");
                        var restart = IsRestart();
                        _shutdownTask = ShuttingDown.Invoke(restart);
                    }
                    m.Result = 1; // never block shutdown
                    return;

                case WM_ENDSESSION:
                    if (m.WParam != IntPtr.Zero && ((uint)(long)m.LParam & ENDSESSION_CLOSEAPP) != 0)
                    {
                        Log.Write("Closing so an installer can update the app");
                        Application.Exit();
                    }
                    else if (m.WParam != IntPtr.Zero && _shutdownTask != null)
                    {
                        _shutdownTask.Wait(TimeSpan.FromSeconds(4));
                        ShutdownBlockReasonDestroy(Handle);
                    }
                    else if (m.WParam == IntPtr.Zero)
                    {
                        _shutdownTask = null; // shutdown was cancelled
                        ShutdownBlockReasonDestroy(Handle);
                    }
                    m.Result = 0;
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"WndProc error: {ex}");
        }
        base.WndProc(ref m);
    }

    private void HandlePower(ref Message m)
    {
        switch ((int)m.WParam)
        {
            case PBT_APMSUSPEND:
                Suspending?.Invoke();
                break;
            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                // Both arrive on a normal resume; only report once.
                if ((DateTime.UtcNow - _lastResume).TotalSeconds > 10)
                {
                    _lastResume = DateTime.UtcNow;
                    Resumed?.Invoke();
                }
                break;
            case PBT_POWERSETTINGCHANGE:
                var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
                if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                {
                    // 0 = off, 1 = on, 2 = dimmed (treated as "still on", we do nothing)
                    if (setting.Data == 0) DisplayStateChanged?.Invoke(false);
                    else if (setting.Data == 1) DisplayStateChanged?.Invoke(true);
                }
                break;
        }
        m.Result = 1;
    }

    /// <summary>
    /// Windows logs event 1074 (User32) when a shutdown/restart is started, including whether it's a restart.
    /// Best effort: if we can't tell, treat it as a shutdown.
    /// </summary>
    private static bool IsRestart()
    {
        try
        {
            var q = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='User32'] and EventID=1074 and TimeCreated[timediff(@SystemTime) <= 120000]]]")
            { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            using var ev = reader.ReadEvent();
            if (ev == null || ev.Properties.Count < 5) return false;
            var type = ev.Properties[4].Value?.ToString() ?? "";
            Log.Write($"Shutdown type from event log: '{type}'");
            return type.Contains("restart", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not read shutdown type: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_powerNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_powerNotify);
        WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);

    [DllImport("user32.dll")]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Asks the already-running copy to show its window.</summary>
    public static void AskRunningCopyToShow() => PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Milliseconds since the last keyboard/mouse input in this session.</summary>
    public static long MillisecondsSinceLastInput()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return 0;
        return (uint)Environment.TickCount - lii.dwTime;
    }
}
