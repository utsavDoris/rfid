using RfidScanner.Models;

namespace RfidScanner.Core;

public static class ChainwayProtocol
{
    public const byte DefaultAddress = 0x00;

    // Nordic UART Service (Chainway R6 advertises as Nordic_UART_CW)
    public static readonly Guid ServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid RxUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // host -> device
    public static readonly Guid TxUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // device -> host

    public static bool IsChainwayDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Nordic_UART", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Chainway", StringComparison.OrdinalIgnoreCase)
            || name.Equals("R6", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("R6 ", StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] BuildInventoryCommand(byte scanTime = 0x0A)
    {
        // Continuous inventory with ~1s scan window
        return BuildFrame(DefaultAddress, 0x01, [scanTime]);
    }

    public static byte[] BuildStopInventoryCommand()
        => BuildFrame(DefaultAddress, 0x93);

    public static byte[] BuildSetRealtimeParamsCommand()
    {
        // TagProtocol=EPC, ReadPause=10ms, Filter=off, Q=4, Session=auto
        return BuildFrame(DefaultAddress, 0x75, [0x00, 0x00, 0x00, 0x04, 0xFF]);
    }

    public static byte[] BuildSetRealtimeModeCommand()
        => BuildFrame(DefaultAddress, 0x76, [0x01]);

    public static byte[] BuildFrame(byte address, byte command, ReadOnlySpan<byte> data = default)
    {
        var len = (byte)(data.Length + 4);
        var frame = new byte[1 + len];
        frame[0] = len;
        frame[1] = address;
        frame[2] = command;
        data.CopyTo(frame.AsSpan(3));

        var crc = Crc16(frame.AsSpan(0, 3 + data.Length));
        frame[3 + data.Length] = (byte)(crc & 0xFF);
        frame[4 + data.Length] = (byte)(crc >> 8);
        return frame;
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0x8408);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }

    public static bool TryParseFrame(ReadOnlySpan<byte> frame, out ChainwayFrame parsed)
    {
        parsed = default;
        if (frame.Length < 5)
            return false;

        var len = frame[0];
        if (frame.Length < len + 1)
            return false;

        var address = frame[1];
        var command = frame[2];
        var dataLen = len - 4;
        var data = dataLen > 0 ? frame.Slice(3, dataLen).ToArray() : Array.Empty<byte>();

        var crcIndex = 3 + dataLen;
        var expectedCrc = (ushort)(frame[crcIndex] | (frame[crcIndex + 1] << 8));
        var actualCrc = Crc16(frame.Slice(0, crcIndex));
        if (expectedCrc != actualCrc)
            return false;

        parsed = new ChainwayFrame(address, command, data);
        return true;
    }

    public static IEnumerable<RfidTag> ExtractTags(ChainwayFrame frame)
    {
        // Real-time inventory upload: reCmd 0xEE, status 0x00
        if (frame.Command == 0xEE)
        {
            if (frame.Data.Length >= 4 && frame.Data[0] == 0x00)
            {
                var tag = ParseRealtimeTag(frame.Data.AsSpan(1));
                if (tag != null)
                    yield return tag;
            }
            yield break;
        }

        // Standard inventory response (reCmd = 0x01)
        if (frame.Command != 0x01 || frame.Data.Length < 3)
            yield break;

        var status = frame.Data[0];
        if (status is not (0x01 or 0x02 or 0x03 or 0x04))
            yield break;

        var index = 2; // skip status + antenna
        var count = frame.Data[index++];

        for (var i = 0; i < count && index < frame.Data.Length; i++)
        {
            var epcLen = frame.Data[index++];
            if (epcLen <= 0 || index + epcLen > frame.Data.Length)
                break;

            var epcBytes = frame.Data.AsSpan(index, epcLen);
            index += epcLen;

            var rssi = index < frame.Data.Length ? (sbyte)frame.Data[index++] : (sbyte)0;

            yield return new RfidTag
            {
                TagId = BytesToHex(epcBytes),
                TagType = "EPC",
                Rssi = rssi
            };
        }
    }

    private static RfidTag? ParseRealtimeTag(ReadOnlySpan<byte> data)
    {
        // Ant, Len, EPC/TID, RSSI
        if (data.Length < 3)
            return null;

        var index = 1; // skip antenna
        var epcLen = data[index++];
        if (epcLen <= 0 || index + epcLen > data.Length)
            return null;

        var epcBytes = data.Slice(index, epcLen);
        index += epcLen;

        var rssi = index < data.Length ? (sbyte)data[index] : (sbyte)0;

        return new RfidTag
        {
            TagId = BytesToHex(epcBytes),
            TagType = "EPC",
            Rssi = rssi
        };
    }

    private static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes);
    }
}

public readonly record struct ChainwayFrame(byte Address, byte Command, byte[] Data);
