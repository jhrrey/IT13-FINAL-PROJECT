namespace VetCare.Models.Repositories;

public enum OwnerSort
{
    /// <summary>Most recently registered first.</summary>
    Newest,

    /// <summary>By last name, then first name.</summary>
    NameAToZ
}

/// <summary>Filters and paging for listing owners. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class OwnerQuery
{
    public int ClinicId { get; set; }

    /// <summary>Matches name, contact number (digits) or email. Null or empty = no filter.</summary>
    public string Search { get; set; }

    public OwnerSort Sort { get; set; } = OwnerSort.Newest;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

/// <summary>Counts for the Owners summary cards.</summary>
public sealed class OwnerListSummary
{
    public int Total { get; set; }
    public int NewThisMonth { get; set; }
    public int WithUpcomingAppointments { get; set; }
    public int WithUnpaidBalance { get; set; }
}
