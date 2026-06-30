namespace LogitechBatteryDisplay;

internal static class ProbeCommand
{
    public static void Run()
    {
        NativeConsole.TryAttach();
        var reader = new LogitechBatteryReader();
        var lines = reader.Probe();
        foreach (var line in lines)
        {
            TryWriteLine(line);
        }

        var logPath = Path.Combine(AppContext.BaseDirectory, "probe-result.txt");
        File.WriteAllLines(logPath, lines);
        TryWriteLine("");
        TryWriteLine($"Probe log: {logPath}");
    }

    private static void TryWriteLine(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch (IOException)
        {
        }
    }
}

internal static partial class NativeConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void TryAttach()
    {
        try
        {
            AttachConsole(AttachParentProcess);
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
