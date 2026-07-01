namespace LogitechBatteryDisplay;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan SleepingMouseSnapshotRetention = TimeSpan.FromHours(24);
    private readonly LogitechBatteryReader _reader = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly BatteryStatusForm _form;
    private readonly TaskbarBatteryForm _taskbarBatteryForm;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _trayIcon;
    private readonly Icon _windowIcon;
    private readonly EventWaitHandle _showWindowEvent;
    private readonly RegisteredWaitHandle _showWindowRegistration;
    private readonly AppSettings _settings;
    private ToolStripMenuItem? _taskbarBatteryMenuItem;
    private ToolStripMenuItem? _taskbarScreenMenuItem;
    private ToolStripMenuItem? _startupMenuItem;
    private bool _isRefreshing;
    private BatterySnapshot _latest = BatterySnapshot.Error("正在读取鼠标电量...");
    private BatterySnapshot? _lastReadableSnapshot;

    public TrayApplicationContext(bool showOnStart = false)
    {
        _settings = AppSettings.Load();
        _settings.StartWithWindows = StartupManager.IsEnabled(Application.ExecutablePath);

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

        _taskbarBatteryForm = new TaskbarBatteryForm();
        _taskbarBatteryForm.SetTargetScreen(_settings.TaskbarBatteryScreenDeviceName);
        _taskbarBatteryForm.UpdateSnapshot(_latest);

        _notifyIcon = new NotifyIcon
        {
            Text = "罗技鼠标电量",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();

        if (_settings.ShowTaskbarBattery)
        {
            _taskbarBatteryForm.ShowPinned();
        }

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
        menu.Opening += (_, _) => RefreshTaskbarScreenMenu();
        menu.Items.Add("显示状态", null, (_, _) => ShowStatusWindow());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());

        _taskbarBatteryMenuItem = new ToolStripMenuItem("任务栏电量窗口")
        {
            Checked = _settings.ShowTaskbarBattery
        };
        _taskbarBatteryMenuItem.Click += (_, _) => SetTaskbarBatteryWindowEnabled(!_settings.ShowTaskbarBattery);
        menu.Items.Add(_taskbarBatteryMenuItem);

        _taskbarScreenMenuItem = new ToolStripMenuItem("任务栏显示器");
        RefreshTaskbarScreenMenu();
        menu.Items.Add(_taskbarScreenMenuItem);

        _startupMenuItem = new ToolStripMenuItem("开机自启")
        {
            Checked = _settings.StartWithWindows
        };
        _startupMenuItem.Click += (_, _) => SetStartWithWindowsEnabled(!_settings.StartWithWindows);
        menu.Items.Add(_startupMenuItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"版本 {GetApplicationVersion()}")
        {
            Enabled = false
        });

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
            var snapshot = ApplySleepingMouseFallback(await Task.Run(_reader.ReadBattery));
            _latest = snapshot;
            _form.UpdateSnapshot(snapshot);
            _taskbarBatteryForm.UpdateSnapshot(snapshot);
            if (_settings.ShowTaskbarBattery)
            {
                _taskbarBatteryForm.Reposition();
            }

            UpdateTray(snapshot);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private BatterySnapshot ApplySleepingMouseFallback(BatterySnapshot snapshot)
    {
        if (snapshot.IsSuccess && snapshot.Percent is not null)
        {
            _lastReadableSnapshot = snapshot;
            return snapshot;
        }

        if (!snapshot.IsSuccess && IsLastReadableSnapshotFresh())
        {
            return _lastReadableSnapshot!;
        }

        if (_lastReadableSnapshot is not null && !IsLastReadableSnapshotFresh())
        {
            _lastReadableSnapshot = null;
        }

        return snapshot;
    }

    private bool IsLastReadableSnapshotFresh()
    {
        return _lastReadableSnapshot is not null &&
            DateTimeOffset.Now - _lastReadableSnapshot.Timestamp <= SleepingMouseSnapshotRetention;
    }

    private void ClearSleepingMouseCache()
    {
        _lastReadableSnapshot = null;
        _latest = BatterySnapshot.Error("正在读取鼠标电量...");
    }

    private void UpdateTray(BatterySnapshot snapshot)
    {
        var status = snapshot.Percent is int value ? $"{value}%" : "未知";
        _notifyIcon.Text = $"罗技鼠标电量：{status}";
    }

    private void SetTaskbarBatteryWindowEnabled(bool enabled)
    {
        _settings.ShowTaskbarBattery = enabled;
        _settings.Save();

        if (_taskbarBatteryMenuItem is not null)
        {
            _taskbarBatteryMenuItem.Checked = enabled;
        }

        if (enabled)
        {
            _taskbarBatteryForm.UpdateSnapshot(_latest);
            _taskbarBatteryForm.ShowPinned();
            return;
        }

        _taskbarBatteryForm.HidePinned();
    }

    private void SetTaskbarBatteryScreen(string? deviceName)
    {
        _settings.TaskbarBatteryScreenDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;
        _settings.Save();
        _taskbarBatteryForm.SetTargetScreen(_settings.TaskbarBatteryScreenDeviceName);
        RefreshTaskbarScreenMenu();

        if (_settings.ShowTaskbarBattery)
        {
            _taskbarBatteryForm.ShowPinned();
        }
    }

    private void RefreshTaskbarScreenMenu()
    {
        if (_taskbarScreenMenuItem is null)
        {
            return;
        }

        _taskbarScreenMenuItem.DropDownItems.Clear();
        var selectedDeviceName = _settings.TaskbarBatteryScreenDeviceName;
        var screens = Screen.AllScreens
            .OrderBy(GetDisplaySortKey)
            .ThenBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        var followPrimaryItem = new ToolStripMenuItem("跟随主显示器")
        {
            Checked = string.IsNullOrWhiteSpace(selectedDeviceName)
        };
        followPrimaryItem.Click += (_, _) => SetTaskbarBatteryScreen(null);
        _taskbarScreenMenuItem.DropDownItems.Add(followPrimaryItem);

        if (screens.Length > 0)
        {
            _taskbarScreenMenuItem.DropDownItems.Add(new ToolStripSeparator());
        }

        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var deviceName = screen.DeviceName;
            var item = new ToolStripMenuItem(BuildScreenMenuLabel(screen))
            {
                Checked = string.Equals(selectedDeviceName, deviceName, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => SetTaskbarBatteryScreen(deviceName);
            _taskbarScreenMenuItem.DropDownItems.Add(item);
        }

        _taskbarScreenMenuItem.Enabled = _taskbarScreenMenuItem.DropDownItems.Count > 0;
    }

    private static string BuildScreenMenuLabel(Screen screen)
    {
        var primarySuffix = screen.Primary ? "（主显示器）" : string.Empty;
        var displayLabel = TryGetDisplayNumber(screen.DeviceName, out var displayNumber)
            ? $"显示器 {displayNumber}"
            : $"显示器 {ShortDeviceName(screen.DeviceName)}";
        return $"{displayLabel}{primarySuffix} - {ShortDeviceName(screen.DeviceName)} {screen.Bounds.Width}x{screen.Bounds.Height}";
    }

    private static string ShortDeviceName(string deviceName)
    {
        var slashIndex = deviceName.LastIndexOf('\\');
        return slashIndex >= 0 && slashIndex < deviceName.Length - 1
            ? deviceName[(slashIndex + 1)..]
            : deviceName;
    }

    private static int GetDisplaySortKey(Screen screen)
    {
        return TryGetDisplayNumber(screen.DeviceName, out var displayNumber)
            ? displayNumber
            : int.MaxValue;
    }

    private static bool TryGetDisplayNumber(string deviceName, out int displayNumber)
    {
        displayNumber = 0;
        var shortName = ShortDeviceName(deviceName);
        const string prefix = "DISPLAY";
        return shortName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(shortName[prefix.Length..], out displayNumber) &&
            displayNumber > 0;
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "未知";
    }

    private void SetStartWithWindowsEnabled(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(Application.ExecutablePath, enabled);
            _settings.StartWithWindows = enabled;
            _settings.Save();

            if (_startupMenuItem is not null)
            {
                _startupMenuItem.Checked = enabled;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            if (_startupMenuItem is not null)
            {
                _startupMenuItem.Checked = _settings.StartWithWindows;
            }

            _notifyIcon.ShowBalloonTip(3000, "开机自启设置失败", ex.Message, ToolTipIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        ClearSleepingMouseCache();
        _timer.Stop();
        _timer.Dispose();
        _showWindowRegistration.Unregister(null);
        _showWindowEvent.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _taskbarBatteryForm.Dispose();
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
