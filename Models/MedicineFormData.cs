namespace VetCare.Models;

/// <summary>What the Add / Edit medicine panel sends to PharmacyController (raw text; the controller validates).</summary>
public sealed class MedicineFormData
{
    public const string NameField = "name";
    public const string CategoryField = "category";
    public const string UnitField = "unit";
    public const string PriceField = "price";
    public const string ReorderField = "reorder";
    public const string ExpiryField = "expiry";
    public const string StartingStockField = "startingStock";

    public const int NameMaxLength = 100;

    public string Name { get; set; }
    public string Category { get; set; }
    public string Unit { get; set; }
    public string Price { get; set; }
    public string ReorderLevel { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Only when adding.</summary>
    public string StartingStock { get; set; }
}

/// <summary>Stock in / Adjust stock panel values.</summary>
public sealed class StockChangeData
{
    public const string QuantityField = "quantity";
    public const string ExpiryField = "expiry";
    public const string RemarksField = "remarks";
    public const string TypeField = "type";

    public const string CountCorrection = "Count correction";
    public static readonly IReadOnlyList<string> AdjustTypes = new[] { CountCorrection, "Expired", "Damaged" };

    public string Quantity { get; set; }
    public DateTime? NewExpiry { get; set; }
    public string Remarks { get; set; }

    /// <summary>Adjust only: Count correction, Expired or Damaged.</summary>
    public string Type { get; set; }

    /// <summary>Count correction only: true adds, false removes.</summary>
    public bool Add { get; set; }
}
