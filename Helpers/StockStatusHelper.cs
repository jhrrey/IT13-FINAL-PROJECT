using VetCare.Models.Entities;

namespace VetCare.Helpers;

/// <summary>The stock status of a medicine (calculated, never saved).</summary>
public enum StockStatus
{
    InStock,
    LowStock,
    OutOfStock,
    ExpiringSoon,
    Expired
}

/// <summary>Pharmacy section 2: stock status rules, colours and the stock bar.</summary>
public static class StockStatusHelper
{
    /// <summary>System settings → Clinic.ExpiringSoonDays (30 by default).</summary>
    public static int ExpiringDays => SettingsCache.GetInt(Models.Entities.SettingKeys.ExpiringSoonDays);

    /// <summary>Expired and out of stock come first; then expiring soon; then low stock.</summary>
    public static StockStatus StatusOf(Medicine m, DateTime today)
    {
        if (m.ExpiryDate != null && m.ExpiryDate.Value.Date < today.Date) return StockStatus.Expired;
        if (m.StockQty <= 0) return StockStatus.OutOfStock;
        if (m.ExpiryDate != null && m.ExpiryDate.Value.Date <= today.Date.AddDays(ExpiringDays)) return StockStatus.ExpiringSoon;
        if (m.StockQty <= m.ReorderLevel) return StockStatus.LowStock;
        return StockStatus.InStock;
    }

    /// <summary>Pill text and colours (background key, text key).</summary>
    public static (string Text, string Background, string TextColor) Style(StockStatus status) => status switch
    {
        StockStatus.LowStock => ("Low stock", "WarningBox", "ConsultText"),
        StockStatus.OutOfStock => ("Out of stock", "DangerSoft", "DangerHover"),
        StockStatus.ExpiringSoon => ("Expiring soon", "OrangeSoft", "Orange"),
        StockStatus.Expired => ("Expired", "LightGray", "BodyText"),
        _ => ("In stock", "Mint", "CompletedText")
    };

    /// <summary>The stock bar: green above the reorder level, amber at or below it, red at 0.</summary>
    public static (double Fraction, string ColorKey) Bar(Medicine m)
    {
        if (m.StockQty <= 0) return (0, "Danger");
        double full = Math.Max(1, m.ReorderLevel * 2.0);
        double fraction = Math.Min(1, m.StockQty / full);
        return (Math.Max(0.06, fraction), m.StockQty <= m.ReorderLevel ? "WarningBorder" : "SuccessDot");
    }

    /// <summary>The spec's stock-log types and their pill colours.</summary>
    public static readonly IReadOnlyList<string> TransactionTypes = new[] { "Stock In", "Dispensed", "Vaccine Used", "Adjustment", "Expired", "Damaged" };

    public static (string Background, string TextColor) TransactionStyle(string type) => type switch
    {
        "Stock In" => ("Mint", "CompletedText"),
        "Dispensed" => ("PaleBlue", "Navy"),
        "Vaccine Used" => ("Cyan", "Navy"),
        "Adjustment" => ("WarningBox", "ConsultText"),
        _ => ("LightGray", "BodyText")
    };
}
