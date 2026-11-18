namespace VetCare.Models.Repositories;

/// <summary>Filters and paging for listing users.</summary>
public sealed class UserQuery
{
    /// <summary>Matches first/last name, username or email (contains). Null or empty = no filter.</summary>
    public string Search { get; set; }

    public int? RoleId { get; set; }

    /// <summary><see cref="AccountStatus.Active"/>, <see cref="AccountStatus.Inactive"/>, or null for both.</summary>
    public string Status { get; set; }

    /// <summary>Only users of this clinic. Null = all clinics.</summary>
    public int? ClinicId { get; set; }

    /// <summary>The roles the viewer may see at all (set by the controller, never by the UI).</summary>
    public IReadOnlyCollection<int> VisibleRoleIds { get; set; } = Array.Empty<int>();

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

/// <summary>One page of results plus the total number of matches.</summary>
public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }

    /// <summary>1-based index of the first item shown (0 when empty).</summary>
    public int From => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;

    public int To => Math.Min(Page * PageSize, TotalCount);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page * PageSize < TotalCount;
}

/// <summary>Counts for the summary cards.</summary>
public sealed class UserSummary
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }

    /// <summary>RoleID → number of users with that role.</summary>
    public IDictionary<int, int> ByRole { get; } = new Dictionary<int, int>();

    public int CountOf(int roleId) => ByRole.TryGetValue(roleId, out var n) ? n : 0;
}
