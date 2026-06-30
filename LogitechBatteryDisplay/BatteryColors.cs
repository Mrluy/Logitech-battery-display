namespace LogitechBatteryDisplay;

internal static class BatteryColors
{
    public static Color For(int? percent, BatteryChargeState state)
    {
        if (state is BatteryChargeState.Recharging or BatteryChargeState.SlowRecharge)
        {
            return Color.FromArgb(42, 129, 212);
        }

        if (percent is null)
        {
            return Color.FromArgb(144, 153, 164);
        }

        return percent.Value switch
        {
            <= 15 => Color.FromArgb(213, 70, 64),
            <= 35 => Color.FromArgb(218, 145, 35),
            _ => Color.FromArgb(36, 157, 103)
        };
    }
}
