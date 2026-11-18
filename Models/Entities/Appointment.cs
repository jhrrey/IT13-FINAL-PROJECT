using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>
/// A row of the <c>Appointment</c> table: one pet with one vet at a set date and time.
/// The owner is reached through the pet.
/// </summary>
public sealed class Appointment
{
    public int AppointmentId { get; set; }
    public int ClinicId { get; set; }
    /// <summary>Empty for a guest booking until check-in (the guest fields hold the details).</summary>
    public int? PetId { get; set; }
    public int VetId { get; set; }

    /// <summary>The staff member who booked it.</summary>
    public int CreatedBy { get; set; }

    public DateTime AppointmentDate { get; set; }
    public TimeSpan AppointmentTime { get; set; }
    public string Reason { get; set; }

    /// <summary>One of <see cref="AppointmentStatuses"/>.</summary>
    public string Status { get; set; }

    // Guest booking (new client, registered at check-in) ------------------------------------

    public string GuestOwnerName { get; set; }
    public string GuestContactNo { get; set; }
    public string GuestPetName { get; set; }
    public string GuestSpecies { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>One of <see cref="SyncStatuses"/>.</summary>
    public string SyncStatus { get; set; }

    // Status timeline -----------------------------------------------------------------

    public DateTime? CheckedInAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; }

    // From joined rows (not Appointment columns) ----------------------------------------

    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public string PetSex { get; set; }
    public int OwnerId { get; set; }
    public string OwnerFirstName { get; set; }
    public string OwnerLastName { get; set; }
    public string OwnerContactNo { get; set; }
    public string VetFirstName { get; set; }
    public string VetLastName { get; set; }
    public string BookedByFirstName { get; set; }
    public string BookedByLastName { get; set; }

    /// <summary>The consultation record for this visit, if one exists.</summary>
    public int? MedicalRecordId { get; set; }

    // Convenience ---------------------------------------------------------------------

    public DateTime StartsAt => AppointmentDate.Date + AppointmentTime;

    /// <summary>"9:30 AM".</summary>
    public string TimeText => TimeSlotHelper.Format(AppointmentTime);

    /// <summary>A new client booked without an Owner or Pet record yet.</summary>
    public bool IsGuest => PetId == null;

    /// <summary>The registered owner, the guest's name, or "Guest (details removed)" after the 30-day cleanup.</summary>
    public string OwnerName => IsGuest
        ? (string.IsNullOrWhiteSpace(GuestOwnerName) ? "Guest (details removed)" : GuestOwnerName)
        : PersonName.Join(OwnerFirstName, OwnerLastName);

    public string ContactNo => IsGuest ? GuestContactNo : OwnerContactNo;
    public string OwnerContactDisplay => string.IsNullOrEmpty(ContactNo) ? "—" : Validator.FormatPhMobile(ContactNo);

    /// <summary>"Pedro Garcia · 0967 123 4567 · Pet: Bruno (Dog)".</summary>
    public string GuestInfoText => OwnerName + " · " + OwnerContactDisplay + " · Pet: " + PetName + " (" + Species + ")";

    /// <summary>"Dr. Lance Concepcion".</summary>
    public string VetName => "Dr. " + PersonName.Join(VetFirstName, VetLastName);

    public string BookedByName => PersonName.Join(BookedByFirstName, BookedByLastName);

    /// <summary>"Dog · Aspin".</summary>
    public string SpeciesAndBreed => IsGuest || string.IsNullOrWhiteSpace(Breed) ? Species : Species + " · " + Breed;

    /// <summary>Scheduled, Checked In or In Consultation (counts for double booking, rule 4).</summary>
    public bool IsActive => AppointmentStatuses.Active.Contains(Status);

    /// <summary>Rule 11: Completed, Cancelled and No-show can't be edited.</summary>
    public bool IsClosed => Status is AppointmentStatuses.Completed or AppointmentStatuses.Cancelled or AppointmentStatuses.NoShow;
}

/// <summary>The appointment status flow (section 3 of the module document).</summary>
public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string CheckedIn = "Checked In";
    public const string InConsultation = "In Consultation";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "No-show";

    public static readonly IReadOnlyList<string> All =
        new[] { Scheduled, CheckedIn, InConsultation, Completed, Cancelled, NoShow };

    /// <summary>Statuses that hold the vet's time slot (rule 4).</summary>
    public static readonly IReadOnlyList<string> Active = new[] { Scheduled, CheckedIn, InConsultation };

    /// <summary>The four queue columns, in order.</summary>
    public static readonly IReadOnlyList<string> Queue = new[] { Scheduled, CheckedIn, InConsultation, Completed };

    /// <summary>Pill / block colours: (background key, text key).</summary>
    public static (string Background, string Text) ColorsFor(string status) => status switch
    {
        CheckedIn => ("Cyan", "Navy"),
        InConsultation => ("WarningBox", "ConsultText"),
        Completed => ("Mint", "CompletedText"),
        Cancelled => ("LightGray", "BodyText"),
        NoShow => ("OrangeSoft", "Orange"),
        _ => ("PaleBlue", "Navy")
    };

    /// <summary>The 3–4px accent line (queue column tops, card left borders).</summary>
    public static string AccentFor(string status) => status switch
    {
        CheckedIn => "CyanStrong",
        InConsultation => "WarningBorder",
        Completed => "CompletedText",
        Cancelled => "NeutralDot",
        NoShow => "Orange",
        _ => "PaleBlueStrong"
    };
}
