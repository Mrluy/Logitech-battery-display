namespace LogitechBatteryDisplay;

internal static class HidppConstants
{
    public const int LogitechVendorId = 0x046D;
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    public const byte SoftwareId = 0x0E;
    public const int RootFeature = 0x0000;
    public const int FeatureSet = 0x0001;
    public const int BatteryStatus = 0x1000;
    public const int BatteryVoltage = 0x1001;
    public const int UnifiedBattery = 0x1004;
    public const int AdcMeasurement = 0x1F20;

    public static readonly int[] KnownLightspeedReceivers =
    [
        0xC539,
        0xC53A,
        0xC53D,
        0xC53F,
        0xC541,
        0xC545,
        0xC547,
        0xC54D
    ];
}
