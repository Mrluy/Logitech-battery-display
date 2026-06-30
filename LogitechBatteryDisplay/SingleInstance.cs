namespace LogitechBatteryDisplay;

internal static class SingleInstance
{
    private const string MutexName = @"Local\LogitechBatteryDisplay.SingleInstance";
    private const string ShowWindowEventName = @"Local\LogitechBatteryDisplay.ShowWindow";

    public static Mutex CreateMutex(out bool isFirstInstance)
    {
        return new Mutex(true, MutexName, out isFirstInstance);
    }

    public static EventWaitHandle CreateShowWindowEvent()
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
    }

    public static void SignalExistingInstance()
    {
        try
        {
            using var showWindowEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
            showWindowEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
