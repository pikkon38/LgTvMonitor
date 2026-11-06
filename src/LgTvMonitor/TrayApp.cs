namespace LgTvMonitor;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly PowerEventWindow _events;
    private readonly TvController _tv;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _offDelay = new();
    private bool _displayOn = true;
    private DateTime _autoOffAt = DateTime.MinValue;
    private SetupForm? _setup;

    private bool _toldAboutTray;
    private readonly ToolStripMenuItem _updateItem;
    private bool _updating;

    /// <param name="launch">How the app was started; decides whether the window opens.</param>
    public TrayApp(Launch launch)
    {
        _tv = new TvController(Config.Load);

        _statusItem = new ToolStripMenuItem { Enabled = false, ForeColor = Theme.Muted };
        _pauseItem = Item("Pause automatic control", TogglePause);
        var menu = Theme.Menu();
        menu.ShowCheckMargin = true;
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Turn TV on", () => _tv.PowerOnAsync("menu")));
        menu.Items.Add(Item("Turn TV off", () => _tv.PowerOffAsync("menu", automatic: false)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        _updateItem = Item("Update", () => _ = InstallUpdateAsync());
        _updateItem.ForeColor = Theme.Accent;
        _updateItem.Visible = false;
        menu.Items.Add(_updateItem);
        menu.Items.Add(Item("Open LG TV Monitor", ShowSetup));
        menu.Items.Add(Item("Open log", SetupForm.OpenLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", BeginExit));
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "LG TV Monitor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowSetup();

        _offDelay.Tick += (_, _) =>
        {
            _offDelay.Stop();
            if (!_displayOn && Allowed(c => c.OffWhenDisplayOff))
            {
                _autoOffAt = DateTime.UtcNow;
                _tv.PowerOffAsync("Windows turned the display off");
            }
        };

        _events = new PowerEventWindow();
        _events.DisplayStateChanged += OnDisplayState;
        _events.ShowRequested += ShowSetup;
        _events.Suspending += () =>
        {
            if (!Allowed(c => c.OffOnSleep)) return;
            // The network goes away quickly once suspend starts, so block briefly.
            _tv.PowerOffAsync("PC going to sleep").Wait(TimeSpan.FromSeconds(3));
        };
        _events.Resumed += () => { if (Allowed(c => c.OnOnResume)) _tv.PowerOnAsync("PC woke from sleep"); };
        _events.Unlocked += () => { if (Allowed(c => c.OnOnUnlock)) _tv.PowerOnAsync("PC unlocked"); };
        _events.ShuttingDown += restart =>
        {
            var cfg = Config.Load();
            if (cfg.Paused || !cfg.OffOnShutdown) return Task.CompletedTask;
            if (restart) { Log.Write("Restart detected: leaving the TV on"); return Task.CompletedTask; }
            return _tv.PowerOffAsync("PC shutting down");
        };

        Log.Write($"Tray app started (v{Updater.Current.ToString(3)})");

        // Look for a new version shortly after start, then twice a day.
        Updater.AvailableChanged += OnUpdateAvailable;
        var first = new System.Windows.Forms.Timer { Interval = 15_000 };
        first.Tick += async (_, _) => { first.Stop(); first.Dispose(); await CheckQuietly(); };
        first.Start();
        var daily = new System.Windows.Forms.Timer { Interval = 12 * 60 * 60 * 1000 };
        daily.Tick += async (_, _) => await CheckQuietly();
        daily.Start();
        var cfg = Config.Load();
        bool stayInTray = launch == Launch.Autostart && cfg.StartMinimized && cfg.IsConfigured;
        if (!stayInTray) ShowSetup();
    }

    private static bool Allowed(Func<Config, bool> flag)
    {
        var cfg = Config.Load();
        return !cfg.Paused && flag(cfg);
    }

    private void OnDisplayState(bool on)
    {
        _displayOn = on;
        Log.Write($"Display state: {(on ? "on" : "off")}");
        if (!on)
        {
            var cfg = Config.Load();
            _offDelay.Interval = Math.Max(1, cfg.DisplayOffDelaySeconds) * 1000;
            _offDelay.Stop();
            _offDelay.Start();
            return;
        }

        _offDelay.Stop();
        if (!Allowed(c => c.OnWhenDisplayOn)) return;

        // Guard against a feedback loop: some setups briefly report "display on" when the TV
        // disappears from HDMI. Only wake the TV right after an auto-off if there was real input.
        var sinceOff = DateTime.UtcNow - _autoOffAt;
        var sinceInput = PowerEventWindow.MillisecondsSinceLastInput();
        if (sinceOff.TotalSeconds < 20 && sinceInput > sinceOff.TotalMilliseconds)
        {
            Log.Write("  ignored: display came back right after turning the TV off, with no keyboard/mouse input");
            return;
        }
        _tv.PowerOnAsync("Windows turned the display on");
    }

    private void TogglePause()
    {
        var cfg = Config.Load();
        cfg.Paused = !cfg.Paused;
        cfg.Save();
        Log.Write(cfg.Paused ? "Automatic control paused" : "Automatic control resumed");
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        var cfg = Config.Load();
        _pauseItem.Checked = cfg.Paused;
        _pauseItem.ForeColor = cfg.Paused ? Theme.Warn : Theme.Text;
        _statusItem.ForeColor = !cfg.IsConfigured ? Theme.Bad : cfg.Paused ? Theme.Warn : Theme.Good;
        _statusItem.Text = !cfg.IsConfigured ? "Not set up yet"
            : cfg.Paused ? $"Paused  ·  TV at {cfg.TvIp}"
            : $"Active  ·  TV at {cfg.TvIp}";
        _icon.Text = "LG TV Monitor" + (cfg.Paused ? " (paused)" : "");
    }

    private void ShowSetup()
    {
        if (_setup is { IsDisposed: false }) { _setup.ShowFromTray(); return; }
        _setup = new SetupForm();
        _setup.FormClosed += (_, _) => RefreshMenu();
        _setup.ExitRequested += () => BeginExit();
        _setup.UpdateRequested += () => _ = InstallUpdateAsync();
        _setup.MinimizedToTray += () =>
        {
            RefreshMenu();
            if (_toldAboutTray) return;
            _toldAboutTray = true;
            _icon.ShowBalloonTip(3000, "LG TV Monitor", "Still running in the tray. Double-click the TV icon to open it, right-click to exit.", ToolTipIcon.Info);
        };
        _setup.Show();
    }

    /// <summary>The app window if it is open (shown or hidden in the tray).</summary>
    private SetupForm? Window => _setup is { IsDisposed: false } ? _setup : null;

    private static async Task CheckQuietly()
    {
        try { await Updater.CheckAsync(); }
        catch (Exception ex) { Log.Write($"Update check failed: {ex.Message}"); }
    }

    private void OnUpdateAvailable()
    {
        var u = Updater.Available;
        _updateItem.Visible = u != null;
        if (u == null) return;
        _updateItem.Text = $"Update to v{u.Version.ToString(3)}";
        _icon.ShowBalloonTip(5000, "LG TV Monitor", $"Version {u.Version.ToString(3)} is available. Right-click the tray icon or open the app to update.", ToolTipIcon.Info);
    }

    /// <summary>Downloads and runs the new installer, then quits so it can replace the app.</summary>
    private async Task InstallUpdateAsync()
    {
        var u = Updater.Available;
        if (u == null || _updating) return;
        _updating = true;
        Window?.SetUpdateState($"Downloading version {u.Version.ToString(3)}...", Theme.Muted, busy: true);
        try
        {
            if (await Updater.DownloadAndRunAsync(u))
            {
                // The installer takes over from here and reopens the app when it's done.
                Window?.CloseForExit();
                BeginExit();
                return;
            }
            Window?.SetUpdateState("Update cancelled: Windows needs admin permission to install it.", Theme.Warn, busy: false);
        }
        catch (Exception ex)
        {
            Log.Write($"Update failed: {ex.Message}");
            if (Window != null) Window.SetUpdateState("The update didn't download: " + ex.Message, Theme.Bad, busy: false);
            else _icon.ShowBalloonTip(4000, "LG TV Monitor", "The update didn't download. Try again from the app.", ToolTipIcon.Warning);
        }
        finally { _updating = false; }
    }

    /// <summary>Quit after the current window message finishes (safe to call from a FormClosing handler).</summary>
    private void BeginExit()
    {
        var sync = SynchronizationContext.Current;
        if (sync != null) sync.Post(_ => ExitThread(), null);
        else ExitThread();
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
        item.Click += (_, _) => onClick();
        return item;
    }

    protected override void ExitThreadCore()
    {
        Log.Write("Tray app exiting");
        _icon.Visible = false;
        _icon.Dispose();
        _events.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>How the app was started.</summary>
public enum Launch { Manual, Autostart, Setup }
