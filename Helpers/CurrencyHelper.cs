using System.Globalization;

namespace VetCare.Helpers;

/// <summary>Philippine peso amounts: "₱1,070.00" (2 decimals, comma thousands, whatever the machine culture is).</summary>
public static class CurrencyHelper
{
    public static string Peso(decimal amount) => "₱" + amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Parses "1,070.50", "₱1070.5" or "1070"; false when it isn't a number.</summary>
    public static bool TryParse(string text, out decimal amount)
    {
        string clean = (text ?? string.Empty).Trim().Replace("₱", string.Empty).Replace(",", string.Empty).Trim();
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }
}

/// <summary>Readable receipt numbers (the optional BillNo column): "VC-2026-0001", counted per clinic per year.</summary>
public static class BillNumberHelper
{
    /// <summary>System settings → Receipts.BillNoPrefix ("VC" by default; a clinic can use its own).</summary>
    public static string Prefix => (SettingsCache.GetText(Models.Entities.SettingKeys.BillNoPrefix) is { Length: >= 2 } p ? p : "VC").ToUpperInvariant();

    public static string Format(int year, int number) => Prefix + "-" + year + "-" + number.ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>"VC-2026-" — the start of every number of that year (for finding the last one).</summary>
    public static string YearPrefix(int year) => Prefix + "-" + year + "-";
}
