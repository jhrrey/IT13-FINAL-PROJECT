namespace VetCare.Models.Navigation;

/// <summary>How much a role may do in a module.</summary>
public enum ModuleAccess
{
    Hidden,
    ViewOnly,
    Full
}

/// <summary>One sidebar entry / module of the system.</summary>
public sealed class ModuleItem
{
    public ModuleItem(string key, string label, string icon, string description)
    {
        Key = key;
        Label = label;
        Icon = icon;
        Description = description;
    }

    public string Key { get; }
    public string Label { get; }

    /// <summary>Lucide icon name.</summary>
    public string Icon { get; }

    /// <summary>One line describing the module (shown on its page).</summary>
    public string Description { get; }
}

public sealed class MenuSection
{
    public MenuSection(string title, params ModuleItem[] items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }
    public IReadOnlyList<ModuleItem> Items { get; }
}

/// <summary>A section with only the items the current user may see.</summary>
public sealed class VisibleSection
{
    public VisibleSection(string title, IReadOnlyList<(ModuleItem Item, ModuleAccess Access)> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }
    public IReadOnlyList<(ModuleItem Item, ModuleAccess Access)> Items { get; }
}

/// <summary>
/// The sidebar's sections and items, and which roles may open each one.
/// The Super Admin has its own system-wide menu (<see cref="SuperAdminSections"/>);
/// every clinic role uses <see cref="Sections"/>, filtered by <see cref="AccessFor"/>.
/// </summary>
public static class MenuCatalog
{
    // Super Admin (system-wide) modules
    public const string Dashboard = "dashboard";
    public const string ClinicManagement = "clinic-management";
    public const string ClinicAdminAccounts = "clinic-admin-accounts";
    public const string DevicesSyncMonitoring = "devices-sync-monitoring";
    public const string SystemSettings = "system-settings";

    // Clinic modules
    public const string ClinicDashboard = "clinic-dashboard";
    public const string UsersAndRoles = "users";
    public const string Owners = "owners";
    public const string Pets = "pets";
    public const string Appointments = "appointments";
    public const string MedicalRecords = "medical-records";
    public const string Vaccinations = "vaccinations";
    public const string Medicines = "medicines";
    public const string Prescriptions = "prescriptions";
    public const string Billing = "billing";
    public const string Reports = "reports";
    public const string DevicesSync = "devices-sync";
    public const string ClinicSettings = "clinic-settings";

    /// <summary>Opened first when the user can see it (otherwise the first visible item).</summary>
    public const string DefaultItem = ClinicDashboard;

    /// <summary>The module opened right after sign-in (Dashboard module: the first page for every clinic role).</summary>
    public static string DefaultItemFor(string role) => DefaultItem;

    /// <summary>The Super Admin's menu: managing the whole system across all clinics.</summary>
    public static IReadOnlyList<MenuSection> SuperAdminSections { get; } = new[]
    {
        new MenuSection("Overview",
            new ModuleItem(Dashboard, "Dashboard", "layout-dashboard", "Clinics, accounts and sync activity across the whole system at a glance.")),

        new MenuSection("Clinic & team",
            new ModuleItem(ClinicManagement, "Clinic management", "building-2", "Review new clinic registrations, approve or reject them, and manage active clinics."),
            new ModuleItem(ClinicAdminAccounts, "Clinic admins", "user-cog", "Create and manage the Administrator account of each clinic.")),

        new MenuSection("Offline / online sync",
            new ModuleItem(DevicesSyncMonitoring, "Devices & sync logs", "refresh-cw", "Every clinic's computers and their offline/online sync status.")),

        new MenuSection("System",
            new ModuleItem(SystemSettings, "System settings", "settings", "Settings that apply to the whole VetCare system."))
    };

    /// <summary>The menu for clinic roles (Administrator, Veterinarian, Cashier/Staff) and the guest preview.</summary>
    public static IReadOnlyList<MenuSection> Sections { get; } = new[]
    {
        new MenuSection("Overview",
            new ModuleItem(ClinicDashboard, "Dashboard", "layout-dashboard", "Today's appointments, low stock, unpaid bills and activity in your clinic at a glance.")),

        new MenuSection("Clinic & team",
            new ModuleItem(UsersAndRoles, "Users & roles", "users", "Create staff accounts and choose what each role can do."),
            new ModuleItem(ClinicSettings, "Clinic settings", "settings", "Your clinic's own fee, hours, slot length and receipt details.")),

        new MenuSection("Pet care",
            new ModuleItem(Owners, "Owners", "contact", "Register pet owners and keep their contact details up to date."),
            new ModuleItem(Pets, "Pets", "paw-print", "Each pet's species, breed, age and weight, linked to its owner."),
            new ModuleItem(Appointments, "Appointments", "calendar-days", "Book, reschedule, check in and complete visits."),
            new ModuleItem(MedicalRecords, "Medical records", "clipboard-plus", "Consultations, diagnoses, treatments and each pet's full history."),
            new ModuleItem(Vaccinations, "Vaccinations", "shield-check", "Vaccines given and when the next dose is due.")),

        new MenuSection("Pharmacy & billing",
            new ModuleItem(Medicines, "Medicines & stock", "warehouse", "Medicine stock levels, low-stock alerts and stock in/out history."),
            new ModuleItem(Prescriptions, "Prescriptions", "pill", "Medicines prescribed by vets and dispensed by staff."),
            new ModuleItem(Billing, "Bills & payments", "circle-dollar-sign", "Bills for consultations, medicines and vaccines, payments and receipts.")),

        new MenuSection("Reports",
            new ModuleItem(Reports, "Reports", "chart-column", "Appointments, medical records, inventory and income reports.")),

        new MenuSection("Offline / online sync",
            new ModuleItem(DevicesSync, "Devices & sync", "refresh-cw", "This clinic's computers and their sync history."))
    };

    private static readonly string[] PetCareAndBilling =
        { Owners, Pets, Appointments, MedicalRecords, Vaccinations, Medicines, Prescriptions, Billing };

    /// <summary>What <paramref name="role"/> may do in the module <paramref name="itemKey"/>. A null role is a guest (preview).</summary>
    public static ModuleAccess AccessFor(string role, string itemKey)
    {
        switch (role)
        {
            case null:
                return ModuleAccess.Full; // guest preview shows every clinic module

            case Roles.SuperAdmin:
                return SuperAdminSections.Any(s => s.Items.Any(i => i.Key == itemKey))
                    ? ModuleAccess.Full
                    : ModuleAccess.Hidden;

            case Roles.Administrator:
                if (itemKey is ClinicDashboard or UsersAndRoles or Reports or DevicesSync or ClinicSettings) return ModuleAccess.Full;
                return PetCareAndBilling.Contains(itemKey) ? ModuleAccess.ViewOnly : ModuleAccess.Hidden;

            case Roles.Veterinarian:
                // Pharmacy: vets prescribe inside the consultation; here they only view stock and prescriptions.
                if (itemKey is Medicines or Prescriptions) return ModuleAccess.ViewOnly;
                return itemKey is ClinicDashboard or Appointments or Pets or MedicalRecords or Vaccinations
                    ? ModuleAccess.Full
                    : ModuleAccess.Hidden;

            case Roles.CashierStaff:
                if (itemKey is MedicalRecords or Vaccinations) return ModuleAccess.ViewOnly; // view & print (e.g. for the owner)
                return itemKey is ClinicDashboard or Owners or Pets or Appointments or Medicines or Prescriptions or Billing
                    ? ModuleAccess.Full
                    : ModuleAccess.Hidden;

            default:
                return ModuleAccess.Hidden;
        }
    }

    /// <summary>The sections and items <paramref name="role"/> can see. Sections with no visible items are left out.</summary>
    public static IReadOnlyList<VisibleSection> VisibleFor(string role) =>
        (role == Roles.SuperAdmin ? SuperAdminSections : Sections)
            .Select(section => new VisibleSection(
                section.Title,
                section.Items
                    .Select(item => (item, AccessFor(role, item.Key)))
                    .Where(entry => entry.Item2 != ModuleAccess.Hidden)
                    .ToList()))
            .Where(section => section.Items.Count > 0)
            .ToList();
}
