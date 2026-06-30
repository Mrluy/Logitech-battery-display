using System.Drawing.Drawing2D;
namespace LogitechBatteryDisplay;

internal sealed class BatteryStatusForm : Form
{
    private const int CornerRadius = 8;
    private readonly Label _title = new();
    private readonly Label _device = new();
    private readonly Label _percent = new();
    private readonly StatusPill _statusChip = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _updatedValue = new();
    private readonly IconButton _refresh = new(ButtonIcon.Refresh);
    private readonly IconButton _close = new(ButtonIcon.Close);
    private readonly BatteryGauge _gauge = new();
    private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");
    private bool _dragging;
    private Point _dragStart;

    public event EventHandler? RefreshRequested;

    public BatteryStatusForm()
    {
        Text = "罗技鼠标电量";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Width = 392;
        Height = 268;
        MinimumSize = Size;
        MaximumSize = Size;
        DoubleBuffered = true;
        BackColor = Palette.Surface;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Padding = new Padding(18);

        ConfigureLabels();
        ConfigureButtons();
        Controls.AddRange([
            _title,
            _device,
            _percent,
            _statusChip,
            _updatedLabel,
            _updatedValue,
            _gauge,
            _refresh,
            _close
        ]);

        LayoutControls();
        UpdateSnapshot(_snapshot);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int csDropShadow = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= csDropShadow;
            return cp;
        }
    }

    public void UpdateSnapshot(BatterySnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateSnapshot(snapshot));
            return;
        }

        _snapshot = snapshot;
        var percentText = snapshot.Percent is int percent ? $"{percent}%" : "--%";
        _title.Text = snapshot.IsSuccess ? "罗技无线鼠标" : "罗技鼠标电量";
        _device.Text = snapshot.DeviceName;
        _percent.Text = percentText;
        _percent.ForeColor = Palette.AccentFor(snapshot.Percent, snapshot.ChargeState);
        _statusChip.SetText(HumanizeState(snapshot), snapshot.IsSuccess ? Palette.ChipText : Palette.Warning);
        _updatedValue.Text = snapshot.Timestamp.ToString("HH:mm:ss");
        _gauge.UpdateSnapshot(snapshot);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var background = new SolidBrush(Palette.Surface);
        using var border = new Pen(Palette.Border, 1);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(bounds, CornerRadius);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);

        using var glow = new LinearGradientBrush(
            new Rectangle(0, 0, Width, Height),
            Color.FromArgb(62, 33, 125, 92),
            Color.FromArgb(0, 33, 125, 92),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(glow, 1, 1, Width - 2, Height - 2);

        using var dot = new SolidBrush(Palette.AccentFor(_snapshot.Percent, _snapshot.ChargeState));
        e.Graphics.FillEllipse(dot, 22, 25, 8, 8);

        DrawInfoPanel(e.Graphics, new Rectangle(22, 210, 328, 38));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedRect(new Rectangle(Point.Empty, Size), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y <= 52)
        {
            _dragging = true;
            _dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private void ConfigureLabels()
    {
        _title.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _title.ForeColor = Palette.PrimaryText;
        _title.BackColor = Color.Transparent;

        _device.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular);
        _device.ForeColor = Palette.SecondaryText;
        _device.BackColor = Color.Transparent;

        _percent.Font = new Font(Font.FontFamily, 42F, FontStyle.Bold);
        _percent.BackColor = Color.Transparent;

        _updatedLabel.Text = "更新";
        foreach (var label in new[] { _updatedLabel })
        {
            label.Font = new Font(Font.FontFamily, 8F, FontStyle.Regular);
            label.ForeColor = Palette.MutedText;
            label.BackColor = Color.Transparent;
        }

        foreach (var label in new[] { _updatedValue })
        {
            label.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);
            label.ForeColor = Palette.PrimaryText;
            label.BackColor = Color.Transparent;
        }
    }

    private void ConfigureButtons()
    {
        _refresh.Location = new Point(310, 18);
        _close.Location = new Point(346, 18);
        var toolTip = new ToolTip();
        toolTip.SetToolTip(_refresh, "刷新");
        toolTip.SetToolTip(_close, "关闭");
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => Hide();
    }

    private void LayoutControls()
    {
        _title.Location = new Point(36, 17);
        _title.Size = new Size(210, 24);

        _device.Location = new Point(22, 47);
        _device.Size = new Size(252, 22);

        _percent.Location = new Point(22, 96);
        _percent.Size = new Size(172, 76);

        _statusChip.Location = new Point(27, 176);
        _statusChip.Size = new Size(76, 26);

        _gauge.Location = new Point(204, 105);
        _gauge.Size = new Size(154, 80);

        _updatedLabel.Location = new Point(38, 218);
        _updatedLabel.Size = new Size(60, 18);
        _updatedValue.Location = new Point(95, 218);
        _updatedValue.Size = new Size(232, 18);
    }

    private static void DrawInfoPanel(Graphics graphics, Rectangle bounds)
    {
        using var fill = new SolidBrush(Palette.Panel);
        using var outline = new Pen(Palette.PanelBorder, 1);
        using var path = RoundedRect(bounds, 8);
        graphics.FillPath(fill, path);
        graphics.DrawPath(outline, path);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string HumanizeState(BatterySnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            return "未连接";
        }

        return snapshot.ChargeState switch
        {
            BatteryChargeState.Discharging => "使用中",
            BatteryChargeState.Recharging => "充电中",
            BatteryChargeState.AlmostFull => "接近充满",
            BatteryChargeState.Full => "已充满",
            BatteryChargeState.SlowRecharge => "慢速充电",
            BatteryChargeState.InvalidBattery => "电池异常",
            BatteryChargeState.ThermalError => "温度异常",
            BatteryChargeState.ChargingError => "充电异常",
            _ => "未知"
        };
    }

    private enum ButtonIcon
    {
        Refresh,
        Close
    }

    private sealed class IconButton : Control
    {
        private readonly ButtonIcon _icon;
        private bool _hovered;

        public IconButton(ButtonIcon icon)
        {
            _icon = icon;
            Size = new Size(28, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(_hovered ? Palette.ButtonHover : Palette.Button);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8);
            e.Graphics.FillPath(fill, path);
            using var pen = new Pen(Palette.PrimaryText, 1.9F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            if (_icon == ButtonIcon.Refresh)
            {
                e.Graphics.DrawArc(pen, new RectangleF(8.3F, 8.0F, 11.5F, 11.5F), 35, 285);
                using var brush = new SolidBrush(Palette.PrimaryText);
                var arrow = new[]
                {
                    new PointF(19.6F, 7.5F),
                    new PointF(21.7F, 12.2F),
                    new PointF(16.6F, 11.5F)
                };
                e.Graphics.FillPolygon(brush, arrow);
                return;
            }

            e.Graphics.DrawLine(pen, 9.4F, 9.4F, 18.6F, 18.6F);
            e.Graphics.DrawLine(pen, 18.6F, 9.4F, 9.4F, 18.6F);
        }
    }

    private sealed class StatusPill : Control
    {
        private string _value = "未知";
        private Color _textColor = Palette.ChipText;

        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void SetText(string value, Color textColor)
        {
            _value = value;
            _textColor = textColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Palette.Chip);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 13);
            using var font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            e.Graphics.FillPath(fill, path);
            TextRenderer.DrawText(
                e.Graphics,
                _value,
                font,
                ClientRectangle,
                _textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class BatteryGauge : Control
    {
        private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");

        public BatteryGauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void UpdateSnapshot(BatterySnapshot snapshot)
        {
            _snapshot = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var percent = _snapshot.Percent;
            var accent = Palette.AccentFor(percent, _snapshot.ChargeState);
            DrawBattery(e.Graphics, new Rectangle(4, 13, 136, 52), percent, accent);
        }

        private static void DrawBattery(Graphics graphics, Rectangle bounds, int? percent, Color accent)
        {
            using var outline = new Pen(Palette.BatteryOutline, 2.6F);
            using var cap = new SolidBrush(Palette.BatteryOutline);
            using var body = RoundedRect(bounds, 8);
            graphics.DrawPath(outline, body);
            graphics.FillRectangle(cap, new Rectangle(bounds.Right + 4, bounds.Top + 16, 8, 20));

            var inner = Rectangle.Inflate(bounds, -8, -8);
            if (percent is int value)
            {
                inner.Width = Math.Max(2, (int)Math.Round(inner.Width * value / 100.0));
                using var fill = new SolidBrush(accent);
                using var fillPath = RoundedRect(inner, 5);
                graphics.FillPath(fill, fillPath);
            }

            using var gloss = new Pen(Color.FromArgb(50, 255, 255, 255), 1.2F);
            graphics.DrawLine(gloss, bounds.Left + 11, bounds.Top + 10, bounds.Right - 12, bounds.Top + 10);
        }
    }

    private static class Palette
    {
        public static readonly Color Surface = Color.FromArgb(17, 19, 22);
        public static readonly Color Panel = Color.FromArgb(29, 32, 36);
        public static readonly Color PanelBorder = Color.FromArgb(48, 54, 60);
        public static readonly Color Border = Color.FromArgb(55, 60, 66);
        public static readonly Color PrimaryText = Color.FromArgb(242, 246, 248);
        public static readonly Color SecondaryText = Color.FromArgb(165, 176, 184);
        public static readonly Color MutedText = Color.FromArgb(111, 123, 132);
        public static readonly Color Chip = Color.FromArgb(34, 75, 58);
        public static readonly Color ChipText = Color.FromArgb(172, 245, 205);
        public static readonly Color Warning = Color.FromArgb(255, 195, 92);
        public static readonly Color Track = Color.FromArgb(48, 55, 60);
        public static readonly Color Button = Color.FromArgb(33, 37, 42);
        public static readonly Color ButtonHover = Color.FromArgb(50, 56, 62);
        public static readonly Color BatteryOutline = Color.FromArgb(206, 215, 218);

        public static Color AccentFor(int? percent, BatteryChargeState state)
        {
            if (state is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge)
            {
                return Color.FromArgb(92, 170, 255);
            }

            if (percent is null)
            {
                return Color.FromArgb(151, 161, 170);
            }

            return percent.Value switch
            {
                <= 15 => Color.FromArgb(255, 99, 87),
                <= 35 => Color.FromArgb(255, 176, 73),
                _ => Color.FromArgb(45, 214, 129)
            };
        }
    }
}
