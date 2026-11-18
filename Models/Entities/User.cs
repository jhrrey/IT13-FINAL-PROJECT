using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>A row of the <c>User</c> table. <see cref="ClinicId"/> is null for the Super Admin.</summary>
public sealed class User
{
    public int UserId { get; set; }
    public int? ClinicId { get; set; }
    public int RoleId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public string Email { get; set; }
    public string ContactNo { get; set; }

    /// <summary><see cref="AccountStatus.Active"/> or <see cref="AccountStatus.Inactive"/>.</summary>
    public string Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>One of <see cref="SyncStatuses"/>.</summary>
    public string SyncStatus { get; set; }

    /// <summary>Last successful sign-in (UTC), or null if never.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>True while the account has a temporary password: it must set its own at the next sign-in.</summary>
    public bool MustChangePassword { get; set; }

    // Joined values (not columns of User) --------------------------------------------

    /// <summary>From the <c>Role</c> table.</summary>
    public string RoleName { get; set; }

    /// <summary>From the <c>Clinic</c> table; null for the Super Admin.</summary>
    public string ClinicName { get; set; }

    /// <summary>From the <c>Clinic</c> table; null for the Super Admin.</summary>
    public string ClinicStatus { get; set; }

    // Convenience ---------------------------------------------------------------------

    public string FullName => PersonName.Join(FirstName, LastName);

    public string Initials => PersonName.Initials(FirstName, LastName);

    public bool IsActive => Status == AccountStatus.Active;

    /// <summary>
    /// Account status as shown for clinic admins (Clinic Admin Accounts, section 2): Inactive, Waiting for approval
    /// (clinic Pending), Clinic inactive (clinic Inactive / Rejected), or Active.
    /// </summary>
    public string DisplayStatus =>
        ClinicStatus == AccountStatus.Pending ? AccountDisplayStatus.WaitingForApproval
        : Status != AccountStatus.Active ? AccountDisplayStatus.Inactive
        : ClinicStatus is AccountStatus.Inactive or AccountStatus.Rejected or AccountStatus.Suspended ? AccountDisplayStatus.ClinicInactive
        : AccountDisplayStatus.Active;

    public static DateTime? ToLocal(DateTime? utc) => utc == null ? null : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime();
}

/// <summary>The calculated account statuses shown on the Clinic admins page.</summary>
public static class AccountDisplayStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string WaitingForApproval = "Waiting for approval";
    public const string ClinicInactive = "Clinic inactive";
}
