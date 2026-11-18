namespace VetCare.Models.Entities;

/// <summary>A row of the <c>Clinic</c> table (one tenant of the system).</summary>
public sealed class Clinic
{
    public int ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string Address { get; set; }
    public string ContactNo { get; set; }
    public string Email { get; set; }

    /// <summary>Pending, Active, Inactive or Rejected (<see cref="AccountStatus"/>).</summary>
    public string Status { get; set; }

    public DateTime? DateCreated { get; set; }

    /// <summary>The consultation fee a new bill starts with (Billing; the cashier can change it per bill).</summary>
    public decimal ConsultationFee { get; set; } = 400m;

    /// <summary>The reject or deactivate reason.</summary>
    public string Remarks { get; set; }

    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>When the status last changed (rejected sign-ups are cleared 30 days after this).</summary>
    public DateTime? StatusChangedAt { get; set; }

    // Shown in the Clinics list (counts only; not columns of Clinic) -------------------------

    public string AdminName { get; set; }
    public string AdminEmail { get; set; }
    public string AdminUsername { get; set; }
    public int UserCount { get; set; }
    public int DeviceCount { get; set; }
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Set by the controller when another clinic has the same name and address, e-mail or contact no.</summary>
    public bool PossibleDuplicate { get; set; }

    public string Initials
    {
        get
        {
            var words = (ClinicName ?? string.Empty)
                .Split(new[] { ' ', '-', '–' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).ToArray();
            if (words.Length == 0) return "?";
            return words.Length == 1
                ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
                : (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
        }
    }
}
