using Microsoft.Data.SqlClient;
using VetCare.Models;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Workspace;

namespace VetCare.Controllers;

/// <summary>
/// The signed-in workspace: decides which menu items the user's role may see,
/// opens modules, and lets a Super Admin switch the clinic they are working in.
/// </summary>
public sealed class WorkspaceController
{
    private readonly UserSession _session;
    private readonly ClinicRepository _clinics;
    private IReadOnlyList<VisibleSection> _sections;
    private WorkspacePage _view;
    private string _activeKey;

    public WorkspaceController(UserSession session, ClinicRepository clinics)
    {
        _session = session;
        _clinics = clinics;
    }

    /// <summary>The user clicked sign out; the app returns to the landing page.</summary>
    public event EventHandler SignOutRequested;

    public Page CreateView()
    {
        _view = new WorkspacePage();
        var sidebar = _view.Sidebar;

        _sections = MenuCatalog.VisibleFor(_session.RoleName);
        sidebar.ShowMenu(_sections);
        sidebar.ItemSelected += (s, key) => Open(key);

        if (_session.IsGuest) sidebar.ShowUser("Guest access", "Not signed in", canSignOut: false);
        else sidebar.ShowUser(_session.FullName, _session.RoleName, canSignOut: true);
        sidebar.SignOutRequested += (s, e) => SignOutRequested?.Invoke(this, EventArgs.Empty);

        sidebar.ShowClinic(ClinicTitle(), canSwitch: _session.IsSuperAdmin);
        sidebar.ClinicSwitcherRequested += async (s, e) => await ShowClinicListAsync();
        sidebar.ClinicSelected += (s, clinic) =>
        {
            // Multi-tenant rule: from now on, module data is filtered by this ClinicID.
            _session.ClinicId = clinic.ClinicId;
            _session.ClinicName = clinic.ClinicName;
            _ = Helpers.SettingsCache.LoadAsync(clinic.ClinicId);   // that clinic's settings (System settings)
            sidebar.HideClinicList();
            sidebar.ShowClinic(ClinicTitle(), canSwitch: true);
            if (_activeKey != null) Open(_activeKey);
        };

        // Top bar: greeting, connection status, and the profile chip's account menu.
        var topBar = _view.TopBar;
        topBar.ShowUser(_session);
        topBar.ProfileMenuRequested += (s, chip) => _view.AccountMenu.Show(chip, new[]
        {
            new Views.Shared.MenuEntry("Sign out", "log-out", () => SignOutRequested?.Invoke(this, EventArgs.Empty))
        });
        StartConnectionChecks();
        StartSyncChecks();

        // Appointments rule 15: clear old guest details (cancelled / no-show guests) once per sign-in.
        if (_session.ClinicId != null && !_session.IsGuest)
            _ = Task.Run(async () =>
            {
                try { await new AppointmentRepository().ClearOldGuestInfoAsync(_session.ClinicId.Value); }
                catch (SqlException) { /* runs again at the next sign-in */ }
            });

        Open(DefaultItemKey());
        return _view;
    }

    /// <summary>
    /// Shows "All synced" while the central database answers and "Offline" when it doesn't.
    /// Checked when the workspace opens and every 30 seconds until it closes.
    /// </summary>
    private void StartConnectionChecks()
    {
        var timer = _view.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += async (s, e) => await CheckConnectionAsync();
        _view.Loaded += async (s, e) =>
        {
            await CheckConnectionAsync();
            timer.Start();
        };
        _view.Unloaded += (s, e) => timer.Stop();
    }

    /// <summary>
    /// Devices & Sync: this computer checks in at sign-in and every Sync.AutoSyncMinutes (clinic users on a registered
    /// computer only). If the server turns the computer away, the user is signed out with the reason.
    /// </summary>
    private void StartSyncChecks()
    {
        if (_session.IsGuest || _session.ClinicId == null || Helpers.DeviceIdentity.CurrentDeviceId == null) return;
        var agent = new SyncAgent(_session);
        agent.Refused += (s, message) =>
        {
            // Back to the sign-in page, which shows why in its warning box.
            AuthController.PendingWarning = ("You were signed out by your administrator", message);
            SignOutRequested?.Invoke(this, EventArgs.Empty);
        };
        var timer = _view.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(Math.Max(1, Helpers.SettingsCache.GetInt(Models.Entities.SettingKeys.AutoSyncMinutes)));
        timer.Tick += async (s, e) =>
        {
            await agent.RunAsync();
            timer.Interval = TimeSpan.FromMinutes(Math.Max(1, Helpers.SettingsCache.GetInt(Models.Entities.SettingKeys.AutoSyncMinutes)));
        };
        _view.Loaded += async (s, e) =>
        {
            await agent.RunAsync();
            timer.Start();
        };
        _view.Unloaded += (s, e) => timer.Stop();
    }

    private async Task CheckConnectionAsync()
    {
        bool online;
        try
        {
            await using var connection = await Models.Data.Database.OpenAsync();
            online = true;
        }
        catch (SqlException)
        {
            online = false;
        }

        _view.TopBar.ShowSync(online);
    }

    private string ClinicTitle()
    {
        if (_session.IsGuest) return "VetCare Clinic";
        if (!string.IsNullOrEmpty(_session.ClinicName)) return _session.ClinicName;
        return _session.IsSuperAdmin ? "All clinics" : "VetCare Clinic";
    }

    /// <summary>"Appointments" if the role can see it, otherwise the first visible item.</summary>
    private string DefaultItemKey()
    {
        var all = _sections.SelectMany(s => s.Items).Select(i => i.Item.Key).ToList();
        string preferred = MenuCatalog.DefaultItemFor(_session.RoleName);
        return all.Contains(preferred) ? preferred : all.FirstOrDefault();
    }

    /// <param name="recordId">Open this record's profile straight away (an owner or pet ID), or null for the list.</param>
    /// <param name="intent">A dashboard shortcut for the module (e.g. "add", "status:LowStock", "pay"); see each controller's StartIntent.</param>
    private void Open(string key, int? recordId = null, string intent = null)
    {
        _intent = intent;
        foreach (var section in _sections)
        {
            foreach (var (item, access) in section.Items)
            {
                if (item.Key != key) continue;

                _activeKey = key;
                _view.Sidebar.SetActive(key);
                _view.ShowModule(CreateModuleView(item, section.Title, access, recordId));
                return;
            }
        }
    }

    /// <summary>True if the signed-in role sees this module in the sidebar.</summary>
    private bool CanOpen(string key) => _sections.Any(s => s.Items.Any(i => i.Item.Key == key));

    /// <summary>A Pets controller that can also book appointments (pet profile, pet form, owner profile).</summary>
    private PetController NewPetController() =>
        new(_session, new PetRepository(), new PetLinksRepository(), new OwnerRepository())
        {
            Appointments = NewAppointmentController(),
            Vaccinations = CanOpen(MenuCatalog.Vaccinations) ? NewVaccinationController() : null
        };

    private VaccinationController NewVaccinationController()
    {
        var vaccinations = new VaccinationController(_session, new VaccinationRepository(), new MedicineRepository(),
            new PetRepository(), new UserRepository(), _clinics)
        {
            Appointments = NewAppointmentController()
        };
        if (CanOpen(MenuCatalog.Pets)) vaccinations.OpenPet = petId => Open(MenuCatalog.Pets, petId);
        if (CanOpen(MenuCatalog.MedicalRecords)) vaccinations.OpenRecord = recordId => Open(MenuCatalog.MedicalRecords, recordId);
        return vaccinations;
    }

    private AppointmentController NewAppointmentController()
    {
        var appointments = new AppointmentController(_session, new AppointmentRepository(), new UserRepository(),
            new PetRepository(), new OwnerRepository());
        if (CanOpen(MenuCatalog.Pets)) appointments.OpenPet = petId => Open(MenuCatalog.Pets, petId);
        appointments.OpenModule = key => { if (CanOpen(key)) Open(key); };
        if (CanOpen(MenuCatalog.Billing) && _session.RoleId == RoleIds.CashierStaff) appointments.OpenCreateBill = OpenCreateBill;
        if (CanOpen(MenuCatalog.MedicalRecords))
        {
            appointments.OpenRecord = recordId => Open(MenuCatalog.MedicalRecords, recordId);
            if (_session.RoleId == RoleIds.Veterinarian) appointments.OpenConsultation = OpenConsultation;
        }
        return appointments;
    }

    /// <summary>The appointment whose consultation page the Medical records module should open next.</summary>
    private int? _pendingConsultation;

    /// <summary>The visit the Billing module should open "Create bill" for next (Appointments → "Create bill").</summary>
    private int? _pendingCreateBill;

    private void OpenCreateBill(int appointmentId)
    {
        _pendingCreateBill = appointmentId;
        Open(MenuCatalog.Billing);
    }

    /// <summary>"Start consultation" in Appointments: the Medical records module opens on the consultation page.</summary>
    private void OpenConsultation(int appointmentId)
    {
        _pendingConsultation = appointmentId;
        Open(MenuCatalog.MedicalRecords);
    }

    private MedicalRecordController NewMedicalRecordController()
    {
        var records = new MedicalRecordController(_session, new MedicalRecordRepository(), new AppointmentRepository(),
            new PetRepository(), new MedicineRepository(), new VaccinationRepository(), new UserRepository(), _clinics)
        {
            Appointments = NewAppointmentController()
        };
        if (CanOpen(MenuCatalog.Appointments)) records.OpenAppointments = () => Open(MenuCatalog.Appointments);
        if (CanOpen(MenuCatalog.Pets)) records.OpenPet = petId => Open(MenuCatalog.Pets, petId);
        return records;
    }

    /// <summary>The page for a module: built modules get their controller, the rest a placeholder.</summary>
    /// <summary>The shortcut for the module being opened (consumed by CreateModuleView).</summary>
    private string _intent;

    private View CreateModuleView(ModuleItem item, string sectionTitle, ModuleAccess access, int? recordId)
    {
        string intent = _intent;
        _intent = null;
        switch (item.Key)
        {
            case MenuCatalog.DevicesSyncMonitoring: // Super Admin: every clinic's computers
            case MenuCatalog.DevicesSync:           // Administrator: their clinic's computers
                return new SyncMonitorController(_session, new DeviceRepository(), _clinics) { StartIntent = intent }.CreateView();

            case MenuCatalog.SystemSettings:        // Super Admin: global settings and clinic defaults
            case MenuCatalog.ClinicSettings:        // Administrator: the settings a clinic can override
                return new SettingController(_session, new SettingRepository(), _clinics) { StartIntent = intent }.CreateView();

            case MenuCatalog.Dashboard:            // Super Admin: the whole platform (counts and statuses only)
                return new DashboardController(_session, new DashboardRepository())
                {
                    OpenModule = (key, id, shortcut) => { if (CanOpen(key)) Open(key, id, shortcut); }
                }.CreateSuperAdminView();

            case MenuCatalog.ClinicDashboard:      // Administrator, Veterinarian and Cashier/Staff (each their own)
            {
                var dashboard = new DashboardController(_session, new DashboardRepository())
                {
                    OpenModule = (key, id, shortcut) => { if (CanOpen(key)) Open(key, id, shortcut); },
                    Appointments = CanOpen(MenuCatalog.Appointments) ? NewAppointmentController() : null
                };
                if (CanOpen(MenuCatalog.MedicalRecords) && _session.RoleId == RoleIds.Veterinarian) dashboard.OpenConsultation = OpenConsultation;
                if (CanOpen(MenuCatalog.Billing) && _session.RoleId == RoleIds.CashierStaff) dashboard.OpenCreateBill = OpenCreateBill;
                return dashboard.CreateView();
            }

            case MenuCatalog.UsersAndRoles:        // Administrator: Veterinarian and Cashier/Staff accounts
            case MenuCatalog.ClinicAdminAccounts:  // Super Admin: Administrator accounts
                return new UserController(_session, new UserRepository(), new RoleRepository(), _clinics) { StartIntent = intent }.CreateView();

            case MenuCatalog.Owners:               // Cashier/Staff (full) and Administrator (view only)
            {
                var owners = new OwnerController(_session, new OwnerRepository(),
                    new OwnerLinksRepository(new PetRepository(), new PetLinksRepository()), NewPetController());
                if (CanOpen(MenuCatalog.Pets)) owners.OpenPet = petId => Open(MenuCatalog.Pets, petId);
                if (CanOpen(MenuCatalog.Billing)) owners.OpenBill = billId => Open(MenuCatalog.Billing, billId);
                owners.StartIntent = intent;
                return owners.CreateView(recordId);
            }

            case MenuCatalog.Pets:                 // Cashier/Staff (full), Veterinarian and Administrator (view only)
            {
                var pets = NewPetController();
                if (CanOpen(MenuCatalog.Owners)) pets.OpenOwner = ownerId => Open(MenuCatalog.Owners, ownerId);
                if (CanOpen(MenuCatalog.MedicalRecords)) pets.OpenRecord = id => Open(MenuCatalog.MedicalRecords, id);
                return pets.CreateView(recordId);
            }

            case MenuCatalog.Appointments:         // Cashier/Staff (full), Veterinarian (own schedule), Administrator (view only)
                return NewAppointmentController().CreateView(recordId);

            case MenuCatalog.Vaccinations:         // Veterinarian (full), Cashier/Staff (view, print, book), Administrator (view only)
            {
                var vaccinations = NewVaccinationController();
                vaccinations.StartIntent = intent;
                return vaccinations.CreateView();
            }

            case MenuCatalog.MedicalRecords:       // Veterinarian (full), Cashier/Staff (view & print), Administrator (view only)
            {
                int? consultation = _pendingConsultation;
                _pendingConsultation = null;
                return NewMedicalRecordController().CreateView(consultation == null ? recordId : null, consultation);
            }

            case MenuCatalog.Billing:              // Cashier/Staff (full) and Administrator (view only)
            {
                int? createFor = _pendingCreateBill;
                _pendingCreateBill = null;
                return new BillingController(_session, new BillingRepository(), _clinics) { StartIntent = intent }
                    .CreateView(createFor == null ? recordId : null, createFor);
            }

            case MenuCatalog.Reports:              // Administrator only (reports rule 1)
                return new ReportController(_session, new ReportRepository(), _clinics).CreateView();

            case MenuCatalog.ClinicManagement:     // Super Admin only (clinic management rule 1)
                return new ClinicController(_session, _clinics, new UserRepository(), new DeviceRepository()) { StartIntent = intent }.CreateView();

            case MenuCatalog.Medicines:            // Cashier/Staff (full), Veterinarian and Administrator (view only)
            case MenuCatalog.Prescriptions:        // Cashier/Staff (dispense), Veterinarian and Administrator (view only)
            {
                var pharmacy = new PharmacyController(_session, new MedicineRepository(), new InventoryRepository(),
                    new PrescriptionRepository(), new UserRepository(), _clinics);
                if (CanOpen(MenuCatalog.MedicalRecords)) pharmacy.OpenRecord = id => Open(MenuCatalog.MedicalRecords, id);
                pharmacy.StartIntent = intent;
                return pharmacy.CreateView(item.Key == MenuCatalog.Medicines ? PharmacyPage.Medicines : PharmacyPage.Prescriptions);
            }

            default:
                return new ModulePageView(item, sectionTitle, access, _session.ClinicName);
        }
    }

    private async Task ShowClinicListAsync()
    {
        if (!_session.IsSuperAdmin) return; // only the Super Admin may switch clinics

        try
        {
            var clinics = (await _clinics.GetAllAsync()).Where(c => c.Status == AccountStatus.Active).ToList();
            _view.Sidebar.ShowClinicList(clinics, _session.ClinicId);
        }
        catch (SqlException)
        {
            _view.Sidebar.ShowClinicList(Array.Empty<Models.Entities.Clinic>(), null,
                "Couldn’t load clinics. Check that SQL Server is running.");
        }
    }
}
