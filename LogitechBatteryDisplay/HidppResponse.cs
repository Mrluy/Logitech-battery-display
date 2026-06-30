namespace LogitechBatteryDisplay;

internal sealed record HidppResponse(byte ReportId, byte DeviceNumber, byte[] Data)
{
    public byte RequestHi => Data.Length > 0 ? Data[0] : (byte)0;

    public byte RequestLo => Data.Length > 1 ? Data[1] : (byte)0;

    public byte[] Payload => Data.Length > 2 ? Data[2..] : [];

    public string ToHex() =>
        string.Join(" ", new[] { ReportId, DeviceNumber }.Concat(Data).Select(value => value.ToString("X2")));
}
