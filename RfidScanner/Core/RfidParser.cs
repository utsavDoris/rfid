using System.Text;
using RfidScanner.Models;

namespace RfidScanner.Core;

public static class RfidParser
{
    public static RfidTag? Parse(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        if (data.Length >= 2 && data[0] == 0xAA && data[1] == 0xBB)
            return ParseFormatA(data);

        if (data[0] == 0xA0)
            return ParseFormatB(data);

        return ParseFormatC(data);
    }

    public static RfidTag? Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try
        {
            var cleaned = hex.Replace(" ", "").Replace("-", "");
            if (cleaned.Length % 2 != 0)
                return null;

            var bytes = new byte[cleaned.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);

            return Parse(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static RfidTag? ParseFormatA(byte[] data)
    {
        // AA BB [len] [type] [epc...] [checksum]
        if (data.Length < 6)
            return null;

        var payloadLen = data[2];
        if (data.Length < 4 + payloadLen)
            return null;

        var tagType = $"0x{data[3]:X2}";
        var epcLen = Math.Max(0, payloadLen - 2);
        if (epcLen == 0)
            return null;

        var epcBytes = new byte[epcLen];
        Array.Copy(data, 4, epcBytes, 0, epcLen);

        var rssi = data.Length > 4 + epcLen ? (sbyte)data[4 + epcLen] : 0;

        return new RfidTag
        {
            TagId = BytesToHex(epcBytes),
            TagType = tagType,
            Rssi = rssi
        };
    }

    private static RfidTag? ParseFormatB(byte[] data)
    {
        // A0 [cmd] [len] [epc...] [rssi]
        if (data.Length < 4)
            return null;

        var epcLen = data[2];
        if (data.Length < 3 + epcLen)
            return null;

        var epcBytes = new byte[epcLen];
        Array.Copy(data, 3, epcBytes, 0, epcLen);

        var rssiIndex = 3 + epcLen;
        var rssi = rssiIndex < data.Length ? (sbyte)data[rssiIndex] : 0;

        return new RfidTag
        {
            TagId = BytesToHex(epcBytes),
            TagType = $"0x{data[1]:X2}",
            Rssi = rssi
        };
    }

    private static RfidTag? ParseFormatC(byte[] data)
    {
        var hex = BytesToHex(data);
        if (hex.Length < 4)
            return null;

        return new RfidTag
        {
            TagId = hex,
            TagType = "RAW",
            Rssi = 0
        };
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}
