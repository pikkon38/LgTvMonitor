using System.Diagnostics;
using Microsoft.Win32;

namespace LgTvMonitor;

/// <summary>The app window: same layout as Device Switch (header, cards, footer) and the shared dark theme.</summary>
public sealed class SetupForm : Form
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "LgTvMonitor";
    const int ButtonHeight = 38;

    readonly Config _cfg = Config.Load();

    // Every control in a row is this tall, so inputs, drop-downs and buttons line up.
    const int FieldWidth = 240;
    const int SideButtonWidth = 112;

    readonly DarkInput _ip = new(FieldWidth, "e.g. 192.168.1.50");
    readonly DarkInput _mac = new(FieldWidth, "e.g. 64:95:6C:12:34:56");
    readonly Button _find = Btn("Find TV", width: SideButtonWidth);
    readonly Button _detect = Btn("Detect", width: SideButtonWidth);
    readonly Button _pair = Btn("Pair with TV", primary: true);
    readonly Button _repair = Btn("Pair again");
    readonly Label _pairNote = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 0, 0, 0) };
    readonly DarkDropDown _mode = new() { Width = FieldWidth + 8 + SideButtonWidth + 120, Height = ButtonHeight, Margin = Padding.Empty };
    readonly DarkInput _delay = new(56, center: true);
    readonly DarkCheckBox _onlyPcInput = new("");
    readonly Label _inputNote = new()
    {
        Text = "Pair while the TV shows the PC, and the app will leave the TV on when it shows another input or app.",
        AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 14, 0, 0),
    };
    readonly DarkCheckBox _offDisplay = new("Windows turns the display off (locked or not)");
    readonly DarkCheckBox _offShutdown = new("The PC shuts down (a restart leaves it on)");
    readonly DarkCheckBox _offSleep = new("The PC goes to sleep");
    readonly DarkCheckBox _onDisplay = new("You press a key or move the mouse");
    readonly DarkCheckBox _onUnlock = new("The PC unlocks (Windows Hello, PIN...)");
    readonly DarkCheckBox _onBoot = new("The PC starts, before you sign in");
    readonly DarkCheckBox _onResume = new("The PC wakes from sleep");
    readonly Button _test = Btn("Test off and on");
    readonly Button _save = Btn("Save", primary: true, width: 96);
    readonly Label _state = new() { AutoSize = true, Font = Theme.Ui(20f, FontStyle.Bold), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    readonly Label _stateNote = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Ui(9.5f), Anchor = AnchorStyles.Right, Margin = new Padding(0, 2, 2, 0) };
    readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, MaximumSize = new Size(470, 0), Margin = Padding.Empty };
    readonly DarkCheckBox _autostart = new("Start with Windows");
    readonly DarkCheckBox _startMinimized = new("Start minimized to the tray");
    readonly DarkDropDown _closeAction = new() { Width = 260, Height = ButtonHeight, Margin = Padding.Empty };
    bool _exiting;

    /// <summary>The user chose Exit when closing the window: the tray app should quit.</summary>
    public event Action? ExitRequested;
    /// <summary>The window went to the tray.</summary>
    public event Action? MinimizedToTray;
    /// <summary>The user pressed Update.</summary>
    public event Action? UpdateRequested;

    readonly Button _update = Btn("Update", primary: true);
    readonly LinkLabel _checkUpdates = new()
    {
        Text = "Check for updates", AutoSize = true, LinkColor = Theme.Muted, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Muted,
        LinkBehavior = LinkBehavior.HoverUnderline, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
    };
    readonly LinkLabel _openLog = new()
    {
        Text = "Open log", AutoSize = true, LinkColor = Theme.Muted, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Muted,
        LinkBehavior = LinkBehavior.HoverUnderline, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
    };
    readonly TableLayoutPanel _root;

    public SetupForm()
    {
        Text = "LG TV Monitor";
        Icon = AppIcon.Get();
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(840, 760);

        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24, 20, 24, 16), BackColor = Theme.Bg, AutoScroll = true };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++) _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Header: title on the left, pairing state on the right.
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 18) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = Stack();
        titles.Controls.Add(new Label { Text = "LG TV Monitor", AutoSize = true, Font = Theme.Ui(20f, FontStyle.Bold), Margin = Padding.Empty });
        titles.Controls.Add(new Label
        {
            Text = "Turns your LG TV off and on with your PC, the way a monitor would.", AutoSize = true,
            ForeColor = Theme.Muted, Font = Theme.Ui(10f), Margin = new Padding(2, 2, 0, 0),
        });
        header.Controls.Add(titles, 0, 0);
        var state = Stack();
        state.Anchor = AnchorStyles.Right;
        state.Controls.Add(_state);
        state.Controls.Add(_stateNote);
        header.Controls.Add(state, 1, 0);

        // TV card: a label / field / button grid, then pairing.
        var tvRows = Stack();
        tvRows.Controls.Add(Theme.Caption("Your TV"));
        tvRows.Controls.Add(Note("Switch the TV on, then click Find TV. It fills in both addresses."));
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 14, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddFieldRow(grid, 0, "IP address", _ip, _find);
        AddFieldRow(grid, 1, "MAC address", _mac, _detect);
        tvRows.Controls.Add(grid);
        tvRows.Controls.Add(HRow(new Padding(120, 6, 0, 0), _pair, _repair, _pairNote));
        tvRows.Controls.Add(Note("Tip: give the TV a fixed IP in your router (a DHCP reservation) so it never changes.", top: 16));

        // Turning off card.
        var offRows = Stack();
        offRows.Controls.Add(Theme.Caption("How the TV turns off"));
        _mode.Items.Add("Power off  (Wake-on-LAN turns it back on)");
        _mode.Items.Add("Screen off only  (stays on the network, wakes faster)");
        offRows.Controls.Add(HRow(new Padding(0, 4, 0, 0), _mode));
        _delay.AllowDigitsOnly();
        _delay.Margin = new Padding(0, 0, 10, 0);
        offRows.Controls.Add(HRow(new Padding(0, 10, 0, 0),
            Words("Wait", new Padding(0, 0, 10, 0)), _delay, Words("seconds after Windows turns the display off before turning the TV off")));
        _onlyPcInput.Margin = new Padding(0, 16, 0, 0);
        offRows.Controls.Add(_onlyPcInput);
        offRows.Controls.Add(_inputNote);

        // When card: two columns, off triggers and on triggers.
        var when = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        when.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        when.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var offCol = Stack();
        offCol.Controls.Add(Theme.Caption("TV turns off when"));
        foreach (var c in new[] { _offDisplay, _offShutdown, _offSleep }) offCol.Controls.Add(c);
        var onCol = Stack();
        onCol.Controls.Add(Theme.Caption("TV turns on when"));
        foreach (var c in new[] { _onDisplay, _onUnlock, _onBoot, _onResume }) onCol.Controls.Add(c);
        when.Controls.Add(offCol, 0, 0);
        when.Controls.Add(onCol, 1, 0);

        // App card: startup and close behaviour.
        var appRows = Stack();
        appRows.Controls.Add(Theme.Caption("App"));
        _autostart.Margin = new Padding(0, 0, 28, 0);
        _startMinimized.Margin = Padding.Empty;
        appRows.Controls.Add(HRow(new Padding(0, 2, 0, 0), _autostart, _startMinimized));
        _closeAction.Items.Add("Ask every time");
        _closeAction.Items.Add("Minimize to the tray");
        _closeAction.Items.Add("Exit the app");
        appRows.Controls.Add(HRow(new Padding(0, 12, 0, 0), Words("When I close the window", new Padding(0, 0, 14, 0)), _closeAction));
        _autostart.CheckedChanged += (_, _) => _startMinimized.Enabled = _autostart.Checked;

        // Action bar: status on the left, Test and Save on the right.
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 4, 0, 16) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.Controls.Add(_status, 0, 0);
        _save.Margin = Padding.Empty;
        var buttons = HRow(Padding.Empty, _test, _save);
        buttons.Anchor = AnchorStyles.Right;
        actions.Controls.Add(buttons, 1, 0);

        // Footer under a hairline: version, updates and log on the left; Update button on the right when there is one.
        var rule = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Line, Margin = new Padding(0, 0, 0, 12) };
        var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var dot = new Padding(0, 0, 8, 0);
        var left = HRow(Padding.Empty,
            Words($"v{Updater.Current.ToString(3)}", dot, Theme.Muted), Words("\u00B7", dot, Theme.Muted),
            _checkUpdates, Words("\u00B7", new Padding(8, 0, 8, 0), Theme.Muted), _openLog);
        left.Anchor = AnchorStyles.Left;
        footer.Controls.Add(left, 0, 0);
        _update.Anchor = AnchorStyles.Right;
        _update.Margin = Padding.Empty;
        footer.Controls.Add(_update, 1, 0);
        _update.Click += (_, _) => UpdateRequested?.Invoke();
        _checkUpdates.LinkClicked += async (_, _) => await CheckForUpdatesAsync();
        Updater.AvailableChanged += ShowUpdate;
        Disposed += (_, _) => Updater.AvailableChanged -= ShowUpdate;
        ShowUpdate();

        _root.Controls.Add(header, 0, 0);
        _root.Controls.Add(CardOf(tvRows), 0, 1);
        _root.Controls.Add(CardOf(offRows), 0, 2);
        _root.Controls.Add(CardOf(when), 0, 3);
        _root.Controls.Add(CardOf(appRows), 0, 4);
        _root.Controls.Add(actions, 0, 5);
        _root.Controls.Add(rule, 0, 6);
        _root.Controls.Add(footer, 0, 7);
        Controls.Add(_root);

        _find.Click += async (_, _) => await Busy(_find, FindAsync);
        _detect.Click += async (_, _) => await Busy(_detect, DetectMacAsync);
        _pair.Click += async (_, _) => await Busy(_pair, PairAsync);
        _repair.Click += async (_, _) => await Busy(_repair, PairAsync);
        _test.Click += async (_, _) => await Busy(_test, TestAsync);
        _save.Click += (_, _) => { if (SaveConfig()) SetStatus("Saved. LG TV Monitor keeps running in the tray.", Theme.Good); };
        _openLog.LinkClicked += (_, _) => OpenLog();
        AcceptButton = _save;

        LoadValues();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    /// <summary>Close from the title bar: ask (or use the remembered choice) whether to minimize to the tray or exit.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_exiting || e.CloseReason != CloseReason.UserClosing) return; // shutdown, sign-out, tray Exit: just close

        var action = Config.Load().CloseAction;
        if (action == CloseAction.Ask)
        {
            using var dlg = new CloseDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) { e.Cancel = true; return; }
            action = dlg.Choice;
            if (dlg.Remember)
            {
                var cfg = Config.Load();
                cfg.CloseAction = action;
                try { cfg.Save(); } catch { }
                _cfg.CloseAction = action;
                _closeAction.SelectedIndex = (int)action;
            }
        }

        if (action == CloseAction.Minimize)
        {
            e.Cancel = true;
            Hide();
            MinimizedToTray?.Invoke();
        }
        else
        {
            _exiting = true;
            ExitRequested?.Invoke();
        }
    }

    void ShowUpdate()
    {
        if (InvokeRequired) { BeginInvoke(ShowUpdate); return; }
        var u = Updater.Available;
        _update.Visible = u != null;
        if (u != null) _update.Text = $"Update to v{u.Version.ToString(3)}";
    }

    async Task CheckForUpdatesAsync()
    {
        _checkUpdates.Enabled = false;
        SetStatus("Checking for updates...", Theme.Muted);
        try
        {
            var u = await Updater.CheckAsync();
            if (u == null) SetStatus($"You have the latest version (v{Updater.Current.ToString(3)}).", Theme.Good);
            else SetStatus($"Version {u.Version.ToString(3)} is available. Press Update to install it.", Theme.Accent);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't check for updates: " + ex.Message, Theme.Bad);
        }
        finally { _checkUpdates.Enabled = true; }
    }

    /// <summary>Shown while the update downloads, or if it fails.</summary>
    public void SetUpdateState(string text, Color color, bool busy)
    {
        _update.Enabled = !busy;
        if (busy) _update.Text = "Downloading...";
        else ShowUpdate();
        SetStatus(text, color);
    }

    /// <summary>Bring the window back from the tray or the taskbar.</summary>
    /// <summary>Close without the minimize/exit prompt (used when the app quits to update).</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Fit the window to its content.
        var want = _root.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
        var max = Screen.FromControl(this).WorkingArea.Height - 80;
        ClientSize = new Size(ClientSize.Width, Math.Min(want, max));
        ActiveControl = _save;
    }

    // ---------- values ----------

    void LoadValues()
    {
        _ip.Value = _cfg.TvIp;
        _mac.Value = _cfg.TvMac;
        _mode.SelectedIndex = _cfg.OffMode == OffMode.ScreenOff ? 1 : 0;
        _delay.Value = Math.Clamp(_cfg.DisplayOffDelaySeconds, 1, 300).ToString();
        _offDisplay.Checked = _cfg.OffWhenDisplayOff;
        _onDisplay.Checked = _cfg.OnWhenDisplayOn;
        _offShutdown.Checked = _cfg.OffOnShutdown;
        _onBoot.Checked = _cfg.OnOnStartup;
        _offSleep.Checked = _cfg.OffOnSleep;
        _onResume.Checked = _cfg.OnOnResume;
        _onUnlock.Checked = _cfg.OnOnUnlock;
        _autostart.Checked = !_cfg.IsConfigured || IsAutostart();
        _startMinimized.Checked = _cfg.StartMinimized;
        _startMinimized.Enabled = _autostart.Checked;
        _closeAction.SelectedIndex = (int)_cfg.CloseAction;
        UpdatePairUi();
        SetStatus(Config.Load().Paused ? "Automatic control is paused (tray icon > Pause automatic control)." : "", Theme.Warn);
    }

    void UpdatePairUi()
    {
        bool paired = !string.IsNullOrEmpty(_cfg.ClientKey);
        _state.Text = paired ? "Paired" : "Not paired";
        _state.ForeColor = paired ? Theme.Good : Theme.Bad;
        _stateNote.Text = string.IsNullOrWhiteSpace(_cfg.TvIp) ? "No TV set up yet" : $"TV at {_cfg.TvIp}";
        _pair.Visible = !paired;
        _repair.Visible = paired;
        _pairNote.Text = paired
            ? "The TV remembers this PC. Pair again only after a TV reset."
            : "Your TV will ask to allow this PC. Press Allow on the remote.";
        // Only offered once pairing has recorded which HDMI input the PC is on; until then a note explains it.
        bool known = !string.IsNullOrEmpty(_cfg.PcInputAppId);
        _onlyPcInput.Visible = known;
        _inputNote.Visible = !known;
        _onlyPcInput.Checked = known;
        if (known) _onlyPcInput.Text = $"Leave the TV on when it shows another input or app (the PC is on {InputName(_cfg.PcInputAppId)})";
    }

    static string InputName(string appId) =>
        appId.StartsWith("com.webos.app.hdmi", StringComparison.OrdinalIgnoreCase) ? "HDMI " + appId["com.webos.app.hdmi".Length..] : appId;

    string Ip => _ip.Value.Trim();

    // ---------- actions ----------

    async Task FindAsync()
    {
        SetStatus("Searching the network for LG TVs...", Theme.Muted);
        var tvs = await Network.DiscoverAsync();
        if (tvs.Count == 0) { SetStatus("No TV found. Make sure it's on and on the same network, or type its IP.", Theme.Warn); return; }
        if (tvs.Count == 1) { Pick(tvs[0]); await DetectMacAsync(); return; }

        // More than one TV: let the user pick from a dark menu under the button.
        var menu = Theme.Menu();
        var picked = new TaskCompletionSource<Network.FoundTv?>();
        foreach (var t in tvs)
        {
            var item = new ToolStripMenuItem($"{t.Name}    {t.Ip}") { ForeColor = Theme.Text };
            item.Click += (_, _) => picked.TrySetResult(t);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(() => { picked.TrySetResult(null); menu.Dispose(); });
        menu.Show(_find, new Point(0, _find.Height));
        SetStatus($"Found {tvs.Count} TVs. Pick yours.", Theme.Muted);
        if (await picked.Task is { } tv) { Pick(tv); await DetectMacAsync(); }
    }

    void Pick(Network.FoundTv tv)
    {
        _ip.Value = tv.Ip;
        SetStatus($"Found {tv.Name} at {tv.Ip}.", Theme.Good);
    }

    async Task DetectMacAsync()
    {
        if (Ip == "") { SetStatus("Enter the TV's IP address first.", Theme.Warn); return; }
        await WebOsClient.IsReachableAsync(Ip); // fills Windows' ARP cache
        var mac = await Task.Run(() => Network.LookupMac(Ip));
        if (mac == null)
        {
            SetStatus("Couldn't detect the MAC address. It's on the TV under Settings > General > About This TV.", Theme.Warn);
            return;
        }
        _mac.Value = mac;
        SetStatus($"Found the TV at {Ip} ({mac}).", Theme.Good);
    }

    async Task PairAsync()
    {
        if (Ip == "") { SetStatus("Enter the TV's IP address first.", Theme.Warn); return; }
        SetStatus("Look at your TV and press Allow on the remote (you have 60 seconds)...", Theme.Accent);
        try
        {
            var (key, app) = await TvController.PairAsync(Ip, _cfg.ClientKey, CancellationToken.None);
            _cfg.ClientKey = key;
            _cfg.TvIp = Ip;
            _cfg.PcInputAppId = app is not null && app.StartsWith("com.webos.app.hdmi", StringComparison.OrdinalIgnoreCase) ? app : "";
            if (string.IsNullOrWhiteSpace(_mac.Value)) await DetectMacAsync();
            if (!string.IsNullOrWhiteSpace(_mac.Value)) _cfg.TvMac = _mac.Value;
            _cfg.Save();
            if (_cfg.PcInputAppId == "")
                SetStatus($"Paired. The TV was showing '{app}', not an HDMI input, so 'leave the TV on' is off. Pair again while it shows the PC to turn it on.", Theme.Warn);
            else
                SetStatus("Paired. The TV won't ask again.", Theme.Good);
        }
        catch (Exception ex)
        {
            SetStatus($"Pairing failed: {ex.Message}", Theme.Bad);
        }
        UpdatePairUi();
    }

    async Task TestAsync()
    {
        if (!SaveConfig()) return;
        var tv = new TvController(Config.Load);
        SetStatus("Turning the TV off...", Theme.Accent);
        await tv.PowerOffAsync("test", automatic: false);
        await Task.Delay(15000);
        SetStatus("Turning the TV back on...", Theme.Accent);
        await tv.PowerOnAsync("test");
        SetStatus("Test finished. If the TV didn't come back on, open the log and check 'Turn on via Wi-Fi' on the TV.", Theme.Muted);
    }

    bool SaveConfig()
    {
        if (Ip == "") { SetStatus("Enter the TV's IP address.", Theme.Warn); return false; }
        try { _mac.Value = Network.FormatMac(Network.ParseMac(_mac.Value)); }
        catch { SetStatus("Enter a valid MAC address (like 64:95:6C:12:34:56), or click Detect.", Theme.Warn); return false; }

        // Re-read so a key saved by another copy of the app isn't lost.
        var disk = Config.Load();
        if (string.IsNullOrEmpty(_cfg.ClientKey)) _cfg.ClientKey = disk.ClientKey;
        if (!_onlyPcInput.Checked) _cfg.PcInputAppId = "";
        _cfg.TvIp = Ip;
        _cfg.TvMac = _mac.Value;
        _cfg.OffMode = _mode.SelectedIndex == 1 ? OffMode.ScreenOff : OffMode.PowerOff;
        _cfg.DisplayOffDelaySeconds = int.TryParse(_delay.Value, out var secs) ? Math.Clamp(secs, 1, 300) : 2;
        _delay.Value = _cfg.DisplayOffDelaySeconds.ToString();
        _cfg.OffWhenDisplayOff = _offDisplay.Checked;
        _cfg.OnWhenDisplayOn = _onDisplay.Checked;
        _cfg.OffOnShutdown = _offShutdown.Checked;
        _cfg.OnOnStartup = _onBoot.Checked;
        _cfg.OffOnSleep = _offSleep.Checked;
        _cfg.OnOnResume = _onResume.Checked;
        _cfg.OnOnUnlock = _onUnlock.Checked;
        _cfg.Paused = disk.Paused;
        _cfg.StartMinimized = _startMinimized.Checked;
        _cfg.CloseAction = (CloseAction)Math.Max(0, _closeAction.SelectedIndex);
        try
        {
            _cfg.Save();
            SetAutostart(_autostart.Checked);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save settings: {ex.Message} (was install.ps1 run?)", Theme.Bad);
            return false;
        }
        UpdatePairUi();
        if (string.IsNullOrEmpty(_cfg.ClientKey)) { SetStatus("Saved. Pair with the TV so the app can turn it off.", Theme.Warn); return true; }
        return true;
    }

    static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) != null;
    }

    static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --autostart");
        else k.DeleteValue(RunValue, false);
    }

    internal static void OpenLog()
    {
        if (!File.Exists(Log.FilePath)) Log.Write("Log created");
        Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
    }

    // ---------- helpers ----------

    void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    async Task Busy(Button b, Func<Task> work)
    {
        b.Enabled = false;
        try { await work(); }
        catch (Exception ex) { SetStatus(ex.Message, Theme.Bad); }
        finally { b.Enabled = true; }
    }

    /// <summary>Every button the same height, whatever its style; side buttons also share a width.</summary>
    static Button Btn(string text, bool primary = false, int width = 0)
    {
        var b = Theme.Button(text, primary);
        b.Font = primary ? Theme.SemiBold(9.5f) : Theme.Ui(9.5f);
        if (width > 0)
        {
            b.AutoSize = false;
            b.Size = new Size(width, ButtonHeight);
        }
        else
        {
            b.MinimumSize = new Size(0, ButtonHeight);
            b.MaximumSize = new Size(0, ButtonHeight);
        }
        return b;
    }

    /// <summary>One row of the TV grid: label, input and button, all centred on the same line.</summary>
    static void AddFieldRow(TableLayoutPanel grid, int row, string label, DarkInput input, Button button)
    {
        var gap = row == 0 ? 10 : 0;
        grid.Controls.Add(Words(label, new Padding(0, 0, 0, gap)), 0, row);
        input.Margin = new Padding(0, 0, 8, gap);
        input.Anchor = AnchorStyles.Left;
        grid.Controls.Add(input, 1, row);
        button.Margin = new Padding(0, 0, 0, gap);
        button.Anchor = AnchorStyles.Left;
        grid.Controls.Add(button, 2, row);
    }

    static Card CardOf(Control content)
    {
        content.Dock = DockStyle.Top;
        var card = new Card { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 16, 20, 18), Margin = new Padding(0, 0, 0, 14) };
        card.Controls.Add(content);
        return card;
    }

    static FlowLayoutPanel Stack() => new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };

    /// <summary>A single row whose controls are all centred vertically on one line.</summary>
    static TableLayoutPanel HRow(Padding margin, params Control[] controls)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = controls.Length, RowCount = 1, Margin = margin };
        for (int i = 0; i < controls.Length; i++)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (controls[i].Anchor == (AnchorStyles.Top | AnchorStyles.Left)) controls[i].Anchor = AnchorStyles.Left;
            t.Controls.Add(controls[i], i, 0);
        }
        return t;
    }

    static Label Words(string text, Padding margin = default, Color? color = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Theme.Text, Margin = margin, Anchor = AnchorStyles.Left,
    };

    static Label Note(string text, int top = 0) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, top, 0, 0),
    };
}

/// <summary>The app icon (dark tile with an orange TV), embedded in the exe from LgTvMonitor.ico.</summary>
static class AppIcon
{
    static Icon? _icon;
    public static Icon Get()
    {
        if (_icon != null) return _icon;
        try { _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        return _icon ??= SystemIcons.Application;
    }
}
