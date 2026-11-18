using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Admin;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;
using VetCare.Views.Veterinarian;

namespace VetCare.Controllers;

/// <summary>
/// The Dashboard (Tasks/VetCare_Dashboard_Module.md, section 6):
/// 1. the version follows the role (Administrator, Veterinarian, Cashier/Staff);
/// 2. every number is filtered by the clinic, and the vet's by VetID = the signed-in vet;
/// 3. read-only — actions open the real module page or panel, whose own rules still apply;
/// 4. calculated when the page opens, refreshed every 60 seconds and after "Sync now";
/// 5. money only for Administrators and Cashier/Staff (the vet's data has no money at all);
/// 6. "today" is this computer's local date;  7. lists show at most 5 items with "View all";
/// 8. archived records are never counted (DashboardRepository).
/// </summary>
public sealed partial class DashboardController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly DashboardRepository _dashboard;
    private ModuleHostView _host;
    private DashboardPageBase _page;
    private IDispatcherTimer _timer;
    private int _refreshNumber;

    public DashboardController(UserSession session, DashboardRepository dashboard)
    {
        _session = session;
        _dashboard = dashboard;
    }

    /// <summary>Open a module: (menu key, record id, shortcut).</summary>
    public Action<string, int?, string> OpenModule { get; set; } = (_, _, _) => { };

    /// <summary>Opens the consultation page for an appointment (vet).</summary>
    public Action<int> OpenConsultation { get; set; }

    /// <summary>Opens "Create bill" for a completed visit (staff).</summary>
    public Action<int> OpenCreateBill { get; set; }

    /// <summary>The Appointments controller, for Start consultation and the booking / walk-in panels.</summary>
    public AppointmentController Appointments { get; set; }

    private int ClinicId => _session.ClinicId ?? 0;
    private bool IsVet => _session.RoleId == RoleIds.Veterinarian;
    private bool IsStaff => _session.RoleId == RoleIds.CashierStaff;
    private bool IsAdmin => _session.IsAdministrator;

    public bool CanView => _session.ClinicId != null && (IsVet || IsStaff || IsAdmin);

    // Data methods (section 8) ---------------------------------------------------------------------------------

    public Task<AdminDashboardData> GetAdminDashboardAsync() => _dashboard.GetAdminAsync(ClinicId, DateTime.Today);

    public Task<VetDashboardData> GetVetDashboardAsync() => _dashboard.GetVetAsync(ClinicId, _session.UserId, DateTime.Today);

    public Task<StaffDashboardData> GetStaffDashboardAsync() => _dashboard.GetStaffAsync(ClinicId, DateTime.Today);

    public async Task<Appointment> GetNextPatientAsync() => (await GetVetDashboardAsync()).NextPatient;

    public async Task<IReadOnlyList<QueueItem>> GetTodayQueueAsync() => (await GetStaffDashboardAsync()).Queue;

    public Task<SyncState> GetSyncStatusAsync() => _dashboard.GetSyncStateAsync(ClinicId);

    /// <summary>No sync engine yet: "Sync now" reloads the numbers from the database.</summary>
    public Task SyncNowAsync() => RefreshAsync();

    // View ---------------------------------------------------------------------------------------------------------

    public View CreateView()
    {
        if (!CanView)
            return new Label
            {
                Text = "The dashboard is for clinic Administrators, Veterinarians and Cashier/Staff.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await _host.Dialog.HideAsync();
        _host.Dialog.Confirmed += async (s, e) => await _host.Dialog.HideAsync();

        _page = IsVet ? new VetDashboardPage() : IsStaff ? new StaffDashboardPage() : new AdminDashboardPage();
        _page.Actions = Actions();
        _host.ShowPage(_page);

        // Rule 4: every 60 seconds while the page is shown.
        _timer = _host.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(60);
        _timer.Tick += async (s, e) => { if (!_host.Panel.IsOpen && !_host.Dialog.IsOpen) await RefreshAsync(); };
        _host.Loaded += (s, e) => _timer.Start();
        _host.Unloaded += (s, e) => _timer.Stop();

        _ = RefreshAsync();
        return _host;
    }

    private async Task RefreshAsync()
    {
        int mine = ++_refreshNumber;
        var sync = await GetSyncStatusAsync();
        if (!sync.Online)
        {
            if (mine == _refreshNumber) _page.ShowError("You're offline. " + DatabaseError);
            return;
        }
        try
        {
            string clinic = _session.ClinicName;
            var (first, last) = PersonName.Split(_session.FullName);
            switch (_page)
            {
                case VetDashboardPage vet:
                {
                    var data = await GetVetDashboardAsync();
                    if (mine != _refreshNumber) return;
                    vet.Show(data, "Dr. " + (string.IsNullOrEmpty(last) ? first : last), clinic, sync);
                    break;
                }
                case StaffDashboardPage staff:
                {
                    var data = await GetStaffDashboardAsync();
                    if (mine != _refreshNumber) return;
                    staff.Show(data, first, clinic, sync);
                    break;
                }
                case AdminDashboardPage admin:
                {
                    var data = await GetAdminDashboardAsync();
                    if (mine != _refreshNumber) return;
                    admin.Show(data, first, clinic, sync);
                    break;
                }
            }
        }
        catch (SqlException)
        {
            if (mine == _refreshNumber) _page.ShowError(DatabaseError);
        }
        catch (Exception ex)
        {
            if (mine == _refreshNumber) _page.ShowError("The dashboard couldn't be loaded: " + ex.Message);
        }
    }

    /// <summary>Rule 3: every action opens the real module page or panel.</summary>
    private DashboardActions Actions() => new()
    {
        OpenModule = (key, id, intent) => OpenModule(key, id, intent),
        OpenAppointment = a => OpenModule(MenuCatalog.Appointments, a.AppointmentId, null),
        StartConsultation = a => _ = StartConsultationAsync(a),
        CreateBill = appointmentId => OpenCreateBill?.Invoke(appointmentId),
        ReceivePayment = billId => OpenModule(MenuCatalog.Billing, billId, "pay"),
        Dispense = id => OpenModule(MenuCatalog.Prescriptions, null, "dispense:" + id),
        OpenRecord = id => OpenModule(MenuCatalog.MedicalRecords, id, null),
        BookFollowUp = v => _ = BookFollowUpAsync(v),
        BookAppointment = () => _ = BookAsync(),
        WalkIn = () => _ = WalkInAsync(),
        SyncNow = () => _ = SyncNowAsync()
    };

    /// <summary>Vet: "Start consultation" (Checked In → In Consultation, appointment rules apply) or "Continue".</summary>
    private async Task StartConsultationAsync(Appointment a)
    {
        if (OpenConsultation == null || Appointments == null) return;
        try
        {
            if (a.Status == AppointmentStatuses.CheckedIn)
            {
                var result = await Appointments.StartConsultationAsync(a.AppointmentId);
                if (!result.Ok)
                {
                    await _host.Dialog.ShowConfirmAsync("Can’t start the consultation", result.Message, "OK", danger: false, icon: "circle-alert", showCancel: false);
                    await RefreshAsync();
                    return;
                }
            }
            OpenConsultation(a.AppointmentId);
        }
        catch (SqlException)
        {
            await _host.Dialog.ShowConfirmAsync("Can’t start the consultation", DatabaseError, "OK", danger: false, icon: "circle-alert", showCancel: false);
        }
    }

    private async Task BookAsync()
    {
        if (Appointments?.CanManage != true) return;
        await Appointments.OpenFormAsync(_host, new AppointmentFormData { Mode = AppointmentFormMode.Book }, null, async _ => await RefreshAsync());
    }

    private async Task WalkInAsync()
    {
        if (Appointments?.CanManage != true) return;
        await Appointments.OpenWalkInAsync(_host, async _ => await RefreshAsync());
    }

    /// <summary>Staff: "Book" on a vaccine follow-up — the real booking panel with the pet and "Vaccination" filled in.</summary>
    private async Task BookFollowUpAsync(RecordVaccination v)
    {
        if (Appointments?.CanManage != true) return;
        var owner = new Owner { OwnerId = v.OwnerId, FirstName = v.OwnerFirstName, LastName = v.OwnerLastName, ContactNo = v.OwnerContactNo };
        string details = "Due: " + v.VaccineName + " " + VaccinationPrintDocument.NextDoseText(v);
        int max = AppointmentFormData.ReasonMaxLength - "Vaccination: ".Length;
        if (details.Length > max) details = details.Substring(0, max);
        await Appointments.OpenBookFormAsync(_host, owner, v.PetId, async _ => await RefreshAsync(), reasonChip: "Vaccination", reasonDetails: details);
    }
}
