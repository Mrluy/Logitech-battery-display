namespace LogitechBatteryDisplay;

internal static class AppPaths
{
    public static string RootDirectory => AppContext.BaseDirectory;

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string HistoryDatabasePath => Path.Combine(RootDirectory, "battery-history.db");

    public static string LegacyHistoryDatabasePath => Path.Combine(RootDirectory, "battery-history.sqlite");

    public static void EnsureHistoryDatabaseMigrated()
    {
        if (File.Exists(HistoryDatabasePath) || !File.Exists(LegacyHistoryDatabasePath))
        {
            return;
        }

        Directory.CreateDirectory(RootDirectory);
        try
        {
            File.Move(LegacyHistoryDatabasePath, HistoryDatabasePath, overwrite: false);
            MoveSidecarIfExists("-wal");
            MoveSidecarIfExists("-shm");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Copy(LegacyHistoryDatabasePath, HistoryDatabasePath, overwrite: false);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void MoveSidecarIfExists(string suffix)
    {
        var source = LegacyHistoryDatabasePath + suffix;
        if (!File.Exists(source))
        {
            return;
        }

        var destination = HistoryDatabasePath + suffix;
        if (!File.Exists(destination))
        {
            File.Move(source, destination, overwrite: false);
        }
    }

    public static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogitechBatteryDisplay",
        "settings.json");
}
