using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RfidScanner.Models;

public static class RfidTagMapper
{
    private static readonly Regex RssiNumber = new(@"-?\d+", RegexOptions.Compiled);

    public static int ParseRssi(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var cleaned = raw.Trim()
            .Replace("dBm", string.Empty)
            .Replace("dbm", string.Empty)
            .Replace("DBM", string.Empty)
            .Trim();

        if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        var match = RssiNumber.Match(raw);
        if (match.Success
            && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return value;

        return 0;
    }

    public static string FormatRssiDisplay(string? raw, int parsed)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var text = raw.Trim();
            if (text.IndexOf("dBm", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("dbm", StringComparison.OrdinalIgnoreCase) >= 0)
                return text;

            var numeric = ParseRssi(text);
            if (numeric != 0)
                return $"{numeric} dBm";

            return text;
        }

        return parsed != 0 ? $"{parsed} dBm" : "—";
    }

    /// <summary>Tag type label: TID when chip ID present, else EPC.</summary>
    public static string ResolveTagType(string? epc, string? tid, string? user)
    {
        var hasEpc = !string.IsNullOrWhiteSpace(epc);
        var hasTid = !string.IsNullOrWhiteSpace(tid);
        var hasUser = !string.IsNullOrWhiteSpace(user);

        if (hasTid && hasUser && hasEpc)
            return "EPC+TID+USER";
        if (hasTid)
            return "TID";
        if (hasEpc)
            return "EPC";
        return "—";
    }

    public static RfidTag FromScanned(string epc, string tid, string user, string? rssiRaw)
    {
        var parsed = ParseRssi(rssiRaw);
        return new RfidTag
        {
            Epc = epc ?? string.Empty,
            Tid = tid ?? string.Empty,
            User = user ?? string.Empty,
            TagType = ResolveTagType(epc, tid, user),
            Rssi = parsed,
            RssiDisplay = FormatRssiDisplay(rssiRaw, parsed)
        };
    }
}
