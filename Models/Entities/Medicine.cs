namespace VetCare.Models.Entities;

/// <summary>A row of the <c>Medicine</c> table: one item of the clinic's stock list.</summary>
public sealed class Medicine
{
    public const string VaccineCategory = "Vaccine";

    /// <summary>Pharmacy rule 3: the categories the form offers.</summary>
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "Antibiotic", "Antifungal", "Vaccine", "Dewormer", "Anti-parasitic", "Pain relief", "Topical", "Supplement", "Other"
    };

    /// <summary>The units the form offers.</summary>
    public static readonly IReadOnlyList<string> Units = new[] { "Tablet", "Capsule", "Bottle", "Vial", "Tube", "Sachet", "mL" };

    public int MedicineId { get; set; }
    public int ClinicId { get; set; }
    public string MedicineName { get; set; }
    public string Category { get; set; }

    /// <summary>Tablet, Capsule, Bottle, Vial, Tube, Sachet or mL.</summary>
    public string Unit { get; set; }

    /// <summary>Current stock. Only changed through stock transactions (rule 5).</summary>
    public int StockQty { get; set; }

    /// <summary>"Low stock" when the stock reaches this number.</summary>
    public int ReorderLevel { get; set; }

    /// <summary>Selling price per unit (₱).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The nearest expiry date of the current stock.</summary>
    public DateTime? ExpiryDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public string SyncStatus { get; set; }

    public bool IsVaccine => Category == VaccineCategory;

    public bool InStock => StockQty >= 1;

    /// <summary>Rule 10: expired medicines can't be dispensed or used.</summary>
    public bool IsExpired => ExpiryDate != null && ExpiryDate.Value.Date < DateTime.Today;

    /// <summary>"Stock: 24 Tablet", "Out of stock" or "Expired".</summary>
    public string StockText => IsExpired ? "Expired" : StockQty <= 0 ? "Out of stock" : "Stock: " + StockQty + " " + Unit;

    /// <summary>"per tablet".</summary>
    public string PerUnitText => "per " + (Unit == "mL" ? "mL" : (Unit ?? string.Empty).ToLowerInvariant());
}
