namespace LogitechBatteryDisplay;

internal static class SingleInstance
{
    private const string MutexName = @"Local\LogitechBatteryDisplay.SingleInstance";
    private const string ShowWindowEventName = @"Local\LogitechBatteryDisplay.ShowWindow";
    private const string ShowHistoryEventName = @"Local\LogitechBatteryDisplay.ShowHistory";

    public static Mutex CreateMutex(out bool isFirstInstance)
    {
        return new Mutex(true, MutexName, out isFirstInstance);
    }

    public static EventWaitHandle CreateShowWindowEvent()
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
    }

    public static EventWaitHandle CreateShowHistoryEvent()
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, ShowHistoryEventName);
    }

    public static void SignalExistingInstance(bool showHistory)
    {
        try
        {
            using var showWindowEvent = EventWaitHandle.OpenExisting(showHistory ? ShowHistoryEventName : ShowWindowEventName);
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
