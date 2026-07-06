namespace LogitechBatteryDisplay;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--probe", StringComparison.OrdinalIgnoreCase))
        {
            ProbeCommand.Run();
            return;
        }

        ApplicationConfiguration.Initialize();
        var showHistoryOnStart = args.Any(arg =>
            arg.Equals("--history", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--show-history", StringComparison.OrdinalIgnoreCase));
        var showStatusOnStart = args.Any(arg => arg.Equals("--show", StringComparison.OrdinalIgnoreCase));

        using var instanceMutex = SingleInstance.CreateMutex(out var isFirstInstance);
        if (!isFirstInstance)
        {
            SingleInstance.SignalExistingInstance(showHistoryOnStart);
            return;
        }

        Application.Run(new TrayApplicationContext(showStatusOnStart, showHistoryOnStart));
    }
}
