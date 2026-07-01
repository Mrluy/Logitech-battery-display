using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LogitechBatteryDisplay;

internal sealed class TaskbarBatteryForm : Form
{
    private static readonly Color Transparent = Color.FromArgb(255, 1, 2, 3);
    private const string TaskbarWindowMarker = "LogitechBatteryDisplay.TaskbarBatteryWindow";
    private const int CollisionGap = 8;
    private const int GwlExStyle = -20;
    private const long WsExTopMost = 0x00000008;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const int DwmwaCloaked = 14;
    private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");
    private string? _targetScreenDeviceName;

    public TaskbarBatteryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = TaskbarWindowMarker;
        Name = TaskbarWindowMarker;
        AccessibleName = TaskbarWindowMarker;
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
            Location = AvoidOccupiedTaskbarWindows(GetTaskbarAnchorLocation(taskbarAnchor, taskbar), screen, taskbar);
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

            Location = AvoidOccupiedTaskbarWindows(
                new Point(x, taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2)),
                screen,
                taskbar);
            return;
        }

        Location = AvoidOccupiedTaskbarWindows(
            new Point(
                taskbar.Left + Math.Max(0, (taskbar.Width - Width) / 2),
                taskbar.Bottom - Height - reservedRightForTray),
            screen,
            taskbar);
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
            var rightGap = anchorCandidate.IsNotificationAnchor ? 0 : horizontalRightGap;
            return new Point(
                rightEdge - Width - rightGap,
                anchor.Top + Math.Max(0, (anchor.Height - Height) / 2));
        }

        var bottomEdge = anchorCandidate.IsNotificationAnchor ? anchor.Top : anchor.Bottom;
        var bottomGap = anchorCandidate.IsNotificationAnchor ? 0 : verticalBottomGap;
        return new Point(
            anchor.Left + Math.Max(0, (anchor.Width - Width) / 2),
            bottomEdge - Height - bottomGap);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetProp(Handle, TaskbarWindowMarker, new IntPtr(1));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        RemoveProp(Handle, TaskbarWindowMarker);
        base.OnHandleDestroyed(e);
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

                var hasNotificationAnchor = TryGetNotificationAreaBounds(hWnd, taskbarBounds, out var notificationAnchor);
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

    private Point AvoidOccupiedTaskbarWindows(Point proposedLocation, Screen screen, Rectangle taskbar)
    {
        var bounds = new Rectangle(proposedLocation, Size);
        var occupied = GetOccupiedTaskbarWindows(screen, taskbar);
        for (var attempts = 0; attempts < 24; attempts++)
        {
            var blocker = occupied.FirstOrDefault(rect => IntersectsWithGap(bounds, rect, CollisionGap));
            if (blocker.IsEmpty)
            {
                break;
            }

            if (taskbar.Height <= taskbar.Width)
            {
                var nextX = blocker.Left - bounds.Width - CollisionGap;
                var minX = taskbar.Left + CollisionGap;
                if (nextX < minX || nextX == bounds.X)
                {
                    bounds.X = Math.Max(minX, nextX);
                    break;
                }

                bounds.X = nextX;
                continue;
            }

            var nextY = blocker.Top - bounds.Height - CollisionGap;
            var minY = taskbar.Top + CollisionGap;
            if (nextY < minY || nextY == bounds.Y)
            {
                bounds.Y = Math.Max(minY, nextY);
                break;
            }

            bounds.Y = nextY;
        }

        return bounds.Location;
    }

    private List<Rectangle> GetOccupiedTaskbarWindows(Screen screen, Rectangle taskbar)
    {
        var occupied = new List<Rectangle>();
        var ownHandle = IsHandleCreated ? Handle : IntPtr.Zero;
        EnumWindows(
            (hWnd, _) =>
            {
                if (hWnd == ownHandle || !GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsWith(taskbar))
                {
                    return true;
                }

                if (!IsOccupiedTaskbarWindow(hWnd, bounds, screen, taskbar))
                {
                    return true;
                }

                occupied.Add(bounds);
                return true;
            },
            IntPtr.Zero);

        return taskbar.Height <= taskbar.Width
            ? occupied.OrderByDescending(rect => rect.Left).ToList()
            : occupied.OrderByDescending(rect => rect.Top).ToList();
    }

    private static bool IsOccupiedTaskbarWindow(IntPtr hWnd, Rectangle bounds, Screen screen, Rectangle taskbar)
    {
        if (IsIgnoredCollisionWindow(hWnd, bounds, screen, taskbar))
        {
            return false;
        }

        if (IsWindowVisible(hWnd))
        {
            return true;
        }

        return IsLikelyHiddenTaskbarOverlay(hWnd, bounds, taskbar);
    }

    private static bool IsIgnoredCollisionWindow(IntPtr hWnd, Rectangle bounds, Screen screen, Rectangle taskbar)
    {
        var className = GetWindowClassName(hWnd);
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SIBTranslucentLayer" or
            "EdgeUiInputTopWndClass" or "Progman" or "WorkerW" or "Button" or
            "NotifyIconOverflowWindow" or "tooltips_class32" or "OrayUI_Shadow" or
            "Qt5QWindowPopupDropShadowSaveBits")
        {
            return true;
        }

        if (bounds.Width > 700 || bounds.Height > taskbar.Height + 60)
        {
            return true;
        }

        return IntersectionArea(bounds, screen.Bounds) <= 0;
    }

    private static bool IsLikelyHiddenTaskbarOverlay(IntPtr hWnd, Rectangle bounds, Rectangle taskbar)
    {
        if (IsDwmCloaked(hWnd))
        {
            return false;
        }

        var exStyle = GetWindowExStyle(hWnd);
        if ((exStyle & WsExTopMost) == 0)
        {
            return false;
        }

        var className = GetWindowClassName(hWnd);
        if (className.StartsWith("WindowsForms10.Window.", StringComparison.Ordinal) ||
            className.StartsWith("HwndWrapper[", StringComparison.Ordinal))
        {
            return true;
        }

        var looksLikePassiveOverlay =
            (exStyle & (WsExToolWindow | WsExNoActivate)) != 0 &&
            bounds.Width <= 700 &&
            bounds.Height <= taskbar.Height + 60;
        return looksLikePassiveOverlay;
    }

    private static bool IsDwmCloaked(IntPtr hWnd)
    {
        return DwmGetWindowAttribute(hWnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static long GetWindowExStyle(IntPtr hWnd)
    {
        return GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static bool IntersectsWithGap(Rectangle first, Rectangle second, int gap)
    {
        return Rectangle.Inflate(first, gap, gap).IntersectsWith(second);
    }

    private static bool TryGetNotificationAreaBounds(IntPtr taskbar, Rectangle taskbarBounds, out Rectangle bounds)
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

        var trayBounds = Rectangle.FromLTRB(trayRect.Left, trayRect.Top, trayRect.Right, trayRect.Bottom);
        var hiddenIconsButton = Rectangle.Empty;
        EnumChildWindows(
            trayNotify,
            (hWnd, _) =>
            {
                if (GetWindowClassName(hWnd) != "SIBTrayButton" || !GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                var buttonBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (IntersectionArea(buttonBounds, trayBounds) <= 0)
                {
                    return true;
                }

                if (hiddenIconsButton.IsEmpty || IsCloserToTaskList(buttonBounds, hiddenIconsButton, taskbarBounds))
                {
                    hiddenIconsButton = buttonBounds;
                }

                return true;
            },
            IntPtr.Zero);

        bounds = hiddenIconsButton.IsEmpty ? trayBounds : hiddenIconsButton;
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool IsCloserToTaskList(Rectangle candidate, Rectangle current, Rectangle taskbar)
    {
        if (taskbar.Height <= taskbar.Width)
        {
            return candidate.Left < current.Left;
        }

        return candidate.Top < current.Top;
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

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
