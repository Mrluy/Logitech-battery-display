namespace LogitechBatteryDisplay;

internal static class AppPaths
{
    public static string RootDirectory => AppContext.BaseDirectory;

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string HistoryDatabasePath => Path.Combine(RootDirectory, "battery-history.sqlite");

    public static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogitechBatteryDisplay",
        "settings.json");
}
