using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LgTvMonitor;

/// <summary>
/// A text field the same height as the theme's buttons: a borderless TextBox centred inside a
/// Raised box with a Line border that turns orange while it has focus.
/// </summary>
sealed class DarkInput : Control
{
    public const int FieldHeight = 38;
    readonly TextBox _box;
    bool _focused;

    public DarkInput(int width, string placeholder = "", bool center = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Raised, ForeColor = Theme.Text, Font = Theme.Ui(10.5f),
            PlaceholderText = placeholder, TextAlign = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        Controls.Add(_box);
        Size = new Size(width, FieldHeight);
        BackColor = Theme.Raised;
        Cursor = Cursors.IBeam;
        Margin = new Padding(0, 0, 8, 0);
    }

    public event EventHandler? ValueChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    /// <summary>Only allow digits (for the delay field).</summary>
    public void AllowDigitsOnly() =>
        _box.KeyPress += (_, e) => e.Handled = !char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar);

    protected override void OnClick(EventArgs e) { _box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_box == null) return;
        _box.Width = Width - 24;
        _box.Location = new Point(12, (Height - _box.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var pen = new Pen(_focused ? Theme.Accent : Theme.Line);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>A dark check box: rounded square, orange when ticked, text in the theme font.</summary>
sealed class DarkCheckBox : CheckBox
{
    const int Box = 18;
    const int Gap = 10;
    bool _hover;

    public DarkCheckBox(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        AutoSize = true;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        Font = Theme.Ui(10f);
        Margin = new Padding(0, 0, 12, 10);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return new Size(Box + Gap + t.Width + 2, Math.Max(Box + 4, t.Height + 4));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, (Height - Box) / 2, Box, Box);
        using (var path = Rounded(r, 4))
        {
            if (Checked)
            {
                using var fill = new SolidBrush(Enabled ? (_hover ? ControlPaint.Light(Theme.Accent, 0.2f) : Theme.Accent) : Theme.Line);
                g.FillPath(fill, path);
                using var tick = new Pen(Theme.Bg, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(r.X + 4.5f, r.Y + 9.5f), new PointF(r.X + 7.5f, r.Y + 12.5f), new PointF(r.X + 13.5f, r.Y + 5.5f),
                });
            }
            else
            {
                using var fill = new SolidBrush(Theme.Raised);
                g.FillPath(fill, path);
                using var pen = new Pen(_hover && Enabled ? Theme.Muted : Theme.Line, 1.2f);
                g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, 1f);
                g.DrawPath(focus, path);
            }
        }
        var textRect = new Rectangle(Box + Gap, 0, Width - Box - Gap, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
