namespace LogitechBatteryDisplay;

internal sealed class HidppException : Exception
{
    public HidppException(string message)
        : base(message)
    {
    }
}
