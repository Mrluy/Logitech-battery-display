namespace LogitechBatteryDisplay;

public sealed record BatterySnapshot(
    bool IsSuccess,
    string DeviceName,
    string ReceiverPath,
    int? DeviceNumber,
    int? Percent,
    BatteryChargeState ChargeState,
    string Source,
    string Message,
    DateTimeOffset Timestamp)
{
    public static BatterySnapshot Error(string message) =>
        new(
            false,
            "Logitech LIGHTSPEED",
            string.Empty,
            null,
            null,
            BatteryChargeState.Unknown,
            "HID++",
            message,
            DateTimeOffset.Now);
}

public enum BatteryChargeState
{
    Unknown,
    Discharging,
    Recharging,
    AlmostFull,
    Full,
    SlowRecharge,
    InvalidBattery,
    ThermalError,
    ChargingError
}
