using VetCare.Models.Entities;

namespace VetCare.Models;

/// <summary>
/// Who is using the app right now. After login, <see cref="ClinicId"/> is the tenant every
/// query must filter by (see the multi-tenant rule in the project overview).
/// </summary>
public sealed class UserSession
{
    private UserSession()
    {
    }

    /// <summary>No one is signed in (preview mode: every menu item is shown).</summary>
    public static UserSession Guest { get; } = new UserSession { FullName = "Guest access", RoleName = null };

    public static UserSession SignedIn(User user) => new UserSession
    {
        UserId = user.UserId,
        FullName = user.FullName,
        RoleId = user.RoleId,
        RoleName = user.RoleName,
        ClinicId = user.ClinicId,
        ClinicName = user.ClinicName
    };

    public bool IsGuest => RoleName == null;

    public int UserId { get; private init; }
    public string FullName { get; private init; }

    /// <summary>One of <see cref="RoleIds"/>; 0 for a guest.</summary>
    public int RoleId { get; private init; }

    /// <summary>One of <see cref="Roles"/>, or null for a guest.</summary>
    public string RoleName { get; private init; }

    /// <summary>The clinic whose data is shown. A Super Admin can switch it; everyone else is fixed to their own.</summary>
    public int? ClinicId { get; set; }

    public string ClinicName { get; set; }

    public bool IsSuperAdmin => RoleId == RoleIds.SuperAdmin;

    public bool IsAdministrator => RoleId == RoleIds.Administrator;
}
