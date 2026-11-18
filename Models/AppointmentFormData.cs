namespace VetCare.Models;

/// <summary>Which version of the appointment panel is open.</summary>
public enum AppointmentFormMode
{
    Book,
    Edit,

    /// <summary>Date and time are "now"; saving books and checks in (section 4.C).</summary>
    WalkIn,

    /// <summary>Only the date and time change (rule 12).</summary>
    Reschedule
}

/// <summary>What the Book / Edit / Walk-in / Reschedule panel sends to the controller.</summary>
public sealed class AppointmentFormData
{
    public const string OwnerField = "owner";
    public const string PetField = "pet";
    public const string VetField = "vet";
    public const string DateField = "date";
    public const string TimeField = "time";
    public const string ReasonField = "reason";
    public const string GuestOwnerField = "guestOwner";
    public const string GuestContactField = "guestContact";
    public const string GuestPetField = "guestPet";
    public const string GuestSpeciesField = "guestSpecies";

    /// <summary>Prefixes for the walk-in's new owner / new pet field errors ("newOwner:firstName").</summary>
    public const string NewOwnerPrefix = "newOwner:";
    public const string NewPetPrefix = "newPet:";

    /// <summary>Quick-pick reasons shown as chips.</summary>
    public static readonly IReadOnlyList<string> QuickReasons =
        new[] { "Check-up", "Vaccination", "Follow-up", "Deworming", "Grooming", "Skin problem", "Other" };

    /// <summary>The Reason column holds 100 characters.</summary>
    public const int ReasonMaxLength = 100;

    public AppointmentFormMode Mode { get; set; }

    /// <summary>0 when booking.</summary>
    public int AppointmentId { get; set; }

    public int OwnerId { get; set; }
    public int PetId { get; set; }

    /// <summary>
    /// "New client (guest)": booking keeps only the guest fields on the appointment; a walk-in registers
    /// <see cref="NewOwner"/> and <see cref="NewPet"/> right away. False = "Returning client".
    /// </summary>
    public bool IsGuest { get; set; }

    public string GuestOwnerName { get; set; }
    public string GuestContactNo { get; set; }
    public string GuestPetName { get; set; }
    public string GuestSpecies { get; set; }

    /// <summary>Walk-in, new client: the full Add owner and Add pet fields.</summary>
    public OwnerFormData NewOwner { get; set; }
    public PetFormData NewPet { get; set; }

    /// <summary>0 = none chosen; <see cref="NextAvailableVet"/> = "Next available" (walk-ins).</summary>
    public int VetId { get; set; }

    public const int NextAvailableVet = -1;

    /// <summary>"Dr. Lance Concepcion" (shown above the slots when rescheduling).</summary>
    public string VetName { get; set; }

    public DateTime? Date { get; set; }
    public TimeSpan? Time { get; set; }

    /// <summary>The chosen chip, e.g. "Vaccination" (null if none).</summary>
    public string ReasonChip { get; set; }

    /// <summary>The optional details text.</summary>
    public string ReasonDetails { get; set; }

    /// <summary>The Reason to store: "Vaccination: rabies booster", or the details alone for "Other".</summary>
    public string ComposeReason()
    {
        string details = string.IsNullOrWhiteSpace(ReasonDetails) ? null : ReasonDetails.Trim();
        if (string.IsNullOrEmpty(ReasonChip) || ReasonChip == "Other") return details;
        return details == null ? ReasonChip : ReasonChip + ": " + details;
    }

    /// <summary>Splits a stored reason back into a chip and details (for editing).</summary>
    public static (string Chip, string Details) SplitReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (null, null);
        foreach (string chip in QuickReasons)
        {
            if (chip == "Other") continue;
            if (reason == chip) return (chip, null);
            if (reason.StartsWith(chip + ": ", StringComparison.Ordinal)) return (chip, reason.Substring(chip.Length + 2));
        }
        return ("Other", reason);
    }
}
