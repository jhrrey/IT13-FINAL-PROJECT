namespace VetCare.Models.Navigation;

/// <summary>How a permission cell is drawn: Full (mint), View (pale blue), Own (cyan) or none (dash).</summary>
public enum PermissionKind
{
    None,
    Full,
    View,
    Own
}

public sealed record PermissionCell(string Label, PermissionKind Kind)
{
    public static readonly PermissionCell None = new("—", PermissionKind.None);
    public static PermissionCell Full(string label = "Full") => new(label, PermissionKind.Full);
    public static PermissionCell View(string label = "View") => new(label, PermissionKind.View);
    public static PermissionCell Own(string label) => new(label, PermissionKind.Own);
}

/// <summary>One row of the permission table: a module and what each role may do in it.</summary>
public sealed record PermissionRow(string Module, PermissionCell SuperAdmin, PermissionCell Administrator,
    PermissionCell Veterinarian, PermissionCell CashierStaff)
{
    public PermissionCell For(int roleId) => roleId switch
    {
        RoleIds.SuperAdmin => SuperAdmin,
        RoleIds.Administrator => Administrator,
        RoleIds.Veterinarian => Veterinarian,
        RoleIds.CashierStaff => CashierStaff,
        _ => PermissionCell.None
    };
}

/// <summary>A role card on the Roles & permissions tab.</summary>
public sealed record RoleInfo(int RoleId, string Name, string Description, string Icon, string CircleColorKey);

/// <summary>
/// The fixed, read-only roles and permissions from Tasks/VetCare_User_Management_Module.md
/// (section 3D). Roles are defined by the system and can't be edited.
/// </summary>
public static class PermissionMatrix
{
    public static IReadOnlyList<RoleInfo> RoleCards { get; } = new[]
    {
        new RoleInfo(RoleIds.SuperAdmin, Roles.SuperAdmin,
            "Approves and manages clinics, creates clinic admin accounts and manages system settings.", "shield-check", "PurpleSoft"),
        new RoleInfo(RoleIds.Administrator, Roles.Administrator,
            "Manages staff accounts, monitors clinic records and generates reports for one clinic.", "user-cog", "PaleBlue"),
        new RoleInfo(RoleIds.Veterinarian, Roles.Veterinarian,
            "Handles consultations, diagnoses, treatments, prescriptions and vaccinations.", "stethoscope", "Mint"),
        new RoleInfo(RoleIds.CashierStaff, Roles.CashierStaff,
            "Registers owners and pets, manages appointments, dispenses medicine and processes billing.", "circle-dollar-sign", "Cyan")
    };

    public static IReadOnlyList<PermissionRow> Rows { get; } = new[]
    {
        new PermissionRow("Dashboard", PermissionCell.Full("Platform"), PermissionCell.Full("Clinic"), PermissionCell.Own("My patients"), PermissionCell.Full("Front desk")),
        new PermissionRow("Clinics", PermissionCell.Full(), PermissionCell.None, PermissionCell.None, PermissionCell.None),
        new PermissionRow("Users & roles", PermissionCell.Full("Admins only"), PermissionCell.Full("Vets & staff"), PermissionCell.None, PermissionCell.None),
        new PermissionRow("Owners", PermissionCell.None, PermissionCell.View(), PermissionCell.None, PermissionCell.Full()),
        new PermissionRow("Pets", PermissionCell.None, PermissionCell.View(), PermissionCell.View(), PermissionCell.Full()),
        new PermissionRow("Appointments", PermissionCell.None, PermissionCell.View(), PermissionCell.Own("Own schedule"), PermissionCell.Full()),
        new PermissionRow("Medical records", PermissionCell.None, PermissionCell.View(), PermissionCell.Full(), PermissionCell.View("View & print")),
        new PermissionRow("Vaccinations", PermissionCell.None, PermissionCell.View(), PermissionCell.Full(), PermissionCell.View("View & print")),
        new PermissionRow("Medicines & stock", PermissionCell.None, PermissionCell.View(), PermissionCell.View(), PermissionCell.Full()),
        new PermissionRow("Prescriptions", PermissionCell.None, PermissionCell.View(), PermissionCell.Full("Create"), PermissionCell.Full("Dispense")),
        new PermissionRow("Bills & payments", PermissionCell.None, PermissionCell.View(), PermissionCell.None, PermissionCell.Full()),
        new PermissionRow("Reports", PermissionCell.None, PermissionCell.Full(), PermissionCell.None, PermissionCell.None),
        new PermissionRow("Devices & sync logs", PermissionCell.Full("All clinics"), PermissionCell.Own("Own clinic"), PermissionCell.None, PermissionCell.None)
    };
}
