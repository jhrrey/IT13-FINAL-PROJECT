using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>A row of the <c>Owner</c> table: a pet owner (client). Owners never sign in.</summary>
public sealed class Owner
{
    public int OwnerId { get; set; }
    public int ClinicId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    /// <summary>Stored as digits only, e.g. "09171234567".</summary>
    public string ContactNo { get; set; }

    public string Email { get; set; }
    public string Address { get; set; }
    public DateTime DateRegistered { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>One of <see cref="SyncStatuses"/>.</summary>
    public string SyncStatus { get; set; }

    // Worked out by the list query (not columns) ---------------------------------------

    /// <summary>Another active owner in the same clinic has the same contact number.</summary>
    public bool PossibleDuplicate { get; set; }

    // Convenience ---------------------------------------------------------------------

    public string FullName => PersonName.Join(FirstName, LastName);

    public string Initials => PersonName.Initials(FirstName, LastName);

    /// <summary>"0917 123 4567".</summary>
    public string ContactDisplay => Validator.FormatPhMobile(ContactNo);
}
