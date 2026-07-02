namespace LogitechBatteryDisplay;

internal static class BatteryColors
{
    public static readonly Color ChargingGold = Color.FromArgb(255, 220, 4);
    public static readonly Color OfflineGray = Color.FromArgb(145, 155, 163);

    public static Color For(int? percent, BatteryChargeState state)
    {
        if (state is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge)
        {
            return ChargingGold;
        }

        if (percent is null)
        {
            return OfflineGray;
        }

        return percent.Value switch
        {
            <= 15 => Color.FromArgb(213, 70, 64),
            <= 35 => Color.FromArgb(218, 145, 35),
            _ => Color.FromArgb(36, 157, 103)
        };
    }
}
