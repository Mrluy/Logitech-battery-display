using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LogitechBatteryDisplay;

internal sealed class TaskbarBatteryForm : Form
{
    private static readonly Color Transparent = Color.FromArgb(255, 1, 2, 3);
    private static readonly Size WindowSize = new(78, 34);
    private static readonly Rectangle ChargingIconBounds = new(3, 8, 13, 18);
    private static readonly Rectangle BatteryBounds = new(20, 7, 49, 20);
    private const string TaskbarWindowMarker = "LogitechBatteryDisplay.TaskbarBatteryWindow";
    private const string ToolTipWindowMarker = "LogitechBatteryDisplay.TaskbarBatteryToolTip";
    private const int CollisionGap = 8;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");
    private string? _targetScreenDeviceName;
    private bool _isPinned;
    private string _toolTipText = string.Empty;
    private readonly Dictionary<IntPtr, Rectangle> _originalTaskListBounds = new();
    private readonly BatteryToolTipForm _toolTip = new();
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 150 };

    public TaskbarBatteryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = TaskbarWindowMarker;
        Name = TaskbarWindowMarker;
        AccessibleName = TaskbarWindowMarker;
        Size = WindowSize;
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Transparent;
        TransparencyKey = Transparent;
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Bold, GraphicsUnit.Point);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        UpdateToolTipText();
        _hoverTimer.Tick += (_, _) => UpdateHoverToolTip();
        _hoverTimer.Start();

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

        var visualChanged = HasTaskbarVisualChange(_snapshot, snapshot);
        _snapshot = snapshot;
        UpdateToolTipText();
        if (visualChanged)
        {
            Invalidate();
        }
    }

    public void ShowPinned()
    {
        _isPinned = true;
        Reposition();
    }

    public void HidePinned()
    {
        _isPinned = false;
        _toolTip.Hide();
        RestoreReservedTaskLists();
        Hide();
    }

    public void SetTargetScreen(string? deviceName)
    {
        RestoreReservedTaskLists();
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
            RestoreReservedTaskLists();
            Hide();
            return;
        }

        var taskbar = GetTaskbarBounds(screen);
        if (taskbar.Width <= 0 || taskbar.Height <= 0)
        {
            RestoreReservedTaskLists();
            var workingArea = screen.WorkingArea;
            ApplyPinnedLocation(new Point(workingArea.Right - Width - 24, workingArea.Bottom - Height - 12));
            return;
        }

        if (TryGetTaskbarAnchor(screen, out var taskbarAnchor) && taskbarAnchor.Bounds.Width > 0 && taskbarAnchor.Bounds.Height > 0)
        {
            ApplyAvailableTaskbarLocation(GetTaskbarAnchorLocation(taskbarAnchor, taskbar), screen, taskbar);
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

            ApplyAvailableTaskbarLocation(
                new Point(x, taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2)),
                screen,
                taskbar);
            return;
        }

        ApplyAvailableTaskbarLocation(
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
            return screens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, _targetScreenDeviceName, StringComparison.OrdinalIgnoreCase));
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

    private void ApplyAvailableTaskbarLocation(Point proposedLocation, Screen screen, Rectangle taskbar)
    {
        if (TryFindAvailableTaskbarLocation(proposedLocation, screen, taskbar, out var location))
        {
            ApplyPinnedLocation(location);
            return;
        }

        Hide();
    }

    private void ApplyPinnedLocation(Point location)
    {
        Location = location;
        if (_isPinned && !Visible)
        {
            Show();
        }
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
        var accent = AccentFor(_snapshot);
        var percentText = percent is int value ? $"{value}%" : "--%";

        if (IsCharging(_snapshot.ChargeState))
        {
            DrawChargingIcon(e.Graphics, ChargingIconBounds, BatteryColors.ChargingGold);
        }

        DrawBattery(e.Graphics, BatteryBounds, percent, accent, percentText, Font);
    }

    private static bool HasTaskbarVisualChange(BatterySnapshot current, BatterySnapshot next) =>
        current.IsSuccess != next.IsSuccess ||
        current.Percent != next.Percent ||
        current.ChargeState != next.ChargeState;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RestoreReservedTaskLists();
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            _toolTip.Dispose();
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

    private void UpdateToolTipText()
    {
        _toolTipText = BuildToolTipText(_snapshot);
        if (_toolTip.Visible)
        {
            ShowBatteryToolTip();
        }
    }

    private void UpdateHoverToolTip()
    {
        if (!_isPinned || !Visible || !Bounds.Contains(Cursor.Position))
        {
            _toolTip.Hide();
            return;
        }

        ShowBatteryToolTip();
    }

    private void ShowBatteryToolTip()
    {
        if (string.IsNullOrWhiteSpace(_toolTipText))
        {
            _toolTip.Hide();
            return;
        }

        _toolTip.ShowText(_toolTipText, GetToolTipLocation());
    }

    private static string BuildToolTipText(BatterySnapshot snapshot)
    {
        var deviceName = string.IsNullOrWhiteSpace(snapshot.DeviceName)
            ? "罗技无线鼠标"
            : snapshot.DeviceName.Trim();
        var percentText = snapshot.Percent is int percent ? $"{percent}%" : "未知";
        return $"{deviceName} 剩余电量 {percentText}";
    }

    private Point GetToolTipLocation()
    {
        var screen = ResolveTargetScreen() ?? Screen.FromPoint(new Point(Bounds.Left + Width / 2, Bounds.Top + Height / 2));
        var screenBounds = screen.Bounds;
        var x = Bounds.Left + (Width - _toolTip.Width) / 2;
        var y = Bounds.Bottom + 6;

        if (y + _toolTip.Height > screenBounds.Bottom - 4)
        {
            y = Bounds.Top - _toolTip.Height - 6;
        }

        x = Math.Clamp(x, screenBounds.Left + 4, screenBounds.Right - _toolTip.Width - 4);
        y = Math.Clamp(y, screenBounds.Top + 4, screenBounds.Bottom - _toolTip.Height - 4);
        return new Point(x, y);
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

    private bool TryFindAvailableTaskbarLocation(Point proposedLocation, Screen screen, Rectangle taskbar, out Point location)
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

        ReserveTaskListSpace(screen, taskbar, bounds);
        location = bounds.Location;
        return true;
    }

    private List<Rectangle> GetOccupiedTaskbarWindows(Screen screen, Rectangle taskbar)
    {
        var occupied = new List<Rectangle>();
        var ownHandle = IsHandleCreated ? Handle : IntPtr.Zero;
        EnumWindows(
            (hWnd, _) =>
            {
                if (hWnd == ownHandle || !IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsWith(taskbar))
                {
                    return true;
                }

                if (IsIgnoredCollisionWindow(hWnd, bounds, screen, taskbar))
                {
                    return true;
                }

                occupied.Add(bounds);
                return true;
            },
            IntPtr.Zero);

        AddOccupiedTaskbarChildWindows(screen, taskbar, ownHandle, occupied);

        return taskbar.Height <= taskbar.Width
            ? occupied.OrderByDescending(rect => rect.Left).ToList()
            : occupied.OrderByDescending(rect => rect.Top).ToList();
    }

    private void ReserveTaskListSpace(Screen screen, Rectangle taskbar, Rectangle reservedBounds)
    {
        foreach (var taskbarWindow in GetTaskbarWindows(screen))
        {
            if (!TryGetReservableTaskList(taskbarWindow, taskbar, out var taskList, out var currentBounds) ||
                !currentBounds.IntersectsWith(taskbar))
            {
                continue;
            }

            if (!_originalTaskListBounds.TryGetValue(taskList, out var originalBounds) ||
                !originalBounds.IntersectsWith(taskbar))
            {
                originalBounds = currentBounds;
                _originalTaskListBounds[taskList] = originalBounds;
            }

            var targetBounds = originalBounds;
            if (taskbar.Height <= taskbar.Width)
            {
                var reservedStart = Math.Clamp(reservedBounds.Left - CollisionGap, originalBounds.Left, originalBounds.Right);
                targetBounds = Rectangle.FromLTRB(originalBounds.Left, originalBounds.Top, reservedStart, originalBounds.Bottom);
            }
            else
            {
                var reservedStart = Math.Clamp(reservedBounds.Top - CollisionGap, originalBounds.Top, originalBounds.Bottom);
                targetBounds = Rectangle.FromLTRB(originalBounds.Left, originalBounds.Top, originalBounds.Right, reservedStart);
            }

            SetChildWindowScreenBounds(taskList, targetBounds);
        }
    }

    private void RestoreReservedTaskLists()
    {
        foreach (var item in _originalTaskListBounds.ToArray())
        {
            SetChildWindowScreenBounds(item.Key, item.Value);
        }

        _originalTaskListBounds.Clear();
    }

    private static void SetChildWindowScreenBounds(IntPtr childWindow, Rectangle screenBounds)
    {
        var parent = GetParent(childWindow);
        var x = screenBounds.Left;
        var y = screenBounds.Top;
        if (parent != IntPtr.Zero && GetWindowRect(parent, out var parentRect))
        {
            x -= parentRect.Left;
            y -= parentRect.Top;
        }

        SetWindowPos(
            childWindow,
            IntPtr.Zero,
            x,
            y,
            Math.Max(0, screenBounds.Width),
            Math.Max(0, screenBounds.Height),
            SwpNoZOrder | SwpNoActivate);
    }

    private static void AddOccupiedTaskbarChildWindows(
        Screen screen,
        Rectangle taskbar,
        IntPtr ownHandle,
        List<Rectangle> occupied)
    {
        foreach (var taskbarWindow in GetTaskbarWindows(screen))
        {
            EnumChildWindows(
                taskbarWindow,
                (hWnd, _) =>
                {
                    if (hWnd == ownHandle || !IsWindowVisible(hWnd) || IsExplorerWindow(hWnd) || !GetWindowRect(hWnd, out var rect))
                    {
                        return true;
                    }

                    var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsWith(taskbar))
                    {
                        return true;
                    }

                    if (IsIgnoredCollisionWindow(hWnd, bounds, screen, taskbar))
                    {
                        return true;
                    }

                    occupied.Add(bounds);
                    return true;
                },
                IntPtr.Zero);
        }
    }

    private static List<IntPtr> GetTaskbarWindows(Screen screen)
    {
        var taskbarWindows = new List<IntPtr>();
        EnumWindows(
            (hWnd, _) =>
            {
                var className = GetWindowClassName(hWnd);
                if (className != "Shell_TrayWnd" && className != "Shell_SecondaryTrayWnd")
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (IntersectionArea(bounds, screen.Bounds) > 0)
                {
                    taskbarWindows.Add(hWnd);
                }

                return true;
            },
            IntPtr.Zero);
        return taskbarWindows;
    }

    private static bool IsIgnoredCollisionWindow(IntPtr hWnd, Rectangle bounds, Screen screen, Rectangle taskbar)
    {
        var className = GetWindowClassName(hWnd);
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SIBTranslucentLayer" or
            "EdgeUiInputTopWndClass" or "Progman" or "WorkerW" or "Button")
        {
            return true;
        }

        if (bounds.Width > 700 || bounds.Height > taskbar.Height + 60)
        {
            return true;
        }

        return IntersectionArea(bounds, screen.Bounds) <= 0;
    }

    private static bool IsExplorerWindow(IntPtr hWnd)
    {
        _ = GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
        return TryGetChildTaskList(taskbar, out _, out bounds);
    }

    private static bool TryGetReservableTaskList(IntPtr taskbar, Rectangle taskbarBounds, out IntPtr taskListHost, out Rectangle bounds)
    {
        taskListHost = IntPtr.Zero;
        bounds = Rectangle.Empty;
        if (!TryGetChildTaskList(taskbar, out var taskList, out var taskListBounds))
        {
            return false;
        }

        taskListHost = taskList;
        bounds = taskListBounds;

        var parent = GetParent(taskList);
        if (parent == IntPtr.Zero || parent == taskbar || !GetWindowRect(parent, out var parentRect))
        {
            return true;
        }

        var parentBounds = Rectangle.FromLTRB(parentRect.Left, parentRect.Top, parentRect.Right, parentRect.Bottom);
        if (IsTaskListHostCandidate(parent, parentBounds, taskbarBounds))
        {
            taskListHost = parent;
            bounds = parentBounds;
        }

        return true;
    }

    private static bool TryGetChildTaskList(IntPtr taskbar, out IntPtr taskList, out Rectangle bounds)
    {
        taskList = IntPtr.Zero;
        bounds = Rectangle.Empty;
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        var preferredTaskList = IntPtr.Zero;
        var fallbackTaskList = IntPtr.Zero;
        EnumChildWindows(
            taskbar,
            (hWnd, _) =>
            {
                var className = GetWindowClassName(hWnd);
                if (className == "MSTaskListWClass")
                {
                    preferredTaskList = hWnd;
                    return false;
                }

                if (className == "MSTaskSwWClass" && fallbackTaskList == IntPtr.Zero)
                {
                    fallbackTaskList = hWnd;
                }

                return true;
            },
            IntPtr.Zero);
        taskList = preferredTaskList == IntPtr.Zero ? fallbackTaskList : preferredTaskList;

        if (taskList == IntPtr.Zero || !GetWindowRect(taskList, out var rect))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool IsTaskListHostCandidate(IntPtr hWnd, Rectangle bounds, Rectangle taskbar)
    {
        var className = GetWindowClassName(hWnd);
        if (className is not ("WorkerW" or "MSTaskSwWClass"))
        {
            return false;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.IntersectsWith(taskbar))
        {
            return false;
        }

        return taskbar.Height <= taskbar.Width
            ? bounds.Height <= taskbar.Height + 8 && bounds.Width <= taskbar.Width
            : bounds.Width <= taskbar.Width + 8 && bounds.Height <= taskbar.Height;
    }

    private static int IntersectionArea(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        return intersection.Width <= 0 || intersection.Height <= 0 ? 0 : intersection.Width * intersection.Height;
    }

    private sealed class BatteryToolTipForm : Form
    {
        private const int HorizontalPadding = 10;
        private const int VerticalPadding = 6;
        private string _text = string.Empty;

        public BatteryToolTipForm()
        {
            Text = ToolTipWindowMarker;
            Name = ToolTipWindowMarker;
            AccessibleName = ToolTipWindowMarker;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(30, 34, 39);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateSize();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int wsExNoActivate = 0x08000000;
                const int wsExToolWindow = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= wsExNoActivate | wsExToolWindow;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void ShowText(string text, Point location)
        {
            if (!string.Equals(_text, text, StringComparison.Ordinal))
            {
                _text = text;
                Text = _text;
                AccessibleName = _text;
                UpdateSize();
                Invalidate();
            }

            if (Location != location)
            {
                Location = location;
            }

            if (!Visible)
            {
                Show();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            using var background = new SolidBrush(Color.FromArgb(240, 30, 34, 39));
            using var border = new Pen(Color.FromArgb(125, 238, 244, 247), 1F);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 5);
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);

            TextRenderer.DrawText(
                e.Graphics,
                _text,
                Font,
                new Rectangle(HorizontalPadding, VerticalPadding, Width - HorizontalPadding * 2, Height - VerticalPadding * 2),
                Color.FromArgb(245, 255, 255, 255),
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void UpdateSize()
        {
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var measured = TextRenderer.MeasureText(_text.Length == 0 ? " " : _text, Font, new Size(520, 0), flags);
            Size = new Size(measured.Width + HorizontalPadding * 2, measured.Height + VerticalPadding * 2);
        }
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

    private static void DrawChargingIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        // Points are taken from charging.svg and scaled into the taskbar battery window.
        var source = new[]
        {
            new PointF(568.917333F, 153.6F),
            new PointF(568.917333F, 450.389333F),
            new PointF(682.666667F, 450.389333F),
            new PointF(455.082667F, 870.4F),
            new PointF(455.082667F, 567.978667F),
            new PointF(341.333333F, 567.978667F)
        };
        var sourceBounds = RectangleF.FromLTRB(341.333333F, 153.6F, 682.666667F, 870.4F);
        var scale = Math.Min(bounds.Width / sourceBounds.Width, bounds.Height / sourceBounds.Height);
        var scaledWidth = sourceBounds.Width * scale;
        var scaledHeight = sourceBounds.Height * scale;
        var offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2F - sourceBounds.Left * scale;
        var offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2F - sourceBounds.Top * scale;

        using var icon = new GraphicsPath();
        icon.AddPolygon(source.Select(point => new PointF(point.X * scale + offsetX, point.Y * scale + offsetY)).ToArray());
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, icon);
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

    private static Color AccentFor(BatterySnapshot snapshot)
    {
        if (!snapshot.IsSuccess)
        {
            return BatteryColors.OfflineGray;
        }

        if (IsCharging(snapshot.ChargeState))
        {
            return BatteryColors.ChargingGold;
        }

        if (snapshot.Percent is null)
        {
            return BatteryColors.OfflineGray;
        }

        return snapshot.Percent.Value switch
        {
            <= 15 => Color.FromArgb(255, 99, 87),
            <= 35 => Color.FromArgb(255, 176, 73),
            _ => Color.FromArgb(45, 214, 129)
        };
    }

    private static bool IsCharging(BatteryChargeState state) =>
        state is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge;

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);

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
