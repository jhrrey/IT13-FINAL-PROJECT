namespace VetCare.Models.Repositories;

/// <summary>Filters for listing appointments. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class AppointmentQuery
{
    public int ClinicId { get; set; }

    /// <summary>First day (inclusive). Null = no lower bound (List view).</summary>
    public DateTime? From { get; set; }

    /// <summary>Last day (inclusive). Null = no upper bound.</summary>
    public DateTime? To { get; set; }

    /// <summary>Null = all vets.</summary>
    public int? VetId { get; set; }

    /// <summary>Null = all statuses.</summary>
    public string Status { get; set; }

    /// <summary>Matches the pet name or the owner's name.</summary>
    public string Search { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Counts for the Appointments summary cards (one day).</summary>
public sealed class AppointmentDaySummary
{
    public int Total { get; set; }
    public int Waiting { get; set; }
    public int InConsultation { get; set; }
    public int Completed { get; set; }
}

/// <summary>Result of a booking / edit / reschedule write.</summary>
public enum SlotWriteResult
{
    Saved,

    /// <summary>Rule 4: the vet already has an active appointment in that slot.</summary>
    SlotTaken,

    /// <summary>The appointment is gone, belongs to another clinic, or its status no longer allows the change.</summary>
    NotFound
}
