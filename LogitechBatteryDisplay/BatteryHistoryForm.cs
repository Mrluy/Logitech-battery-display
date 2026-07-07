using System.Drawing.Drawing2D;
using Microsoft.Data.Sqlite;

namespace LogitechBatteryDisplay;

internal sealed class BatteryHistoryForm : Form
{
    private readonly BatteryHistoryStore _historyStore;
    private readonly ComboBox _range = new();
    private readonly DateTimePicker _customStart = new();
    private readonly Label _customRangeSeparator = new();
    private readonly DateTimePicker _customEnd = new();
    private readonly Button _refresh = new();
    private readonly Label _summary = new();
    private readonly Label _emptyHint = new();
    private readonly BatteryHistoryChart _chart = new();
    private int _refreshRequestId;

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
        Controls.AddRange([_range, _customStart, _customRangeSeparator, _customEnd, _refresh, _summary, _emptyHint, _chart]);
        Load += async (_, _) => await RefreshHistoryAsync();
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
            new HistoryRangeOption("最近 2 小时", TimeSpan.FromHours(2)),
            new HistoryRangeOption("最近 4 小时", TimeSpan.FromHours(4)),
            new HistoryRangeOption("最近 6 小时", TimeSpan.FromHours(6)),
            new HistoryRangeOption("最近 12 小时", TimeSpan.FromHours(12)),
            new HistoryRangeOption("最近 24 小时", TimeSpan.FromHours(24)),
            new HistoryRangeOption("最近 7 天", TimeSpan.FromDays(7)),
            new HistoryRangeOption("自定义范围", null)
        ]);
        _range.SelectedIndex = 5;
        _range.SelectedIndexChanged += async (_, _) =>
        {
            LayoutControls();
            await RefreshHistoryAsync();
        };

        var now = DateTime.Now;
        ConfigureCustomDateTimePicker(_customStart, now.AddHours(-24));
        ConfigureCustomDateTimePicker(_customEnd, now);
        _customStart.ValueChanged += async (_, _) => await RefreshCustomRangeAsync();
        _customEnd.ValueChanged += async (_, _) => await RefreshCustomRangeAsync();

        _customRangeSeparator.AutoSize = false;
        _customRangeSeparator.ForeColor = HistoryPalette.SecondaryText;
        _customRangeSeparator.TextAlign = ContentAlignment.MiddleCenter;
        _customRangeSeparator.Text = "至";

        _refresh.Text = "刷新";
        _refresh.FlatStyle = FlatStyle.Flat;
        _refresh.BackColor = HistoryPalette.Button;
        _refresh.ForeColor = HistoryPalette.PrimaryText;
        _refresh.FlatAppearance.BorderColor = HistoryPalette.Border;
        _refresh.Click += async (_, _) => await RefreshHistoryAsync();

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
        var isCustom = IsCustomRangeSelected();
        _range.SetBounds(margin, margin, 132, 30);

        var nextX = _range.Right + 10;
        _customStart.Visible = isCustom;
        _customRangeSeparator.Visible = isCustom;
        _customEnd.Visible = isCustom;
        if (isCustom)
        {
            _customStart.SetBounds(nextX, margin, 164, 30);
            _customRangeSeparator.SetBounds(_customStart.Right + 6, margin, 18, 30);
            _customEnd.SetBounds(_customRangeSeparator.Right + 6, margin, 164, 30);
            nextX = _customEnd.Right + 10;
        }

        _refresh.SetBounds(nextX, margin, 76, 30);
        _summary.SetBounds(_refresh.Right + 14, margin, Math.Max(80, ClientSize.Width - _refresh.Right - margin - 14), 30);
        _chart.SetBounds(margin, 64, Math.Max(200, ClientSize.Width - margin * 2), Math.Max(220, ClientSize.Height - 82));
        _emptyHint.Bounds = _chart.Bounds;
    }

    private async Task RefreshHistoryAsync()
    {
        if (_range.SelectedItem is not HistoryRangeOption option)
        {
            return;
        }

        var requestId = ++_refreshRequestId;
        var now = DateTimeOffset.Now;
        if (!TryGetSelectedRange(option, now, out var since, out var until, out var rangeError))
        {
            _chart.SetData([], now, now);
            _emptyHint.Text = rangeError;
            _emptyHint.Visible = true;
            _summary.Text = rangeError;
            _refresh.Enabled = true;
            return;
        }

        _refresh.Enabled = false;
        _emptyHint.Visible = false;
        _emptyHint.Text = "暂无历史记录";
        _summary.Text = $"{option.Label} 正在读取...";

        try
        {
            var entries = await Task.Run(() => _historyStore.GetEntries(since, until));
            if (IsDisposed || requestId != _refreshRequestId)
            {
                return;
            }

            _chart.SetData(entries, since, until);
            _emptyHint.Visible = entries.Count == 0;
            _summary.Text = BuildSummary(entries, option.Label, until);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException or FormatException)
        {
            if (IsDisposed || requestId != _refreshRequestId)
            {
                return;
            }

            _chart.SetData([], since, now);
            _emptyHint.Text = "历史记录读取失败";
            _emptyHint.Visible = true;
            _summary.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed && requestId == _refreshRequestId)
            {
                _refresh.Enabled = true;
            }
        }
    }

    private static string BuildSummary(IReadOnlyList<BatteryHistoryEntry> entries, string rangeLabel, DateTimeOffset until)
    {
        if (entries.Count == 0)
        {
            return $"{rangeLabel} 没有记录";
        }

        var durations = EstimateDurations(entries, until);
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

    private async Task RefreshCustomRangeAsync()
    {
        if (IsCustomRangeSelected())
        {
            await RefreshHistoryAsync();
        }
    }

    private bool TryGetSelectedRange(
        HistoryRangeOption option,
        DateTimeOffset now,
        out DateTimeOffset since,
        out DateTimeOffset until,
        out string rangeError)
    {
        rangeError = string.Empty;
        if (!option.IsCustom)
        {
            until = now;
            since = now - option.Duration!.Value;
            return true;
        }

        since = new DateTimeOffset(_customStart.Value);
        until = new DateTimeOffset(_customEnd.Value);
        if (until > since)
        {
            return true;
        }

        rangeError = "结束时间必须晚于开始时间";
        return false;
    }

    private bool IsCustomRangeSelected() =>
        _range.SelectedItem is HistoryRangeOption { IsCustom: true };

    private static void ConfigureCustomDateTimePicker(DateTimePicker picker, DateTime value)
    {
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy-MM-dd HH:mm:ss";
        picker.Value = value;
        picker.Visible = false;
    }

    private sealed record HistoryRangeOption(string Label, TimeSpan? Duration)
    {
        public bool IsCustom => Duration is null;

        public override string ToString() => Label;
    }

    private readonly record struct HistoryDurations(TimeSpan Charging, TimeSpan Using, TimeSpan Sleeping);

    private sealed class BatteryHistoryChart : Control
    {
        private IReadOnlyList<BatteryHistoryEntry> _entries = [];
        private DateTimeOffset _since;
        private DateTimeOffset _until;
        private int _selectedIndex = -1;
        private bool _isDraggingSelection;

        public BatteryHistoryChart()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Cross;
        }

        public void SetData(IReadOnlyList<BatteryHistoryEntry> entries, DateTimeOffset since, DateTimeOffset until)
        {
            var selectedTime = SelectedEntry?.RecordedAt;
            _entries = entries;
            _since = since;
            _until = until;
            _selectedIndex = ResolveSelectedIndex(selectedTime);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(HistoryPalette.Surface);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var border = new Pen(HistoryPalette.Border, 1F);
            e.Graphics.DrawRectangle(border, bounds);

            var plot = GetPlotRectangle();
            DrawGrid(e.Graphics, plot);
            DrawStateBand(e.Graphics, plot);
            DrawPercentLine(e.Graphics, plot);
            DrawSelection(e.Graphics, plot);
            DrawAxisText(e.Graphics, plot);
            DrawLegend(e.Graphics);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _entries.Count == 0)
            {
                return;
            }

            var plot = GetPlotRectangle();
            var band = GetStateBandRectangle(plot);
            if (!plot.Contains(e.Location) && !band.Contains(e.Location) && !IsNearSelectedLine(plot, e.X))
            {
                return;
            }

            Capture = true;
            _isDraggingSelection = true;
            SelectNearestEntry(plot, e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDraggingSelection && _entries.Count > 0)
            {
                SelectNearestEntry(GetPlotRectangle(), e.X);
                return;
            }

            var plot = GetPlotRectangle();
            var band = GetStateBandRectangle(plot);
            Cursor = plot.Contains(e.Location) || band.Contains(e.Location) || IsNearSelectedLine(plot, e.X)
                ? Cursors.SizeWE
                : Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDraggingSelection = false;
                Capture = false;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_isDraggingSelection)
            {
                Cursor = Cursors.Cross;
            }
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

            using var chargingPen = CreateLinePen(HistoryPalette.Charging);
            using var usingPen = CreateLinePen(HistoryPalette.Using);
            using var sleepingPen = CreateLinePen(HistoryPalette.Sleeping);
            using var unknownPen = CreateLinePen(HistoryPalette.UnknownLine);
            var step = Math.Max(1, _entries.Count / Math.Max(1, plot.Width * 2));
            BatteryHistoryEntry? lastEntry = null;
            PointF? lastPoint = null;

            for (var index = 0; index < _entries.Count; index += step)
            {
                DrawPercentLinePoint(graphics, plot, _entries[index], ref lastEntry, ref lastPoint, chargingPen, usingPen, sleepingPen, unknownPen);
            }

            if ((_entries.Count - 1) % step != 0)
            {
                DrawPercentLinePoint(graphics, plot, _entries[^1], ref lastEntry, ref lastPoint, chargingPen, usingPen, sleepingPen, unknownPen);
            }
        }

        private void DrawPercentLinePoint(
            Graphics graphics,
            Rectangle plot,
            BatteryHistoryEntry entry,
            ref BatteryHistoryEntry? lastEntry,
            ref PointF? lastPoint,
            Pen chargingPen,
            Pen usingPen,
            Pen sleepingPen,
            Pen unknownPen)
        {
            if (entry.Percent is not int percent)
            {
                lastEntry = null;
                lastPoint = null;
                return;
            }

            var point = new PointF(TimeToX(plot, entry.RecordedAt), PercentToY(plot, percent));
            if (lastEntry is not null && lastPoint is PointF previous)
            {
                graphics.DrawLine(PenFor(entry.StateGroup, chargingPen, usingPen, sleepingPen, unknownPen), previous, point);
            }

            lastEntry = entry;
            lastPoint = point;
        }

        private void DrawStateBand(Graphics graphics, Rectangle plot)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var band = GetStateBandRectangle(plot);
            using var border = new Pen(HistoryPalette.Border, 1F);
            using var chargingBrush = new SolidBrush(HistoryPalette.Charging);
            using var usingBrush = new SolidBrush(HistoryPalette.Using);
            using var sleepingBrush = new SolidBrush(HistoryPalette.Sleeping);

            var activeState = string.Empty;
            var activeLeft = 0F;
            var activeRight = 0F;
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

                if (activeState.Length == 0)
                {
                    activeState = entry.StateGroup;
                    activeLeft = left;
                    activeRight = right;
                    continue;
                }

                if (entry.StateGroup == activeState && left <= activeRight + 0.5F)
                {
                    activeRight = Math.Max(activeRight, right);
                    continue;
                }

                FillStateSegment(graphics, band, BrushFor(activeState, chargingBrush, usingBrush, sleepingBrush), activeLeft, activeRight);
                activeState = entry.StateGroup;
                activeLeft = left;
                activeRight = right;
            }

            if (activeState.Length > 0)
            {
                FillStateSegment(graphics, band, BrushFor(activeState, chargingBrush, usingBrush, sleepingBrush), activeLeft, activeRight);
            }

            graphics.DrawRectangle(border, band);
        }

        private void DrawSelection(Graphics graphics, Rectangle plot)
        {
            var selected = SelectedEntry;
            if (selected is null)
            {
                return;
            }

            var band = GetStateBandRectangle(plot);
            var x = TimeToX(plot, selected.RecordedAt);
            var stateColor = ColorFor(selected.StateGroup);
            using var linePen = new Pen(HistoryPalette.SelectionLine, 1F);
            using var pointFill = new SolidBrush(HistoryPalette.Surface);
            using var pointBorder = new Pen(stateColor, 1.4F);

            graphics.DrawLine(linePen, x, plot.Top, x, band.Bottom);
            if (selected.Percent is int percent)
            {
                var y = PercentToY(plot, percent);
                graphics.FillEllipse(pointFill, x - 4F, y - 4F, 8F, 8F);
                graphics.DrawEllipse(pointBorder, x - 4F, y - 4F, 8F, 8F);
            }

            DrawSelectionLabel(graphics, plot, selected, x, stateColor);
        }

        private void DrawSelectionLabel(Graphics graphics, Rectangle plot, BatteryHistoryEntry selected, float markerX, Color stateColor)
        {
            var percentText = selected.Percent is int percent ? $"{percent}%" : "未知";
            var text = $"{selected.RecordedAt:MM-dd HH:mm:ss}  {percentText}  {StateText(selected.StateGroup)}";
            var textSize = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
            var labelWidth = Math.Min(plot.Width - 12, textSize.Width + 18);
            var labelHeight = 24;
            var labelX = (int)Math.Round(markerX + 10);
            if (labelX + labelWidth > plot.Right - 6)
            {
                labelX = (int)Math.Round(markerX - labelWidth - 10);
            }

            labelX = Math.Clamp(labelX, plot.Left + 6, Math.Max(plot.Left + 6, plot.Right - labelWidth - 6));
            var labelBounds = new Rectangle(labelX, plot.Top + 8, labelWidth, labelHeight);
            using var fill = new SolidBrush(HistoryPalette.SelectionPanel);
            using var border = new Pen(stateColor, 1F);
            graphics.FillRectangle(fill, labelBounds);
            graphics.DrawRectangle(border, labelBounds);
            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                new Rectangle(labelBounds.Left + 9, labelBounds.Top + 1, labelBounds.Width - 18, labelBounds.Height - 2),
                HistoryPalette.PrimaryText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
            var x = 58;
            x = DrawLegendItem(graphics, x, 2, HistoryPalette.Charging, "充电");
            x = DrawLegendItem(graphics, x, 2, HistoryPalette.Using, "使用");
            DrawLegendItem(graphics, x, 2, HistoryPalette.Sleeping, "休眠/离线");
        }

        private int DrawLegendItem(Graphics graphics, int x, int y, Color color, string text)
        {
            const int swatchWidth = 12;
            const int swatchTextGap = 6;
            const int itemGap = 28;
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, x, y + 6, swatchWidth, 8);

            var textSize = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
            var textBounds = new Rectangle(x + swatchWidth + swatchTextGap, y, textSize.Width, 22);
            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                textBounds,
                HistoryPalette.SecondaryText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return textBounds.Right + itemGap;
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

        private static void FillStateSegment(Graphics graphics, Rectangle band, Brush brush, float left, float right)
        {
            if (right <= left)
            {
                right = left + 1;
            }

            graphics.FillRectangle(brush, left, band.Top, right - left, band.Height);
        }

        private BatteryHistoryEntry? SelectedEntry =>
            _selectedIndex >= 0 && _selectedIndex < _entries.Count ? _entries[_selectedIndex] : null;

        private Rectangle GetPlotRectangle() =>
            new(54, 24, Math.Max(80, Width - 78), Math.Max(120, Height - 92));

        private static Rectangle GetStateBandRectangle(Rectangle plot) =>
            new(plot.Left, plot.Bottom + 18, plot.Width, 16);

        private int ResolveSelectedIndex(DateTimeOffset? selectedTime)
        {
            if (_entries.Count == 0)
            {
                return -1;
            }

            return selectedTime is DateTimeOffset time ? FindNearestEntryIndex(time) : _entries.Count - 1;
        }

        private void SelectNearestEntry(Rectangle plot, int x)
        {
            var selectedTime = TimeFromX(plot, x);
            var index = FindNearestEntryIndex(selectedTime);
            if (index == _selectedIndex)
            {
                return;
            }

            _selectedIndex = index;
            Invalidate();
        }

        private int FindNearestEntryIndex(DateTimeOffset time)
        {
            if (_entries.Count == 0)
            {
                return -1;
            }

            var targetTicks = time.UtcTicks;
            var low = 0;
            var high = _entries.Count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                var midTicks = _entries[mid].RecordedAt.UtcTicks;
                if (midTicks < targetTicks)
                {
                    low = mid + 1;
                }
                else if (midTicks > targetTicks)
                {
                    high = mid - 1;
                }
                else
                {
                    return mid;
                }
            }

            if (low <= 0)
            {
                return 0;
            }

            if (low >= _entries.Count)
            {
                return _entries.Count - 1;
            }

            var previousDelta = Math.Abs(targetTicks - _entries[low - 1].RecordedAt.UtcTicks);
            var nextDelta = Math.Abs(_entries[low].RecordedAt.UtcTicks - targetTicks);
            return previousDelta <= nextDelta ? low - 1 : low;
        }

        private bool IsNearSelectedLine(Rectangle plot, int x)
        {
            var selected = SelectedEntry;
            return selected is not null && Math.Abs(TimeToX(plot, selected.RecordedAt) - x) <= 8F;
        }

        private static Brush BrushFor(string stateGroup, Brush chargingBrush, Brush usingBrush, Brush sleepingBrush) =>
            stateGroup switch
            {
                BatteryHistoryStateGroups.Charging => chargingBrush,
                BatteryHistoryStateGroups.Sleeping => sleepingBrush,
                _ => usingBrush
            };

        private static Pen CreateLinePen(Color color) =>
            new(color, 0.55F)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

        private static Pen PenFor(string stateGroup, Pen chargingPen, Pen usingPen, Pen sleepingPen, Pen unknownPen) =>
            stateGroup switch
            {
                BatteryHistoryStateGroups.Charging => chargingPen,
                BatteryHistoryStateGroups.Sleeping => sleepingPen,
                BatteryHistoryStateGroups.Using => usingPen,
                _ => unknownPen
            };

        private static Color ColorFor(string stateGroup) =>
            stateGroup switch
            {
                BatteryHistoryStateGroups.Charging => HistoryPalette.Charging,
                BatteryHistoryStateGroups.Sleeping => HistoryPalette.Sleeping,
                BatteryHistoryStateGroups.Using => HistoryPalette.Using,
                _ => HistoryPalette.UnknownLine
            };

        private static string StateText(string stateGroup) =>
            stateGroup switch
            {
                BatteryHistoryStateGroups.Charging => "充电",
                BatteryHistoryStateGroups.Sleeping => "休眠/离线",
                BatteryHistoryStateGroups.Using => "使用",
                _ => "未知"
            };

        private DateTimeOffset TimeFromX(Rectangle plot, int x)
        {
            var total = Math.Max(1, (_until - _since).TotalMilliseconds);
            var offset = Math.Clamp((x - plot.Left) / (double)Math.Max(1, plot.Width), 0D, 1D);
            return _since.AddMilliseconds(total * offset);
        }
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
        public static readonly Color UnknownLine = Color.FromArgb(112, 122, 132);
        public static readonly Color SelectionLine = Color.FromArgb(230, 245, 249, 252);
        public static readonly Color SelectionPanel = Color.FromArgb(235, 22, 25, 29);
    }
}
