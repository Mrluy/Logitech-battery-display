using System.Drawing.Drawing2D;

namespace LogitechBatteryDisplay;

internal sealed class BatteryHistoryForm : Form
{
    private readonly BatteryHistoryStore _historyStore;
    private readonly ComboBox _range = new();
    private readonly Button _refresh = new();
    private readonly Label _summary = new();
    private readonly Label _emptyHint = new();
    private readonly BatteryHistoryChart _chart = new();

    public BatteryHistoryForm(BatteryHistoryStore historyStore)
    {
        _historyStore = historyStore;
        Text = "电量历史图表";
        Width = 940;
        Height = 560;
        MinimumSize = new Size(760, 430);
        BackColor = HistoryPalette.Surface;
        ForeColor = HistoryPalette.PrimaryText;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        ConfigureControls();
        Controls.AddRange([_range, _refresh, _summary, _emptyHint, _chart]);
        Load += (_, _) => RefreshHistory();
        Resize += (_, _) => LayoutControls();
        LayoutControls();
    }

    private void ConfigureControls()
    {
        _range.DropDownStyle = ComboBoxStyle.DropDownList;
        _range.FlatStyle = FlatStyle.Flat;
        _range.BackColor = HistoryPalette.Panel;
        _range.ForeColor = HistoryPalette.PrimaryText;
        _range.Items.AddRange([
            new HistoryRangeOption("最近 1 小时", TimeSpan.FromHours(1)),
            new HistoryRangeOption("最近 6 小时", TimeSpan.FromHours(6)),
            new HistoryRangeOption("最近 24 小时", TimeSpan.FromHours(24)),
            new HistoryRangeOption("最近 7 天", TimeSpan.FromDays(7))
        ]);
        _range.SelectedIndex = 2;
        _range.SelectedIndexChanged += (_, _) => RefreshHistory();

        _refresh.Text = "刷新";
        _refresh.FlatStyle = FlatStyle.Flat;
        _refresh.BackColor = HistoryPalette.Button;
        _refresh.ForeColor = HistoryPalette.PrimaryText;
        _refresh.FlatAppearance.BorderColor = HistoryPalette.Border;
        _refresh.Click += (_, _) => RefreshHistory();

        _summary.AutoSize = false;
        _summary.ForeColor = HistoryPalette.SecondaryText;
        _summary.TextAlign = ContentAlignment.MiddleLeft;

        _emptyHint.AutoSize = false;
        _emptyHint.ForeColor = HistoryPalette.MutedText;
        _emptyHint.TextAlign = ContentAlignment.MiddleCenter;
        _emptyHint.Text = "暂无历史记录";
        _emptyHint.Visible = false;

        _chart.BackColor = HistoryPalette.Surface;
    }

    private void LayoutControls()
    {
        const int margin = 18;
        _range.SetBounds(margin, margin, 132, 30);
        _refresh.SetBounds(_range.Right + 10, margin, 76, 30);
        _summary.SetBounds(_refresh.Right + 14, margin, Math.Max(80, ClientSize.Width - _refresh.Right - margin - 14), 30);
        _chart.SetBounds(margin, 64, Math.Max(200, ClientSize.Width - margin * 2), Math.Max(220, ClientSize.Height - 82));
        _emptyHint.Bounds = _chart.Bounds;
    }

    private void RefreshHistory()
    {
        if (_range.SelectedItem is not HistoryRangeOption option)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var since = now - option.Duration;
        var entries = _historyStore.GetEntries(since);
        _chart.SetData(entries, since, now);
        _emptyHint.Visible = entries.Count == 0;
        _summary.Text = BuildSummary(entries, option, now);
    }

    private static string BuildSummary(IReadOnlyList<BatteryHistoryEntry> entries, HistoryRangeOption option, DateTimeOffset now)
    {
        if (entries.Count == 0)
        {
            return $"{option.Label} 没有记录";
        }

        var durations = EstimateDurations(entries, now);
        var latest = entries[^1];
        var latestPercent = latest.Percent is int percent ? $"{percent}%" : "未知";
        return $"最新 {latestPercent} · 充电 {FormatDuration(durations.Charging)} · 使用 {FormatDuration(durations.Using)} · 休眠 {FormatDuration(durations.Sleeping)}";
    }

    private static HistoryDurations EstimateDurations(IReadOnlyList<BatteryHistoryEntry> entries, DateTimeOffset now)
    {
        var charging = TimeSpan.Zero;
        var usingTime = TimeSpan.Zero;
        var sleeping = TimeSpan.Zero;
        for (var index = 0; index < entries.Count; index++)
        {
            var start = entries[index].RecordedAt;
            var end = index < entries.Count - 1 ? entries[index + 1].RecordedAt : now;
            var duration = end - start;
            if (duration <= TimeSpan.Zero)
            {
                continue;
            }

            if (duration > TimeSpan.FromSeconds(5))
            {
                duration = TimeSpan.FromSeconds(1);
            }

            switch (entries[index].StateGroup)
            {
                case BatteryHistoryStateGroups.Charging:
                    charging += duration;
                    break;
                case BatteryHistoryStateGroups.Sleeping:
                    sleeping += duration;
                    break;
                default:
                    usingTime += duration;
                    break;
            }
        }

        return new HistoryDurations(charging, usingTime, sleeping);
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}时{value.Minutes:D2}分";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes}分{value.Seconds:D2}秒";
        }

        return $"{Math.Max(0, (int)value.TotalSeconds)}秒";
    }

    private sealed record HistoryRangeOption(string Label, TimeSpan Duration)
    {
        public override string ToString() => Label;
    }

    private readonly record struct HistoryDurations(TimeSpan Charging, TimeSpan Using, TimeSpan Sleeping);

    private sealed class BatteryHistoryChart : Control
    {
        private IReadOnlyList<BatteryHistoryEntry> _entries = [];
        private DateTimeOffset _since;
        private DateTimeOffset _until;

        public BatteryHistoryChart()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void SetData(IReadOnlyList<BatteryHistoryEntry> entries, DateTimeOffset since, DateTimeOffset until)
        {
            _entries = entries;
            _since = since;
            _until = until;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(HistoryPalette.Surface);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var border = new Pen(HistoryPalette.Border, 1F);
            e.Graphics.DrawRectangle(border, bounds);

            var plot = new Rectangle(54, 24, Math.Max(80, Width - 78), Math.Max(120, Height - 92));
            DrawGrid(e.Graphics, plot);
            DrawStateBand(e.Graphics, plot);
            DrawPercentLine(e.Graphics, plot);
            DrawAxisText(e.Graphics, plot);
            DrawLegend(e.Graphics);
        }

        private void DrawGrid(Graphics graphics, Rectangle plot)
        {
            using var plotFill = new SolidBrush(HistoryPalette.Panel);
            using var gridPen = new Pen(HistoryPalette.Grid, 1F);
            using var axisPen = new Pen(HistoryPalette.Axis, 1.2F);
            graphics.FillRectangle(plotFill, plot);

            for (var value = 0; value <= 100; value += 25)
            {
                var y = PercentToY(plot, value);
                var labelY = (int)Math.Round(y) - 9;
                graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(
                    graphics,
                    $"{value}%",
                    Font,
                    new Rectangle(2, labelY, 48, 18),
                    HistoryPalette.MutedText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            graphics.DrawRectangle(axisPen, plot);
        }

        private void DrawPercentLine(Graphics graphics, Rectangle plot)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            using var linePen = new Pen(HistoryPalette.PercentLine, 2.2F)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var step = Math.Max(1, _entries.Count / Math.Max(1, plot.Width * 2));
            using var path = new GraphicsPath();
            var hasFigure = false;

            for (var index = 0; index < _entries.Count; index += step)
            {
                var entry = _entries[index];
                if (entry.Percent is not int percent)
                {
                    hasFigure = false;
                    continue;
                }

                var point = new PointF(TimeToX(plot, entry.RecordedAt), PercentToY(plot, percent));
                if (!hasFigure)
                {
                    path.StartFigure();
                    path.AddLine(point, point);
                    hasFigure = true;
                    continue;
                }

                var last = path.PathPoints[^1];
                path.AddLine(last, point);
            }

            if (path.PointCount > 1)
            {
                graphics.DrawPath(linePen, path);
            }
        }

        private void DrawStateBand(Graphics graphics, Rectangle plot)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var band = new Rectangle(plot.Left, plot.Bottom + 18, plot.Width, 16);
            using var border = new Pen(HistoryPalette.Border, 1F);
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                var nextTime = index < _entries.Count - 1 ? _entries[index + 1].RecordedAt : _until;
                var left = TimeToX(band, entry.RecordedAt);
                var right = TimeToX(band, nextTime);
                if (right <= left)
                {
                    right = left + 1;
                }

                using var brush = new SolidBrush(StateColor(entry.StateGroup));
                graphics.FillRectangle(brush, left, band.Top, right - left, band.Height);
            }

            graphics.DrawRectangle(border, band);
        }

        private void DrawAxisText(Graphics graphics, Rectangle plot)
        {
            var bandTop = plot.Bottom + 38;
            TextRenderer.DrawText(graphics, _since.ToString("MM-dd HH:mm"), Font, new Rectangle(plot.Left - 20, bandTop, 130, 22), HistoryPalette.MutedText);
            TextRenderer.DrawText(
                graphics,
                _since.AddMilliseconds((_until - _since).TotalMilliseconds / 2).ToString("MM-dd HH:mm"),
                Font,
                new Rectangle(plot.Left + plot.Width / 2 - 65, bandTop, 130, 22),
                HistoryPalette.MutedText,
                TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(
                graphics,
                _until.ToString("MM-dd HH:mm"),
                Font,
                new Rectangle(plot.Right - 110, bandTop, 130, 22),
                HistoryPalette.MutedText,
                TextFormatFlags.Right);
        }

        private void DrawLegend(Graphics graphics)
        {
            DrawLegendItem(graphics, 58, 2, HistoryPalette.Charging, "充电");
            DrawLegendItem(graphics, 128, 2, HistoryPalette.Using, "使用");
            DrawLegendItem(graphics, 198, 2, HistoryPalette.Sleeping, "休眠/离线");
        }

        private void DrawLegendItem(Graphics graphics, int x, int y, Color color, string text)
        {
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, x, y + 6, 12, 8);
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(x + 18, y, 82, 22), HistoryPalette.SecondaryText);
        }

        private float TimeToX(Rectangle plot, DateTimeOffset time)
        {
            var total = Math.Max(1, (_until - _since).TotalMilliseconds);
            var offset = Math.Clamp((time - _since).TotalMilliseconds / total, 0, 1);
            return (float)(plot.Left + plot.Width * offset);
        }

        private static float PercentToY(Rectangle plot, int percent)
        {
            var value = Math.Clamp(percent, 0, 100);
            return plot.Bottom - (plot.Height * value / 100F);
        }

        private static Color StateColor(string stateGroup) =>
            stateGroup switch
            {
                BatteryHistoryStateGroups.Charging => HistoryPalette.Charging,
                BatteryHistoryStateGroups.Sleeping => HistoryPalette.Sleeping,
                _ => HistoryPalette.Using
            };
    }

    private static class HistoryPalette
    {
        public static readonly Color Surface = Color.FromArgb(17, 19, 22);
        public static readonly Color Panel = Color.FromArgb(27, 31, 35);
        public static readonly Color Border = Color.FromArgb(55, 60, 66);
        public static readonly Color Grid = Color.FromArgb(42, 48, 54);
        public static readonly Color Axis = Color.FromArgb(92, 101, 110);
        public static readonly Color PrimaryText = Color.FromArgb(242, 246, 248);
        public static readonly Color SecondaryText = Color.FromArgb(170, 181, 190);
        public static readonly Color MutedText = Color.FromArgb(116, 128, 138);
        public static readonly Color Button = Color.FromArgb(33, 37, 42);
        public static readonly Color PercentLine = Color.FromArgb(75, 231, 146);
        public static readonly Color Charging = Color.FromArgb(255, 220, 4);
        public static readonly Color Using = Color.FromArgb(45, 214, 129);
        public static readonly Color Sleeping = Color.FromArgb(145, 155, 163);
    }
}
