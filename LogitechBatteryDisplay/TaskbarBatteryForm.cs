using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LogitechBatteryDisplay;

internal sealed class TaskbarBatteryForm : Form
{
    private static readonly Color Transparent = Color.FromArgb(255, 1, 2, 3);
    private BatterySnapshot _snapshot = BatterySnapshot.Error("正在读取鼠标电量...");

    public TaskbarBatteryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Width = 148;
        Height = 34;
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Transparent;
        TransparencyKey = Transparent;
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
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

    public void Reposition()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
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

        if (TryGetTaskListBounds(out var taskList) && taskList.Width > Width && taskList.Height > 0)
        {
            Location = GetTaskListLocation(taskList, taskbar);
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

    private Point GetTaskListLocation(Rectangle taskList, Rectangle taskbar)
    {
        const int margin = 14;
        if (taskbar.Height <= taskbar.Width)
        {
            return new Point(
                taskList.Right - Width - margin,
                taskList.Top + Math.Max(0, (taskList.Height - Height) / 2));
        }

        return new Point(
            taskList.Left + Math.Max(0, (taskList.Width - Width) / 2),
            taskList.Bottom - Height - margin);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Transparent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        e.Graphics.Clear(Transparent);

        var percent = _snapshot.Percent;
        var accent = AccentFor(percent, _snapshot.ChargeState);
        var percentText = percent is int value ? $"{value}%" : "--%";

        using var textBrush = new SolidBrush(accent);
        TextRenderer.DrawText(
            e.Graphics,
            percentText,
            Font,
            new Rectangle(0, 3, 54, Height - 6),
            accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        DrawBattery(e.Graphics, new Rectangle(62, 7, 74, 20), percent, accent);
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

    private static bool TryGetTaskListBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var taskbar = FindWindow("Shell_TrayWnd", null);
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

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString();
    }

    private static void DrawBattery(Graphics graphics, Rectangle bounds, int? percent, Color accent)
    {
        using var outline = new Pen(Color.FromArgb(225, 238, 244, 247), 2F);
        using var cap = new SolidBrush(Color.FromArgb(225, 238, 244, 247));
        using var body = RoundedRect(bounds, 5);
        graphics.DrawPath(outline, body);
        graphics.FillRectangle(cap, new Rectangle(bounds.Right + 3, bounds.Top + 6, 5, 8));

        var inner = Rectangle.Inflate(bounds, -5, -5);
        if (percent is int value)
        {
            inner.Width = Math.Max(2, (int)Math.Round(inner.Width * value / 100.0));
            using var fill = new SolidBrush(accent);
            using var fillPath = RoundedRect(inner, 3);
            graphics.FillPath(fill, fillPath);
        }
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

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
