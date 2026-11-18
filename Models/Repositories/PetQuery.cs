namespace VetCare.Models.Repositories;

/// <summary>Filters and paging for listing pets. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class PetQuery
{
    public int ClinicId { get; set; }

    /// <summary>Matches the pet name or the owner's name. Null or empty = no filter.</summary>
    public string Search { get; set; }

    /// <summary>Null = all species.</summary>
    public string Species { get; set; }

    /// <summary>Null = both sexes.</summary>
    public string Sex { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;
}

/// <summary>Counts for the Pets summary cards.</summary>
public sealed class PetListSummary
{
    public int Total { get; set; }
    public int Dogs { get; set; }
    public int Cats { get; set; }
    public int VaccinesDueThisMonth { get; set; }
}
