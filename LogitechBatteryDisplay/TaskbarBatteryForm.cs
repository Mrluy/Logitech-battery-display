using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LogitechBatteryDisplay;

internal sealed class TaskbarBatteryForm : Form
{
    private static readonly Color Transparent = Color.FromArgb(255, 1, 2, 3);
    private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");
    private string? _targetScreenDeviceName;

    public TaskbarBatteryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Width = 60;
        Height = 34;
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Transparent;
        TransparencyKey = Transparent;
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Bold, GraphicsUnit.Point);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;
            const int wsExTransparent = 0x00000020;
            var cp = base.CreateParams;
            cp.ExStyle |= wsExNoActivate | wsExToolWindow | wsExTransparent;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void UpdateSnapshot(BatterySnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateSnapshot(snapshot));
            return;
        }

        _snapshot = snapshot;
        Invalidate();
    }

    public void ShowPinned()
    {
        Reposition();
        Show();
    }

    public void SetTargetScreen(string? deviceName)
    {
        _targetScreenDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;
        if (Visible)
        {
            Reposition();
        }
    }

    public void Reposition()
    {
        var screen = ResolveTargetScreen();
        if (screen is null)
        {
            return;
        }

        var taskbar = GetTaskbarBounds(screen);
        if (taskbar.Width <= 0 || taskbar.Height <= 0)
        {
            var workingArea = screen.WorkingArea;
            Location = new Point(workingArea.Right - Width - 24, workingArea.Bottom - Height - 12);
            return;
        }

        if (TryGetTaskbarAnchor(screen, out var taskbarAnchor) && taskbarAnchor.Bounds.Width > 0 && taskbarAnchor.Bounds.Height > 0)
        {
            Location = GetTaskbarAnchorLocation(taskbarAnchor, taskbar);
            return;
        }

        const int reservedRightForTray = 520;
        const int margin = 12;
        if (taskbar.Height <= taskbar.Width)
        {
            var x = taskbar.Right - Width - reservedRightForTray;
            if (x < taskbar.Left + margin)
            {
                x = taskbar.Right - Width - margin;
            }

            Location = new Point(x, taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2));
            return;
        }

        Location = new Point(
            taskbar.Left + Math.Max(0, (taskbar.Width - Width) / 2),
            taskbar.Bottom - Height - reservedRightForTray);
    }

    private Screen? ResolveTargetScreen()
    {
        var screens = Screen.AllScreens;
        if (!string.IsNullOrWhiteSpace(_targetScreenDeviceName))
        {
            var selected = screens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, _targetScreenDeviceName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return Screen.PrimaryScreen ?? screens.FirstOrDefault();
    }

    private Point GetTaskbarAnchorLocation(TaskbarAnchorCandidate anchorCandidate, Rectangle taskbar)
    {
        const int horizontalRightGap = 24;
        const int verticalBottomGap = 14;
        var anchor = anchorCandidate.Bounds;
        if (taskbar.Height <= taskbar.Width)
        {
            var rightEdge = anchorCandidate.IsNotificationAnchor ? anchor.Left : anchor.Right;
            return new Point(
                rightEdge - Width - horizontalRightGap,
                anchor.Top + Math.Max(0, (anchor.Height - Height) / 2));
        }

        var bottomEdge = anchorCandidate.IsNotificationAnchor ? anchor.Top : anchor.Bottom;
        return new Point(
            anchor.Left + Math.Max(0, (anchor.Width - Width) / 2),
            bottomEdge - Height - verticalBottomGap);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Transparent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        e.Graphics.Clear(Transparent);

        var percent = _snapshot.Percent;
        var accent = AccentFor(percent, _snapshot.ChargeState);
        var percentText = percent is int value ? $"{value}%" : "--%";

        DrawBattery(e.Graphics, new Rectangle(3, 7, 49, 20), percent, accent, percentText, Font);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reposition();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
        {
            Reposition();
        }
    }

    private static Rectangle GetTaskbarBounds(Screen screen)
    {
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;

        if (work.Top > bounds.Top)
        {
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        }

        if (work.Bottom < bounds.Bottom)
        {
            return Rectangle.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        }

        if (work.Left > bounds.Left)
        {
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        }

        if (work.Right < bounds.Right)
        {
            return Rectangle.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        }

        return Rectangle.Empty;
    }

    private static bool TryGetTaskbarAnchor(Screen screen, out TaskbarAnchorCandidate bounds)
    {
        bounds = default;
        var candidates = new List<TaskbarAnchorCandidate>();
        EnumWindows(
            (hWnd, _) =>
            {
                var className = GetWindowClassName(hWnd);
                if (className != "Shell_TrayWnd" && className != "Shell_SecondaryTrayWnd")
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out var taskbarRect))
                {
                    return true;
                }

                var taskbarBounds = Rectangle.FromLTRB(
                    taskbarRect.Left,
                    taskbarRect.Top,
                    taskbarRect.Right,
                    taskbarRect.Bottom);
                var score = IntersectionArea(taskbarBounds, screen.Bounds);
                if (score <= 0)
                {
                    return true;
                }

                var hasNotificationAnchor = TryGetNotificationAreaBounds(hWnd, out var notificationAnchor);
                var hasTaskList = TryGetChildTaskListBounds(hWnd, out var taskList);
                if (hasNotificationAnchor)
                {
                    candidates.Add(new TaskbarAnchorCandidate(notificationAnchor, score, true));
                }
                else if (hasTaskList)
                {
                    candidates.Add(new TaskbarAnchorCandidate(taskList, score, false));
                }

                return true;
            },
            IntPtr.Zero);

        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            var score = candidate.Score + (candidate.IsNotificationAnchor ? 1 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                bounds = candidate;
            }
        }

        return bestScore > 0 && bounds.Bounds.Width > 0 && bounds.Bounds.Height > 0;
    }

    private static bool TryGetNotificationAreaBounds(IntPtr taskbar, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        var trayNotify = IntPtr.Zero;
        EnumChildWindows(
            taskbar,
            (hWnd, _) =>
            {
                if (GetWindowClassName(hWnd) == "TrayNotifyWnd")
                {
                    trayNotify = hWnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        if (trayNotify == IntPtr.Zero || !GetWindowRect(trayNotify, out var trayRect))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(trayRect.Left, trayRect.Top, trayRect.Right, trayRect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryGetChildTaskListBounds(IntPtr taskbar, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        var taskList = IntPtr.Zero;
        EnumChildWindows(
            taskbar,
            (hWnd, _) =>
            {
                var className = GetWindowClassName(hWnd);
                if (className is "MSTaskListWClass" or "MSTaskSwWClass")
                {
                    taskList = hWnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        if (taskList == IntPtr.Zero || !GetWindowRect(taskList, out var rect))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static int IntersectionArea(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        return intersection.Width <= 0 || intersection.Height <= 0 ? 0 : intersection.Width * intersection.Height;
    }

    private readonly struct TaskbarAnchorCandidate(Rectangle bounds, int score, bool isNotificationAnchor)
    {
        public Rectangle Bounds { get; } = bounds;

        public int Score { get; } = score;

        public bool IsNotificationAnchor { get; } = isNotificationAnchor;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString();
    }

    private static void DrawBattery(Graphics graphics, Rectangle bounds, int? percent, Color accent, string percentText, Font font)
    {
        using var outline = new Pen(Color.FromArgb(225, 238, 244, 247), 1.8F);
        using var cap = new SolidBrush(Color.FromArgb(225, 238, 244, 247));
        using var body = RoundedRect(bounds, 4);
        graphics.DrawPath(outline, body);
        graphics.FillRectangle(cap, new Rectangle(bounds.Right + 2, bounds.Top + 6, 4, 8));

        var inner = Rectangle.Inflate(bounds, -4, -4);
        if (percent is int value)
        {
            inner.Width = Math.Max(2, (int)Math.Round(inner.Width * value / 100.0));
            using var fill = new SolidBrush(accent);
            using var fillPath = RoundedRect(inner, 2);
            graphics.FillPath(fill, fillPath);
        }

        DrawCenteredPercent(graphics, bounds, percentText, font);
    }

    private static void DrawCenteredPercent(Graphics graphics, Rectangle bounds, string text, Font font)
    {
        using var path = new GraphicsPath();
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip
        };
        using var fittedFont = CreateFittedPercentFont(graphics, bounds, text, font);
        var textBounds = Rectangle.Inflate(bounds, -3, 0);
        path.AddString(
            text,
            fittedFont.FontFamily,
            (int)fittedFont.Style,
            graphics.DpiY * fittedFont.SizeInPoints / 72F,
            textBounds,
            format);

        using var halo = new Pen(Color.FromArgb(210, 4, 10, 14), 1.8F)
        {
            LineJoin = LineJoin.Round
        };
        using var fill = new SolidBrush(Color.FromArgb(248, 255, 255, 255));
        graphics.DrawPath(halo, path);
        graphics.FillPath(fill, path);
    }

    private static Font CreateFittedPercentFont(Graphics graphics, Rectangle bounds, string text, Font baseFont)
    {
        const float minimumSize = 6.8F;
        var maximumSize = Math.Min(baseFont.SizeInPoints, 8.4F);
        var targetSize = minimumSize;
        using var format = StringFormat.GenericTypographic;

        for (var size = maximumSize; size >= minimumSize; size -= 0.2F)
        {
            using var candidate = new Font(baseFont.FontFamily, size, baseFont.Style, GraphicsUnit.Point);
            var measured = graphics.MeasureString(text, candidate, bounds.Size, format);
            if (measured.Width <= bounds.Width - 7 && measured.Height <= bounds.Height - 2)
            {
                targetSize = size;
                break;
            }
        }

        return new Font(baseFont.FontFamily, targetSize, baseFont.Style, GraphicsUnit.Point);
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

    private static Color AccentFor(int? percent, BatteryChargeState state)
    {
        if (state is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge)
        {
            return Color.FromArgb(92, 170, 255);
        }

        if (percent is null)
        {
            return Color.FromArgb(168, 178, 187);
        }

        return percent.Value switch
        {
            <= 15 => Color.FromArgb(255, 99, 87),
            <= 35 => Color.FromArgb(255, 176, 73),
            _ => Color.FromArgb(45, 214, 129)
        };
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
