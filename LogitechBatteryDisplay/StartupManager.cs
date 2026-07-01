using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace LogitechBatteryDisplay;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedStartupFolderKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ValueName = "LogitechBatteryDisplay";
    private const string ShortcutName = "罗技鼠标电量显示.lnk";
    private const string LegacyShortcutName = "LogitechBatteryDisplay.lnk";

    public static bool IsEnabled(string executablePath)
    {
        if (IsStartupShortcutEnabled(executablePath))
        {
            return true;
        }

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
        if (enabled)
        {
            DeleteFileIfExists(GetStartupShortcutPath(LegacyShortcutName));
            DeleteRegistryValue(StartupApprovedStartupFolderKeyPath, ShortcutName);
            DeleteRegistryValue(StartupApprovedStartupFolderKeyPath, LegacyShortcutName);
            CreateStartupShortcut(executablePath);
            DeleteRunStartupEntry();
            return;
        }

        DeleteRunStartupEntry();
        DeleteStartupShortcuts();
    }

    private static bool IsStartupShortcutEnabled(string executablePath)
    {
        return ShortcutTargetsExecutable(GetStartupShortcutPath(ShortcutName), executablePath) ||
            ShortcutTargetsExecutable(GetStartupShortcutPath(LegacyShortcutName), executablePath);
    }

    private static void CreateStartupShortcut(string executablePath)
    {
        var shortcutPath = GetStartupShortcutPath(ShortcutName);
        var workingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;

        WithShortcut(shortcutPath, shortcut =>
        {
            SetShortcutProperty(shortcut, "TargetPath", executablePath);
            SetShortcutProperty(shortcut, "WorkingDirectory", workingDirectory);
            SetShortcutProperty(shortcut, "Description", "罗技鼠标电量显示");
            SetShortcutProperty(shortcut, "IconLocation", $"{executablePath},0");
            InvokeShortcutMethod(shortcut, "Save");
        });
    }

    private static bool ShortcutTargetsExecutable(string shortcutPath, string executablePath)
    {
        if (!File.Exists(shortcutPath))
        {
            return false;
        }

        var targetPath = GetShortcutTargetPath(shortcutPath);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        return string.Equals(
            NormalizeFilePath(targetPath),
            NormalizeFilePath(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetShortcutTargetPath(string shortcutPath)
    {
        string? targetPath = null;
        WithShortcut(shortcutPath, shortcut =>
        {
            targetPath = GetShortcutProperty(shortcut, "TargetPath") as string;
        });
        return targetPath;
    }

    private static void DeleteRunStartupEntry()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        DeleteRegistryValue(StartupApprovedRunKeyPath, ValueName);
    }

    private static void DeleteStartupShortcuts()
    {
        DeleteFileIfExists(GetStartupShortcutPath(ShortcutName));
        DeleteFileIfExists(GetStartupShortcutPath(LegacyShortcutName));
        DeleteRegistryValue(StartupApprovedStartupFolderKeyPath, ShortcutName);
        DeleteRegistryValue(StartupApprovedStartupFolderKeyPath, LegacyShortcutName);
    }

    private static void DeleteRegistryValue(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetStartupShortcutPath(string shortcutName)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), shortcutName);
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

    private static string NormalizeFilePath(string path)
    {
        var trimmed = path.Trim('"', ' ');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    private static void WithShortcut(string shortcutPath, Action<object> action)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建 WScript.Shell 实例。");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath])
                ?? throw new InvalidOperationException("无法创建启动快捷方式。");

            action(shortcut);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void SetShortcutProperty(object shortcut, string propertyName, object value)
    {
        shortcut.GetType().InvokeMember(
            propertyName,
            System.Reflection.BindingFlags.SetProperty,
            null,
            shortcut,
            [value]);
    }

    private static object? GetShortcutProperty(object shortcut, string propertyName)
    {
        return shortcut.GetType().InvokeMember(
            propertyName,
            System.Reflection.BindingFlags.GetProperty,
            null,
            shortcut,
            null);
    }

    private static void InvokeShortcutMethod(object shortcut, string methodName)
    {
        shortcut.GetType().InvokeMember(
            methodName,
            System.Reflection.BindingFlags.InvokeMethod,
            null,
            shortcut,
            null);
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }
}
