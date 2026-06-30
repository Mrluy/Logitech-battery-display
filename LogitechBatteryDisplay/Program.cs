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

        using var instanceMutex = SingleInstance.CreateMutex(out var isFirstInstance);
        if (!isFirstInstance)
        {
            SingleInstance.SignalExistingInstance();
            return;
        }

        Application.Run(new TrayApplicationContext(args.Any(arg => arg.Equals("--show", StringComparison.OrdinalIgnoreCase))));
    }
}
