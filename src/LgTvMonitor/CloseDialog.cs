namespace LgTvMonitor;

/// <summary>What the window's close button does.</summary>
public enum CloseAction { Ask, Minimize, Exit }

/// <summary>Themed "minimize to tray or exit?" prompt shown when the window's close button is pressed.</summary>
sealed class CloseDialog : Form
{
    public CloseAction Choice { get; private set; } = CloseAction.Ask; // Ask = cancelled
    public bool Remember => _remember.Checked;

    readonly DarkCheckBox _remember = new("Remember my choice") { ForeColor = Theme.Muted, Font = Theme.Ui(9.5f), Margin = new Padding(0, 18, 0, 0) };

    public CloseDialog()
    {
        Text = "LG TV Monitor";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);

        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Location = new Point(28, 24) };
        stack.Controls.Add(new Label { Text = "Close LG TV Monitor?", AutoSize = true, Font = Theme.SemiBold(13f), Margin = Padding.Empty });
        stack.Controls.Add(new Label
        {
            Text = "Minimize to tray keeps the TV turning off and on with your PC.\nExit stops it until you open the app again.",
            AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0),
        });
        stack.Controls.Add(_remember);

        var minimize = Make("Minimize to tray", primary: true, CloseAction.Minimize);
        var exit = Make("Exit", primary: false, CloseAction.Exit);
        var cancel = Make("Cancel", primary: false, CloseAction.Ask);
        cancel.Margin = Padding.Empty;
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 22, 0, 0) };
        buttons.Controls.AddRange(new Control[] { minimize, exit, cancel });
        stack.Controls.Add(buttons);
        Controls.Add(stack);

        // Size the window to the content with even margins.
        var size = stack.GetPreferredSize(Size.Empty);
        ClientSize = new Size(size.Width + 56, size.Height + 48);
        ActiveControl = minimize;
        AcceptButton = minimize;
        CancelButton = cancel;
    }

    Button Make(string text, bool primary, CloseAction action)
    {
        var b = Theme.Button(text, primary);
        b.Font = primary ? Theme.SemiBold(9.5f) : Theme.Ui(9.5f);
        b.MinimumSize = new Size(88, 38);
        b.MaximumSize = new Size(0, 38);
        b.Click += (_, _) => { Choice = action; DialogResult = action == CloseAction.Ask ? DialogResult.Cancel : DialogResult.OK; };
        return b;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }
}
