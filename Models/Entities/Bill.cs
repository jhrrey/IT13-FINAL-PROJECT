using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>A row of the <c>Bill</c> table: one completed visit's charges for one owner, with display data.</summary>
public sealed class Bill
{
    public int BillId { get; set; }
    public int ClinicId { get; set; }
    public int AppointmentId { get; set; }
    public int OwnerId { get; set; }

    /// <summary>The staff member who created the bill.</summary>
    public int CashierId { get; set; }

    /// <summary>"VC-2026-0001".</summary>
    public string BillNo { get; set; }

    /// <summary>Sum of the item subtotals (rule 4).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>One of <see cref="BillStatuses"/>.</summary>
    public string Status { get; set; }

    public DateTime BillDate { get; set; }

    /// <summary>The reason a bill was voided.</summary>
    public string Remarks { get; set; }

    public string SyncStatus { get; set; }

    /// <summary>Sum of the payments.</summary>
    public decimal Paid { get; set; }

    // Display (joined) -------------------------------------------------------------------------

    public string OwnerName { get; set; }
    public string OwnerContactNo { get; set; }
    public int PetId { get; set; }
    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public string VetName { get; set; }
    public string CashierName { get; set; }
    public DateTime VisitDate { get; set; }
    public TimeSpan VisitTime { get; set; }
    public string Reason { get; set; }

    public IReadOnlyList<BillItem> Items { get; set; } = Array.Empty<BillItem>();
    public IReadOnlyList<Payment> Payments { get; set; } = Array.Empty<Payment>();

    /// <summary>Total − payments; calculated, never saved.</summary>
    public decimal Balance => Status == BillStatuses.Void ? 0 : Math.Max(0, TotalAmount - Paid);

    public bool IsUnpaid => Status == BillStatuses.Unpaid;

    /// <summary>Can take a payment: Unpaid or Partial.</summary>
    public bool IsOpen => Status is BillStatuses.Unpaid or BillStatuses.Partial;

    /// <summary>Rule 14: void only when there are no payments yet.</summary>
    public bool CanVoid => Status == BillStatuses.Unpaid && Paid == 0;

    public string NumberText => string.IsNullOrEmpty(BillNo) ? "Bill #" + BillId : BillNo;
    public string ContactDisplay => string.IsNullOrEmpty(OwnerContactNo) ? "—" : Validator.FormatPhMobile(OwnerContactNo);
    public string SpeciesAndBreed => string.IsNullOrWhiteSpace(Breed) ? Species : Species + " · " + Breed;
}

/// <summary>A row of the <c>BillItem</c> table.</summary>
public sealed class BillItem
{
    public int BillItemId { get; set; }
    public int BillId { get; set; }
    public int? MedicineId { get; set; }

    /// <summary>One of <see cref="BillItemTypes"/>.</summary>
    public string ItemType { get; set; }

    public string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Quantity × unit price (rule 4).</summary>
    public decimal Subtotal => Quantity * UnitPrice;

    /// <summary>Rule 6: Medicine and Vaccine items come from Pharmacy and Vaccination and can't be changed here.</summary>
    public bool IsEditable => ItemType is BillItemTypes.Consultation or BillItemTypes.Service;
}

/// <summary>A row of the <c>Payment</c> table, with display data.</summary>
public sealed class Payment
{
    public int PaymentId { get; set; }
    public int BillId { get; set; }
    public string BillNo { get; set; }
    public string OwnerName { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>One of <see cref="PaymentMethods.All"/>.</summary>
    public string PaymentMethod { get; set; }

    public string ReferenceNo { get; set; }
    public DateTime PaymentDate { get; set; }
    public string ReceivedByName { get; set; }
    public string SyncStatus { get; set; }
}

public static class BillStatuses
{
    public const string Unpaid = "Unpaid";
    public const string Partial = "Partial";
    public const string Paid = "Paid";
    public const string Void = "Void";

    public static readonly IReadOnlyList<string> All = new[] { Unpaid, Partial, Paid, Void };

    /// <summary>Pill colours (background key, text key).</summary>
    public static (string Background, string Text) ColorsFor(string status) => status switch
    {
        Partial => ("WarningBox", "ConsultText"),
        Paid => ("Mint", "CompletedText"),
        Void => ("LightGray", "BodyText"),
        _ => ("OrangeSoft", "Orange")
    };
}

public static class BillItemTypes
{
    public const string Consultation = "Consultation";
    public const string Medicine = "Medicine";
    public const string Vaccine = "Vaccine";
    public const string Service = "Service";

    /// <summary>Type pill colours (background key, text key).</summary>
    public static (string Background, string Text) ColorsFor(string type) => type switch
    {
        Consultation => ("Cyan", "Navy"),
        Medicine => ("PaleBlue", "Navy"),
        Vaccine => ("Mint", "CompletedText"),
        _ => ("LightGray", "BodyText")
    };
}

public static class PaymentMethods
{
    public const string Cash = "Cash";
    public const string GCash = "GCash";
    public const string Maya = "Maya";
    public const string Card = "Card";
    public const string BankTransfer = "Bank transfer";

    public static readonly IReadOnlyList<string> All = new[] { Cash, GCash, Maya, Card, BankTransfer };

    /// <summary>Rule 10: every method except cash needs a reference no.</summary>
    public static bool NeedsReference(string method) => method != Cash;

    public static string IconFor(string method) => method switch
    {
        Cash => "banknote",
        GCash or Maya => "smartphone",
        Card => "credit-card",
        _ => "landmark"
    };
}
