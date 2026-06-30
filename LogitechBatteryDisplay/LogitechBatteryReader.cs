using HidSharp;
using System.Diagnostics;
using System.Text;

namespace LogitechBatteryDisplay;

internal sealed class LogitechBatteryReader
{
    private static readonly object RequestLock = new();

    public BatterySnapshot ReadBattery()
    {
        try
        {
            return ReadBatteryCore();
        }
        catch (Exception ex) when (ex is HidppException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            return BatterySnapshot.Error(ex.Message);
        }
    }

    public IReadOnlyList<string> Probe()
    {
        var lines = new List<string>();
        var devices = EnumerateCandidateDevices().ToList();
        lines.Add($"HID candidates: {devices.Count}");

        foreach (var device in devices)
        {
            lines.Add("");
            lines.Add(DescribeDevice(device));
            try
            {
                using var client = HidppClient.Open(device);
                lines.Add($"  Opened input={device.GetMaxInputReportLength()} output={device.GetMaxOutputReportLength()} feature={device.GetMaxFeatureReportLength()}");
                var successes = 0;
                foreach (var slot in ProbeSlots())
                {
                    try
                    {
                        var protocol = client.Ping(slot);
                        if (protocol is null)
                        {
                            lines.Add($"  Slot {slot}: no HID++ response");
                            continue;
                        }

                        successes++;
                        lines.Add($"  Slot {slot}: HID++ {protocol.Value:0.0}");
                        var name = TryReadDeviceName(client, slot) ?? "未知罗技设备";
                        lines.Add($"    Name: {name}");

                        var features = DiscoverFeatures(client, slot, lines);
                        foreach (var feature in features.OrderBy(pair => pair.Value.Index))
                        {
                            lines.Add($"    Feature 0x{feature.Key:X4}: index=0x{feature.Value.Index:X2} flags=0x{feature.Value.Flags:X2} version={feature.Value.Version}");
                        }

                        var battery = TryReadBattery(client, slot, name, device.DevicePath, features, lines);
                        if (battery is not null)
                        {
                            lines.Add($"    Battery: {battery.Percent?.ToString() ?? "?"}% {battery.ChargeState} via {battery.Source}");
                        }
                        else
                        {
                            lines.Add("    Battery: no supported battery feature responded");
                        }
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"  Slot {slot}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (successes == 0)
                {
                    lines.Add("  No paired or awake HID++ device responded on slots 1-7.");
                }
            }
            catch (Exception ex)
            {
                lines.Add($"  Open failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (devices.Count == 0)
        {
            lines.Add("No Logitech LIGHTSPEED receiver was found. Plug the receiver in and try again.");
        }

        return lines;
    }

    private static BatterySnapshot ReadBatteryCore()
    {
        var devices = EnumerateCandidateDevices().ToList();
        if (devices.Count == 0)
        {
            throw new HidppException("未发现罗技 LIGHTSPEED 接收器。");
        }

        var errors = new List<string>();
        foreach (var device in devices)
        {
            try
            {
                using var client = HidppClient.Open(device);
                foreach (var slot in ProbeSlots())
                {
                    var protocol = client.Ping(slot);
                    if (protocol is null || protocol.Value < 2.0)
                    {
                        continue;
                    }

                    var name = TryReadDeviceName(client, slot) ?? "Logitech Wireless Mouse";
                    var features = DiscoverFeatures(client, slot);
                    var battery = TryReadBattery(client, slot, name, device.DevicePath, features);
                    if (battery is not null)
                    {
                        return battery;
                    }
                }
            }
            catch (Exception ex) when (ex is HidppException or IOException or TimeoutException or UnauthorizedAccessException)
            {
                errors.Add($"{SafeProductName(device)}: {ex.Message}");
            }
        }

        var detail = errors.Count > 0 ? $" 详细信息：{string.Join("; ", errors)}" : string.Empty;
        throw new HidppException($"接收器已找到，但没有读到已唤醒鼠标的电量。请移动一下鼠标后重试。{detail}");
    }

    private static IEnumerable<HidDevice> EnumerateCandidateDevices()
    {
        var knownReceiverIds = HidppConstants.KnownLightspeedReceivers.ToHashSet();
        return DeviceList.Local
            .GetHidDevices(HidppConstants.LogitechVendorId, null)
            .Where(device =>
                knownReceiverIds.Contains(device.ProductID) ||
                (device.ProductID >= 0xC500 && device.ProductID <= 0xC5FF))
            .Where(HasHidppReports)
            .OrderByDescending(device => knownReceiverIds.Contains(device.ProductID))
            .ThenBy(device => IsLongHidppInterface(device) ? 0 : 1)
            .ThenBy(device => device.ProductID)
            .ToList();
    }

    private static bool HasHidppReports(HidDevice device)
    {
        try
        {
            return IsShortHidppInterface(device) || IsLongHidppInterface(device);
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerable<byte> ProbeSlots()
    {
        for (byte slot = 1; slot <= 7; slot++)
        {
            yield return slot;
        }

        yield return 0xFF;
        yield return 0x00;
    }

    private static bool IsShortHidppInterface(HidDevice device) =>
        device.GetMaxInputReportLength() == 7 && device.GetMaxOutputReportLength() == 7;

    private static bool IsLongHidppInterface(HidDevice device) =>
        device.GetMaxInputReportLength() >= 20 && device.GetMaxOutputReportLength() >= 20;

    private static Dictionary<int, HidppFeature> DiscoverFeatures(HidppClient client, byte deviceNumber, List<string>? trace = null)
    {
        var features = new Dictionary<int, HidppFeature>
        {
            [HidppConstants.RootFeature] = new(HidppConstants.RootFeature, 0, 0, 0)
        };

        var featureSet = client.FeatureRequest(deviceNumber, 0, 0x00, ToBigEndian(HidppConstants.FeatureSet));
        if (featureSet.Length < 1 || featureSet[0] == 0)
        {
            trace?.Add("    Feature set: not available");
            return features;
        }

        var featureSetIndex = featureSet[0];
        features[HidppConstants.FeatureSet] = new(HidppConstants.FeatureSet, featureSetIndex, FeatureByte(featureSet, 1), FeatureByte(featureSet, 2));

        var countReply = client.FeatureRequest(deviceNumber, featureSetIndex, 0x00, []);
        var count = countReply.Length > 0 ? countReply[0] : (byte)0;
        trace?.Add($"    Feature set: index=0x{featureSetIndex:X2}, count={count}");

        for (byte i = 0; i < count; i++)
        {
            var reply = client.FeatureRequest(deviceNumber, featureSetIndex, 0x10, [i]);
            if (reply.Length < 2)
            {
                continue;
            }

            var featureId = (reply[0] << 8) | reply[1];
            features[featureId] = new(featureId, i, FeatureByte(reply, 2), FeatureByte(reply, 3));
        }

        return features;
    }

    private static BatterySnapshot? TryReadBattery(
        HidppClient client,
        byte deviceNumber,
        string deviceName,
        string receiverPath,
        Dictionary<int, HidppFeature> features,
        List<string>? trace = null)
    {
        if (features.TryGetValue(HidppConstants.UnifiedBattery, out var unified))
        {
            try
            {
                var reply = client.FeatureRequest(deviceNumber, unified.Index, 0x10, []);
                trace?.Add($"    Raw 0x1004 fn=0x10: {Hex(reply)}");
                if (reply.Length >= 4)
                {
                    return FromUnifiedBattery(reply, deviceName, receiverPath, deviceNumber);
                }
            }
            catch (Exception ex) when (ex is HidppException or IOException or TimeoutException)
            {
                trace?.Add($"    Raw 0x1004 fn=0x10: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (features.TryGetValue(HidppConstants.BatteryStatus, out var status))
        {
            try
            {
                var reply = client.FeatureRequest(deviceNumber, status.Index, 0x00, []);
                trace?.Add($"    Raw 0x1000 fn=0x00: {Hex(reply)}");
                if (reply.Length >= 3)
                {
                    return FromBatteryStatus(reply, deviceName, receiverPath, deviceNumber);
                }
            }
            catch (Exception ex) when (ex is HidppException or IOException or TimeoutException)
            {
                trace?.Add($"    Raw 0x1000 fn=0x00: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (features.TryGetValue(HidppConstants.BatteryVoltage, out var voltage))
        {
            try
            {
                var reply = client.FeatureRequest(deviceNumber, voltage.Index, 0x00, []);
                trace?.Add($"    Raw 0x1001 fn=0x00: {Hex(reply)}");
                if (reply.Length >= 3)
                {
                    return FromBatteryVoltage(reply, deviceName, receiverPath, deviceNumber, "BatteryVoltage");
                }
            }
            catch (Exception ex) when (ex is HidppException or IOException or TimeoutException)
            {
                trace?.Add($"    Raw 0x1001 fn=0x00: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (features.TryGetValue(HidppConstants.AdcMeasurement, out var adc))
        {
            try
            {
                var reply = client.FeatureRequest(deviceNumber, adc.Index, 0x00, []);
                trace?.Add($"    Raw 0x1F20 fn=0x00: {Hex(reply)}");
                if (reply.Length >= 3)
                {
                    return FromBatteryVoltage(reply, deviceName, receiverPath, deviceNumber, "AdcMeasurement");
                }
            }
            catch (Exception ex) when (ex is HidppException or IOException or TimeoutException)
            {
                trace?.Add($"    Raw 0x1F20 fn=0x00: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    private static BatterySnapshot FromUnifiedBattery(byte[] reply, string deviceName, string receiverPath, byte deviceNumber)
    {
        var percent = reply[0] == 0 ? ApproximateUnifiedLevel(reply[1]) : reply[0];
        var state = DecodeChargeState(reply[2]);
        return new(
            true,
            deviceName,
            receiverPath,
            deviceNumber,
            ClampPercent(percent),
            state,
            "HID++ 0x1004 Unified Battery",
            "已读取",
            DateTimeOffset.Now);
    }

    private static BatterySnapshot FromBatteryStatus(byte[] reply, string deviceName, string receiverPath, byte deviceNumber)
    {
        int? percent = reply[0] == 0 ? null : reply[0];
        var state = DecodeChargeState(reply[2]);
        return new(
            true,
            deviceName,
            receiverPath,
            deviceNumber,
            ClampPercent(percent),
            state,
            "HID++ 0x1000 Battery Status",
            "已读取",
            DateTimeOffset.Now);
    }

    private static BatterySnapshot FromBatteryVoltage(byte[] reply, string deviceName, string receiverPath, byte deviceNumber, string source)
    {
        var millivolts = (reply[0] << 8) | reply[1];
        var flags = reply[2];
        var isCharging = (flags & 0x80) != 0;
        return new(
            true,
            deviceName,
            receiverPath,
            deviceNumber,
            EstimateBatteryPercent(millivolts),
            isCharging ? BatteryChargeState.Recharging : BatteryChargeState.Discharging,
            $"HID++ {source} {millivolts}mV",
            "已读取",
            DateTimeOffset.Now);
    }

    private static string? TryReadDeviceName(HidppClient client, byte deviceNumber)
    {
        try
        {
            var root = client.FeatureRequest(deviceNumber, 0, 0x00, ToBigEndian(0x0005));
            if (root.Length == 0 || root[0] == 0)
            {
                return null;
            }

            var featureIndex = root[0];
            var lengthReply = client.FeatureRequest(deviceNumber, featureIndex, 0x00, []);
            if (lengthReply.Length == 0)
            {
                return null;
            }

            var length = lengthReply[0];
            if (length <= 0 || length > 64)
            {
                return null;
            }

            var bytes = new List<byte>();
            for (byte offset = 0; offset < length; offset += 14)
            {
                var chunk = client.FeatureRequest(deviceNumber, featureIndex, 0x10, [offset]);
                if (chunk.Length <= 1)
                {
                    break;
                }

                bytes.AddRange(chunk.Skip(1).Take(Math.Min(14, length - offset)));
            }

            var name = Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0', ' ');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ToBigEndian(int value) => [(byte)(value >> 8), (byte)value];

    private static byte FeatureByte(byte[] bytes, int index) => index < bytes.Length ? bytes[index] : (byte)0;

    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(value => value.ToString("X2")));

    private static int? ApproximateUnifiedLevel(byte level) =>
        level switch
        {
            8 => 100,
            4 => 75,
            2 => 20,
            1 => 5,
            _ => null
        };

    private static BatteryChargeState DecodeChargeState(byte value) =>
        value switch
        {
            0 => BatteryChargeState.Discharging,
            1 => BatteryChargeState.Recharging,
            2 => BatteryChargeState.AlmostFull,
            3 => BatteryChargeState.Full,
            4 => BatteryChargeState.SlowRecharge,
            5 => BatteryChargeState.InvalidBattery,
            6 => BatteryChargeState.ThermalError,
            7 => BatteryChargeState.ChargingError,
            _ => BatteryChargeState.Unknown
        };

    private static int? ClampPercent(int? percent)
    {
        if (percent is null)
        {
            return null;
        }

        return Math.Max(0, Math.Min(100, percent.Value));
    }

    private static int EstimateBatteryPercent(int millivolts)
    {
        (int Millivolts, int Percent)[] curve =
        [
            (4186, 100),
            (4067, 90),
            (3989, 80),
            (3922, 70),
            (3859, 60),
            (3811, 50),
            (3778, 40),
            (3751, 30),
            (3717, 20),
            (3671, 10),
            (3646, 5),
            (3579, 2),
            (3500, 0)
        ];

        if (millivolts >= curve[0].Millivolts)
        {
            return curve[0].Percent;
        }

        if (millivolts <= curve[^1].Millivolts)
        {
            return curve[^1].Percent;
        }

        for (var i = 0; i < curve.Length - 1; i++)
        {
            var high = curve[i];
            var low = curve[i + 1];
            if (millivolts >= low.Millivolts && millivolts <= high.Millivolts)
            {
                var span = high.Millivolts - low.Millivolts;
                var fraction = (millivolts - low.Millivolts) / (double)span;
                return (int)Math.Round(low.Percent + ((high.Percent - low.Percent) * fraction));
            }
        }

        return 0;
    }

    private static string DescribeDevice(HidDevice device)
    {
        return $"VID=0x{device.VendorID:X4} PID=0x{device.ProductID:X4} {SafeProductName(device)} Path={device.DevicePath}";
    }

    private static string SafeProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName() ?? "HID device";
        }
        catch
        {
            return "HID device";
        }
    }

    private sealed class HidppClient : IDisposable
    {
        private readonly HidDevice _device;
        private readonly HidStream _stream;
        private readonly bool _supportsShort;
        private readonly bool _supportsLong;
        private byte _softwareId = HidppConstants.SoftwareId;

        private HidppClient(HidDevice device, HidStream stream)
        {
            _device = device;
            _stream = stream;
            _supportsShort = device.GetMaxInputReportLength() == 7 && device.GetMaxOutputReportLength() == 7;
            _supportsLong = device.GetMaxInputReportLength() >= 20 && device.GetMaxOutputReportLength() >= 20;
            _stream.ReadTimeout = 900;
            _stream.WriteTimeout = 900;
        }

        public static HidppClient Open(HidDevice device)
        {
            if (!device.TryOpen(out var stream))
            {
                throw new HidppException("无法打开 HID 设备，可能正在被其他程序独占。");
            }

            return new HidppClient(device, stream);
        }

        public double? Ping(byte deviceNumber)
        {
            var marker = (byte)Random.Shared.Next(1, 255);
            var requestId = (ushort)(0x0010 | NextSoftwareId());
            var response = Request(deviceNumber, requestId, [0, 0, marker], longMessage: !_supportsShort && _supportsLong, 1400, returnErrors: true);
            if (response is null || response.Payload.Length < 3)
            {
                return null;
            }

            if (response.Payload.Length >= 3 && response.Payload[2] == marker)
            {
                return response.Payload[0] + (response.Payload[1] / 10.0);
            }

            return null;
        }

        public byte[] FeatureRequest(byte deviceNumber, byte featureIndex, byte function, byte[] parameters)
        {
            var requestId = (ushort)((featureIndex << 8) | (function & 0xF0) | NextSoftwareId());
            var useLong = (!_supportsShort && _supportsLong) || parameters.Length > 3;
            var response = Request(deviceNumber, requestId, parameters, useLong, 1800, returnErrors: false);
            if (response is null)
            {
                throw new HidppException($"HID++ 请求超时：device={deviceNumber}, feature=0x{featureIndex:X2}, fn=0x{function:X2}");
            }

            return response.Payload;
        }

        private HidppResponse? Request(
            byte deviceNumber,
            ushort requestId,
            byte[] parameters,
            bool longMessage,
            int timeoutMs,
            bool returnErrors)
        {
            lock (RequestLock)
            {
                DrainInput();
                var requestData = new byte[2 + parameters.Length];
                requestData[0] = (byte)(requestId >> 8);
                requestData[1] = (byte)requestId;
                parameters.CopyTo(requestData, 2);

                Write(deviceNumber, requestData, longMessage);

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    var response = ReadOnce(timeoutMs);
                    if (response is null)
                    {
                        continue;
                    }

                    if (response.DeviceNumber != deviceNumber && response.DeviceNumber != (deviceNumber ^ 0xFF))
                    {
                        continue;
                    }

                    if (IsHidpp20Error(response, requestId, out var errorCode))
                    {
                        if (returnErrors)
                        {
                            return null;
                        }

                        throw new HidppException($"HID++ 返回错误 0x{errorCode:X2}：device={deviceNumber}, request=0x{requestId:X4}");
                    }

                    if (IsHidpp10Error(response, requestId, out errorCode))
                    {
                        if (returnErrors)
                        {
                            return null;
                        }

                        throw new HidppException($"HID++ 1.0 返回错误 0x{errorCode:X2}：device={deviceNumber}, request=0x{requestId:X4}");
                    }

                    if (response.Data.Length >= 2 &&
                        response.Data[0] == requestData[0] &&
                        response.Data[1] == requestData[1])
                    {
                        return response;
                    }
                }

                return null;
            }
        }

        private byte NextSoftwareId()
        {
            _softwareId++;
            if (_softwareId < 0x08 || _softwareId > 0x0E)
            {
                _softwareId = HidppConstants.SoftwareId;
            }

            return _softwareId;
        }

        private void Write(byte deviceNumber, byte[] requestData, bool longMessage)
        {
            var reportId = longMessage ? HidppConstants.LongReportId : HidppConstants.ShortReportId;
            var reportLength = Math.Max(_device.GetMaxOutputReportLength(), longMessage ? 20 : 7);
            var buffer = new byte[reportLength];
            buffer[0] = reportId;
            buffer[1] = deviceNumber;
            requestData.AsSpan(0, Math.Min(requestData.Length, buffer.Length - 2)).CopyTo(buffer.AsSpan(2));
            _stream.Write(buffer);
        }

        private HidppResponse? ReadOnce(int timeoutMs)
        {
            var previousTimeout = _stream.ReadTimeout;
            try
            {
                _stream.ReadTimeout = Math.Max(50, Math.Min(500, timeoutMs));
                var buffer = new byte[Math.Max(_device.GetMaxInputReportLength(), 32)];
                var read = _stream.Read(buffer);
                if (read <= 0)
                {
                    return null;
                }

                var reportId = buffer[0];
                if (reportId is not (HidppConstants.ShortReportId or HidppConstants.LongReportId))
                {
                    return null;
                }

                var length = reportId == HidppConstants.ShortReportId ? 7 : 20;
                length = Math.Min(length, read);
                if (length < 4)
                {
                    return null;
                }

                var data = buffer.Skip(2).Take(length - 2).ToArray();
                return new HidppResponse(reportId, buffer[1], data);
            }
            catch (TimeoutException)
            {
                return null;
            }
            finally
            {
                _stream.ReadTimeout = previousTimeout;
            }
        }

        private void DrainInput()
        {
            var previousTimeout = _stream.ReadTimeout;
            try
            {
                _stream.ReadTimeout = 1;
                var buffer = new byte[Math.Max(_device.GetMaxInputReportLength(), 32)];
                while (true)
                {
                    try
                    {
                        _stream.Read(buffer);
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _stream.ReadTimeout = previousTimeout;
            }
        }

        private static bool IsHidpp20Error(HidppResponse response, ushort requestId, out byte errorCode)
        {
            errorCode = 0;
            if (response.Data.Length >= 4 &&
                response.Data[0] == 0xFF &&
                response.Data[1] == (byte)(requestId >> 8) &&
                response.Data[2] == (byte)requestId)
            {
                errorCode = response.Data[3];
                return true;
            }

            return false;
        }

        private static bool IsHidpp10Error(HidppResponse response, ushort requestId, out byte errorCode)
        {
            errorCode = 0;
            if (response.Data.Length >= 4 &&
                response.Data[0] == 0x8F &&
                response.Data[1] == (byte)(requestId >> 8) &&
                response.Data[2] == (byte)requestId)
            {
                errorCode = response.Data[3];
                return true;
            }

            return false;
        }

        public void Dispose() => _stream.Dispose();
    }
}
