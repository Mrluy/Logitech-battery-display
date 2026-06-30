using Microsoft.Win32;

namespace LogitechBatteryDisplay;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogitechBatteryDisplay";

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var registeredPath = NormalizeExecutablePath(value);
        return string.Equals(registeredPath, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(string executablePath, bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string NormalizeExecutablePath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 0)
            {
                return trimmed[1..endQuote];
            }
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return trimmed[..(exeIndex + 4)].Trim('"', ' ');
        }

        return trimmed.Trim('"', ' ');
    }

    private static string Quote(string value) => $"\"{value}\"";
}
