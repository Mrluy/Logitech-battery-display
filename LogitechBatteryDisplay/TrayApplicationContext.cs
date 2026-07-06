using Microsoft.Data.Sqlite;

namespace LogitechBatteryDisplay;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan SleepingMouseSnapshotRetention = TimeSpan.FromHours(24);
    private const int RefreshIntervalMs = 1_000;
    private readonly LogitechBatteryReader _reader = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly BatteryStatusForm _form;
    private readonly BatteryHistoryStore _historyStore;
    private readonly Dictionary<string, TaskbarBatteryForm> _taskbarBatteryForms = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _trayIcon;
    private readonly Icon _windowIcon;
    private readonly EventWaitHandle _showWindowEvent;
    private readonly EventWaitHandle _showHistoryEvent;
    private readonly RegisteredWaitHandle _showWindowRegistration;
    private readonly RegisteredWaitHandle _showHistoryRegistration;
    private readonly AppSettings _settings;
    private ToolStripMenuItem? _taskbarBatteryMenuItem;
    private ToolStripMenuItem? _taskbarScreenMenuItem;
    private ToolStripMenuItem? _startupMenuItem;
    private BatteryHistoryForm? _historyForm;
    private bool _isRefreshing;
    private bool _historyWriteErrorShown;
    private BatterySnapshot _latest = BatterySnapshot.Error("正在读取鼠标电量...");
    private BatterySnapshot? _lastReadableSnapshot;

    public TrayApplicationContext(bool showOnStart = false, bool showHistoryOnStart = false)
    {
        _settings = AppSettings.Load();
        _settings.StartWithWindows = StartupManager.IsEnabled(Application.ExecutablePath);
        if (_settings.StartWithWindows)
        {
            try
            {
                StartupManager.SetEnabled(Application.ExecutablePath, enabled: true);
            }
            catch (Exception ex) when (IsStartupConfigurationException(ex))
            {
            }
        }

        _trayIcon = LoadApplicationIcon();
        _windowIcon = (Icon)_trayIcon.Clone();
        _historyStore = new BatteryHistoryStore(AppPaths.HistoryDatabasePath);

        _form = new BatteryStatusForm
        {
            Icon = _windowIcon
        };
        _ = _form.Handle;
        _form.RefreshRequested += (_, _) => _ = RefreshAsync();

        _showWindowEvent = SingleInstance.CreateShowWindowEvent();
        _showHistoryEvent = SingleInstance.CreateShowHistoryEvent();
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
        _showHistoryRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showHistoryEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_form.IsDisposed)
                {
                    try
                    {
                        _form.BeginInvoke(ShowHistoryWindow);
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

        if (_settings.ShowTaskbarBattery)
        {
            EnsureTaskbarBatteryScreenSelection();
            RefreshTaskbarBatteryWindows();
        }

        _timer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMs
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
        if (showOnStart || showHistoryOnStart)
        {
            EventHandler? initialShowHandler = null;
            initialShowHandler = (_, _) =>
            {
                Application.Idle -= initialShowHandler;
                if (showOnStart)
                {
                    ShowStatusWindow();
                }

                if (showHistoryOnStart)
                {
                    ShowHistoryWindow();
                }
            };
            Application.Idle += initialShowHandler;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RefreshTaskbarScreenMenu();
        menu.Items.Add("显示状态", null, (_, _) => ShowStatusWindow());
        menu.Items.Add("历史图表", null, (_, _) => ShowHistoryWindow());
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
            UpdateTaskbarBatterySnapshots(snapshot);
            UpdateTray(snapshot);
            await RecordHistoryAsync(snapshot);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RecordHistoryAsync(BatterySnapshot snapshot)
    {
        try
        {
            await Task.Run(() => _historyStore.Record(snapshot));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (_historyWriteErrorShown)
            {
                return;
            }

            _historyWriteErrorShown = true;
            _notifyIcon.ShowBalloonTip(3000, "电量历史记录失败", ex.Message, ToolTipIcon.Warning);
        }
    }

    private void ShowHistoryWindow()
    {
        try
        {
            if (_historyForm is { IsDisposed: false })
            {
                if (_historyForm.WindowState == FormWindowState.Minimized)
                {
                    _historyForm.WindowState = FormWindowState.Normal;
                }

                _historyForm.Show();
                _historyForm.BringToFront();
                _historyForm.Activate();
                return;
            }

            _historyForm = new BatteryHistoryForm(_historyStore)
            {
                Icon = _windowIcon
            };
            _historyForm.StartPosition = FormStartPosition.CenterScreen;
            _historyForm.Show();
            _historyForm.BringToFront();
            _historyForm.Activate();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or SqliteException)
        {
            _notifyIcon.ShowBalloonTip(3000, "历史图表打开失败", ex.Message, ToolTipIcon.Warning);
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
            return BuildSleepingMouseSnapshot(_lastReadableSnapshot!);
        }

        if (_lastReadableSnapshot is not null && !IsLastReadableSnapshotFresh())
        {
            _lastReadableSnapshot = null;
        }

        return snapshot;
    }

    private static BatterySnapshot BuildSleepingMouseSnapshot(BatterySnapshot snapshot) =>
        snapshot with
        {
            IsSuccess = false,
            ChargeState = BatteryChargeState.Unknown,
            Message = "鼠标休眠或离线"
        };

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
        if (enabled)
        {
            EnsureTaskbarBatteryScreenSelection();
        }

        _settings.Save();

        if (_taskbarBatteryMenuItem is not null)
        {
            _taskbarBatteryMenuItem.Checked = enabled;
        }

        RefreshTaskbarScreenMenu();
        RefreshTaskbarBatteryWindows();
    }

    private void SetAllTaskbarBatteryScreens()
    {
        var screens = GetSortedScreens();
        _settings.SetTaskbarBatteryScreenDeviceNames(screens.Select(screen => screen.DeviceName));
        _settings.ShowTaskbarBattery = screens.Length > 0;
        _settings.Save();
        UpdateTaskbarBatteryMenuCheckedState();
        RefreshTaskbarScreenMenu();
        RefreshTaskbarBatteryWindows();
    }

    private void SetTaskbarBatteryScreenEnabled(string deviceName, bool enabled)
    {
        var selected = _settings.TaskbarBatteryScreenDeviceNames.ToList();
        if (enabled)
        {
            if (!selected.Contains(deviceName, StringComparer.OrdinalIgnoreCase))
            {
                selected.Add(deviceName);
            }

            _settings.ShowTaskbarBattery = true;
        }
        else
        {
            selected.RemoveAll(name => string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase));
            if (selected.Count == 0)
            {
                _settings.ShowTaskbarBattery = false;
            }
        }

        _settings.SetTaskbarBatteryScreenDeviceNames(selected);
        _settings.Save();
        UpdateTaskbarBatteryMenuCheckedState();
        RefreshTaskbarScreenMenu();
        RefreshTaskbarBatteryWindows();
    }

    private void EnsureTaskbarBatteryScreenSelection()
    {
        if (_settings.TaskbarBatteryScreenDeviceNames.Count > 0)
        {
            return;
        }

        var primary = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (primary is not null)
        {
            _settings.SetTaskbarBatteryScreenDeviceNames([primary.DeviceName]);
        }
    }

    private void UpdateTaskbarBatteryMenuCheckedState()
    {
        if (_taskbarBatteryMenuItem is not null)
        {
            _taskbarBatteryMenuItem.Checked = _settings.ShowTaskbarBattery;
        }
    }

    private void UpdateTaskbarBatterySnapshots(BatterySnapshot snapshot)
    {
        foreach (var form in _taskbarBatteryForms.Values)
        {
            form.UpdateSnapshot(snapshot);
        }
    }

    private void RefreshTaskbarBatteryWindows()
    {
        var desired = _settings.ShowTaskbarBattery
            ? _settings.TaskbarBatteryScreenDeviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        foreach (var item in _taskbarBatteryForms.ToArray())
        {
            if (desired.Contains(item.Key))
            {
                continue;
            }

            item.Value.HidePinned();
            item.Value.Dispose();
            _taskbarBatteryForms.Remove(item.Key);
        }

        foreach (var deviceName in desired.OrderBy(GetDisplaySortKey))
        {
            if (!_taskbarBatteryForms.TryGetValue(deviceName, out var form))
            {
                form = new TaskbarBatteryForm();
                form.SetTargetScreen(deviceName);
                _taskbarBatteryForms.Add(deviceName, form);
            }

            form.UpdateSnapshot(_latest);
            form.ShowPinned();
        }
    }

    private void RefreshTaskbarScreenMenu()
    {
        if (_taskbarScreenMenuItem is null)
        {
            return;
        }

        _taskbarScreenMenuItem.DropDownItems.Clear();
        var selectedDeviceNames = _settings.TaskbarBatteryScreenDeviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var screens = GetSortedScreens();
        var currentScreenNames = screens.Select(screen => screen.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allCurrentSelected = screens.Length > 0 && screens.All(screen => selectedDeviceNames.Contains(screen.DeviceName));

        var allScreensItem = new ToolStripMenuItem("全部当前显示器")
        {
            Checked = allCurrentSelected
        };
        allScreensItem.Click += (_, _) => SetAllTaskbarBatteryScreens();
        _taskbarScreenMenuItem.DropDownItems.Add(allScreensItem);

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
                Checked = selectedDeviceNames.Contains(deviceName)
            };
            item.Click += (_, _) => SetTaskbarBatteryScreenEnabled(deviceName, !item.Checked);
            _taskbarScreenMenuItem.DropDownItems.Add(item);
        }

        var unavailableDeviceNames = selectedDeviceNames
            .Where(deviceName => !currentScreenNames.Contains(deviceName))
            .OrderBy(GetDisplaySortKey)
            .ToArray();
        if (unavailableDeviceNames.Length > 0)
        {
            _taskbarScreenMenuItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var deviceName in unavailableDeviceNames)
            {
                var item = new ToolStripMenuItem($"{ShortDeviceName(deviceName)}（未连接）")
                {
                    Checked = true
                };
                item.Click += (_, _) => SetTaskbarBatteryScreenEnabled(deviceName, enabled: false);
                _taskbarScreenMenuItem.DropDownItems.Add(item);
            }
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

    private static Screen[] GetSortedScreens()
    {
        return Screen.AllScreens
            .OrderBy(GetDisplaySortKey)
            .ThenBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();
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
        return GetDisplaySortKey(screen.DeviceName);
    }

    private static int GetDisplaySortKey(string deviceName)
    {
        return TryGetDisplayNumber(deviceName, out var displayNumber)
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
        catch (Exception ex) when (IsStartupConfigurationException(ex))
        {
            if (_startupMenuItem is not null)
            {
                _startupMenuItem.Checked = _settings.StartWithWindows;
            }

            _notifyIcon.ShowBalloonTip(3000, "开机自启设置失败", ex.Message, ToolTipIcon.Warning);
        }
    }

    private static bool IsStartupConfigurationException(Exception ex)
    {
        return ex is UnauthorizedAccessException or IOException or InvalidOperationException or
            System.Reflection.TargetInvocationException or System.Runtime.InteropServices.COMException;
    }

    protected override void ExitThreadCore()
    {
        ClearSleepingMouseCache();
        _timer.Stop();
        _timer.Dispose();
        _showWindowRegistration.Unregister(null);
        _showHistoryRegistration.Unregister(null);
        _showWindowEvent.Dispose();
        _showHistoryEvent.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var form in _taskbarBatteryForms.Values)
        {
            form.Dispose();
        }

        _taskbarBatteryForms.Clear();
        _historyForm?.Dispose();
        _historyStore.Dispose();
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
