using System.Text.RegularExpressions;

namespace RfidScanner.Core;

/// <summary>Matches RFIDStockPro SupabaseManager.getSanitizedPrefix (non-legacy).</summary>
public static class CompanyTableHelper
{
    public static string GetSanitizedPrefix(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return string.Empty;

        var s = companyName.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[ .\-]", "_");
        s = Regex.Replace(s, @"[^a-z0-9_]", "");
        s = Regex.Replace(s, @"_+", "_").Trim('_');

        if (s.Length > 0 && char.IsDigit(s[0]))
            s = "c_" + s;

        return s;
    }

    public static string GetProductTableName(string companyName) =>
        $"{GetSanitizedPrefix(companyName)}_product";

    public static string GetSalesTableName(string companyName) =>
        $"{GetSanitizedPrefix(companyName)}_sales";

    public static string GetReturnTableName(string companyName) =>
        $"{GetSanitizedPrefix(companyName)}_return";

    public static string GetMemoTableName(string companyName) =>
        $"{GetSanitizedPrefix(companyName)}_memo";

    public static string GetReportsTableName(string companyName) =>
        $"{GetSanitizedPrefix(companyName)}_reports";
}
