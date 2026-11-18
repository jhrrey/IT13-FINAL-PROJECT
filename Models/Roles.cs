namespace VetCare.Models;

/// <summary>The role names in the <c>Role</c> table.</summary>
public static class Roles
{
    public const string SuperAdmin = "Super Admin";
    public const string Administrator = "Administrator";
    public const string Veterinarian = "Veterinarian";
    public const string CashierStaff = "Cashier/Staff";
}

/// <summary>The fixed <c>Role.RoleID</c> values (see Database/001_user_management.sql).</summary>
public static class RoleIds
{
    public const int SuperAdmin = 1;
    public const int Administrator = 2;
    public const int Veterinarian = 3;
    public const int CashierStaff = 4;

    public static string NameOf(int roleId) => roleId switch
    {
        SuperAdmin => Roles.SuperAdmin,
        Administrator => Roles.Administrator,
        Veterinarian => Roles.Veterinarian,
        CashierStaff => Roles.CashierStaff,
        _ => "Unknown"
    };
}

/// <summary>The values stored in the <c>Status</c> columns of <c>Clinic</c> and <c>User</c>.</summary>
public static class AccountStatus
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Rejected = "Rejected";
    public const string Suspended = "Suspended";
}

/// <summary>The values stored in <c>User.SyncStatus</c>.</summary>
public static class SyncStatuses
{
    public const string Synced = "Synced";
    public const string Pending = "Pending";
    public const string Conflict = "Conflict";
}
