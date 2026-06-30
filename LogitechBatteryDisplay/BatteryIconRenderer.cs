using System.Drawing.Drawing2D;

namespace LogitechBatteryDisplay;

internal static class BatteryIconRenderer
{
    public static Icon RenderUnknown() => RenderCore(null, BatteryChargeState.Unknown);

    public static Icon Render(int percent, BatteryChargeState chargeState) => RenderCore(percent, chargeState);

    private static Icon RenderCore(int? percent, BatteryChargeState chargeState)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var color = BatteryColors.For(percent, chargeState);
        using var outline = new Pen(Color.FromArgb(38, 45, 52), 4);
        using var cap = new SolidBrush(Color.FromArgb(38, 45, 52));
        var body = new Rectangle(7, 18, 46, 28);
        graphics.DrawRectangle(outline, body);
        graphics.FillRectangle(cap, new Rectangle(54, 27, 5, 10));

        var fillRect = new Rectangle(13, 24, 34, 16);
        using var fill = new SolidBrush(color);
        if (percent is int value)
        {
            fillRect.Width = Math.Max(3, (int)Math.Round(fillRect.Width * (value / 100.0)));
            graphics.FillRectangle(fill, fillRect);
        }
        else
        {
            using var font = new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(110, 122, 135));
            graphics.DrawString("?", font, brush, new PointF(20, 17));
        }

        if (percent is int number)
        {
            using var font = new Font("Segoe UI", number >= 100 ? 14 : 16, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.FromArgb(20, 24, 28));
            var text = number.ToString();
            var textSize = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, textBrush, (64 - textSize.Width) / 2, 46);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
