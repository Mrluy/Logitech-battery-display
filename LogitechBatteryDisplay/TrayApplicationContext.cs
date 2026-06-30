namespace LogitechBatteryDisplay;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly LogitechBatteryReader _reader = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly BatteryStatusForm _form;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _trayIcon;
    private readonly Icon _windowIcon;
    private readonly EventWaitHandle _showWindowEvent;
    private readonly RegisteredWaitHandle _showWindowRegistration;
    private bool _isRefreshing;
    private BatterySnapshot _latest = BatterySnapshot.Error("正在读取鼠标电量...");

    public TrayApplicationContext(bool showOnStart = false)
    {
        _trayIcon = LoadApplicationIcon();
        _windowIcon = (Icon)_trayIcon.Clone();

        _form = new BatteryStatusForm
        {
            Icon = _windowIcon
        };
        _ = _form.Handle;
        _form.RefreshRequested += (_, _) => _ = RefreshAsync();

        _showWindowEvent = SingleInstance.CreateShowWindowEvent();
        _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_form.IsDisposed)
                {
                    try
                    {
                        _form.BeginInvoke(ShowStatusWindow);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            },
            null,
            -1,
            false);

        _notifyIcon = new NotifyIcon
        {
            Text = "罗技鼠标电量",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 15_000
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
        if (showOnStart)
        {
            ShowStatusWindow();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示状态", null, (_, _) => ShowStatusWindow());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowStatusWindow()
    {
        _form.UpdateSnapshot(_latest);
        if (_form.Visible)
        {
            _form.Activate();
            return;
        }

        _form.StartPosition = FormStartPosition.Manual;
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        _form.Location = new Point(
            Math.Max(workingArea.Left, workingArea.Right - _form.Width - 24),
            Math.Max(workingArea.Top, workingArea.Bottom - _form.Height - 48));
        _form.Show();
        _form.Activate();
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var snapshot = await Task.Run(_reader.ReadBattery);
            _latest = snapshot;
            _form.UpdateSnapshot(snapshot);
            UpdateTray(snapshot);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void UpdateTray(BatterySnapshot snapshot)
    {
        var status = snapshot.Percent is int value ? $"{value}%" : "未知";
        _notifyIcon.Text = $"罗技鼠标电量：{status}";
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _showWindowRegistration.Unregister(null);
        _showWindowEvent.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _form.Dispose();
        _windowIcon.Dispose();
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }

    private static Icon LoadApplicationIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }
}
