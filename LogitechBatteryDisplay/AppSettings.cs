using System.Text.Json;

namespace LogitechBatteryDisplay;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool ShowTaskbarBattery { get; set; }

    public string? TaskbarBatteryScreenDeviceName { get; set; }

    public List<string> TaskbarBatteryScreenDeviceNames { get; set; } = [];

    public bool StartWithWindows { get; set; }

    public static AppSettings Load()
    {
        try
        {
            EnsureSettingsMigrated();
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(AppPaths.RootDirectory);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void SetTaskbarBatteryScreenDeviceNames(IEnumerable<string> deviceNames)
    {
        TaskbarBatteryScreenDeviceNames = NormalizeDeviceNames(deviceNames);
        TaskbarBatteryScreenDeviceName = TaskbarBatteryScreenDeviceNames.FirstOrDefault();
    }

    private void Normalize()
    {
        TaskbarBatteryScreenDeviceNames = NormalizeDeviceNames(TaskbarBatteryScreenDeviceNames);
        if (TaskbarBatteryScreenDeviceNames.Count == 0 && !string.IsNullOrWhiteSpace(TaskbarBatteryScreenDeviceName))
        {
            TaskbarBatteryScreenDeviceNames.Add(TaskbarBatteryScreenDeviceName.Trim());
        }

        TaskbarBatteryScreenDeviceName = TaskbarBatteryScreenDeviceNames.FirstOrDefault();
    }

    private static List<string> NormalizeDeviceNames(IEnumerable<string>? deviceNames)
    {
        return deviceNames?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    private static void EnsureSettingsMigrated()
    {
        if (File.Exists(AppPaths.SettingsPath) || !File.Exists(AppPaths.LegacySettingsPath))
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.RootDirectory);
        File.Copy(AppPaths.LegacySettingsPath, AppPaths.SettingsPath, overwrite: false);
    }
}
