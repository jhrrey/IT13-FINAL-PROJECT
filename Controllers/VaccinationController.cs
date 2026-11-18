using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Veterinarian;

namespace VetCare.Controllers;

/// <summary>
/// Vaccinations. Enforces Tasks/VetCare_Vaccination_Module.md, section 4:
///  1. only Veterinarians record or edit vaccinations; Staff and Admin view, Staff print and book follow-ups;
///  2. only the clinic's vaccinations (through the pet's owner);
///  3. a vaccination is recorded during a consultation, linked to its medical record (MedicalRecordController);
///  4. the item is a Medicine of category Vaccine with at least 1 in stock;
///  5. recording one lowers its stock by 1 and logs a Stock Out (VisitCharges, on "Save &amp; complete visit");
///  6. it is added to the visit's bill as a Vaccine item (VisitCharges);
///  7. date given not in the future; next due date after the date given;
///  8. dose number suggested, 1 or higher;
///  9. default next due dates (Helpers/VaccineScheduleHelper);
/// 10. only the vet who gave it edits it, on the same day;
/// 11. never deleted.
/// </summary>
public sealed class VaccinationController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly VaccinationRepository _vaccinations;
    private readonly MedicineRepository _medicines;
    private readonly PetRepository _pets;
    private readonly UserRepository _users;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private VaccinationPage _page;
    private int _recordsPage = 1;
    private int _refreshNumber;
    private Func<Task> _pendingConfirm;

    public VaccinationController(UserSession session, VaccinationRepository vaccinations, MedicineRepository medicines,
        PetRepository pets, UserRepository users, ClinicRepository clinics)
    {
        _session = session;
        _vaccinations = vaccinations;
        _medicines = medicines;
        _pets = pets;
        _users = users;
        _clinics = clinics;
    }

    public Action<int> OpenPet { get; set; }

    /// <summary>Opens the medical record where a vaccine was given.</summary>
    public Action<int> OpenRecord { get; set; }

    /// <summary>The Appointments booking form ("Book" / "Book vaccination").</summary>
    public AppointmentController Appointments { get; set; }

    // Rules ----------------------------------------------------------------------------------------

    public bool CanView => _session.ClinicId != null &&
        (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian || _session.IsAdministrator);

    /// <summary>Rule 1: only Veterinarians edit vaccinations.</summary>
    public bool CanEdit => _session.ClinicId != null && _session.RoleId == RoleIds.Veterinarian;

    /// <summary>Rule 1: Staff (and vets) print cards; Administrators only view.</summary>
    public bool CanPrint => _session.ClinicId != null && (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian);

    /// <summary>Rule 1: Staff book follow-up visits.</summary>
    public bool CanBook => Appointments?.CanManage == true;

    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 6 of the module document) ---------------------------------------------------

    public Task<IReadOnlyList<RecordVaccination>> GetDueListAsync(DueFilter filter, int? vaccineId, string species) =>
        _vaccinations.GetDueListAsync(ClinicId, filter, vaccineId, species, DateTime.Today);

    public Task<PagedResult<RecordVaccination>> GetAllRecordsAsync(string search, int? vaccineId, int? vetId, DateTime? from, DateTime? to, int page) =>
        _vaccinations.SearchAsync(new VaccinationQuery { ClinicId = ClinicId, Search = search, MedicineId = vaccineId, VetId = vetId, From = from, To = to, Page = page });

    public Task<IReadOnlyList<RecordVaccination>> GetPetVaccinationsAsync(int petId) => _vaccinations.GetByPetAsync(ClinicId, petId);

    public Task<VaccinationSummary> GetSummaryAsync() => _vaccinations.GetSummaryAsync(ClinicId, DateTime.Today);

    /// <summary>Medicines of the category Vaccine, with their stock.</summary>
    public async Task<IReadOnlyList<Medicine>> GetVaccineOptionsAsync() =>
        (await _medicines.GetActiveAsync(ClinicId)).Where(m => m.IsVaccine).ToList();

    /// <summary>Rule 8: the last dose of that vaccine + 1.</summary>
    public async Task<int> SuggestDoseAsync(int petId, int medicineId) =>
        (await GetPetVaccinationsAsync(petId)).Where(v => v.MedicineId == medicineId).Select(v => v.DoseNo).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Rule 9.</summary>
    public static DateTime SuggestNextDueDate(string vaccineName, int doseNumber, DateTime dateGiven) =>
        VaccineScheduleHelper.SuggestNextDue(vaccineName, doseNumber, dateGiven).Date;

    /// <summary>Rules 1, 4, 7, 8 and 10.</summary>
    public async Task<OperationResult> UpdateVaccinationAsync(RecordVaccination current, int medicineId, string doseText, DateTime? nextDue)
    {
        if (!CanEdit) return OperationResult.Fail("Only Veterinarians can edit vaccinations.");
        if (!current.IsEditableBy(_session.UserId, DateTime.Today))
            return OperationResult.Fail("Only the vet who gave this vaccine can edit it, on the same day.");

        var errors = new Dictionary<string, string>();
        var vaccines = await GetVaccineOptionsAsync();
        var vaccine = vaccines.FirstOrDefault(v => v.MedicineId == medicineId);
        if (vaccine == null) errors[VaccinationEditView.VaccineField] = "Pick a vaccine";
        else if (medicineId != current.MedicineId && !vaccine.InStock) errors[VaccinationEditView.VaccineField] = "This vaccine is out of stock";
        if (!int.TryParse(doseText?.Trim(), out int dose) || dose < 1) errors[VaccinationEditView.DoseField] = "Enter a dose number of 1 or more";
        if (nextDue != null && nextDue.Value.Date <= current.DateGiven.Date)
            errors[VaccinationEditView.NextDueField] = "Next due date must be after the date given";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var changed = new RecordVaccination
        {
            VaccinationId = current.VaccinationId,
            MedicineId = medicineId,
            VaccineName = vaccine.MedicineName,
            DoseNo = dose,
            NextDueDate = nextDue
        };
        var (result, detail) = await _vaccinations.UpdateAsync(ClinicId, changed, _session.UserId);
        return result switch
        {
            VaccinationSaveResult.Saved => OperationResult.Success("Vaccination updated."),
            VaccinationSaveResult.OutOfStock => OperationResult.Invalid(new Dictionary<string, string> { [VaccinationEditView.VaccineField] = detail + " is out of stock" }),
            _ => OperationResult.Fail("This vaccination can no longer be edited.")
        };
    }

    public async Task<(Pet Pet, IReadOnlyList<RecordVaccination> Vaccinations, Clinic Clinic)> GetCardPrintDataAsync(int petId)
    {
        var pet = await _pets.GetByIdAsync(ClinicId, petId);
        return (pet, pet == null ? Array.Empty<RecordVaccination>() : await GetPetVaccinationsAsync(petId), await _clinics.GetByIdAsync(ClinicId));
    }

    // View ---------------------------------------------------------------------------------------------

    public View CreateView()
    {
        if (!CanView)
            return new Label
            {
                Text = "Vaccinations are available to Veterinarians, Cashier/Staff and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync();

        _page = new VaccinationPage();
        _page.SetCanBook(CanBook);
        _page.DueFiltersChanged += async (s, e) => await RefreshAsync();
        _page.RecordFiltersChanged += async (s, e) => { _recordsPage = 1; await RefreshAsync(); };
        _page.PageRequested += async (s, p) => { _recordsPage = p; await RefreshAsync(); };
        _page.TabChanged += async (s, tab) => await RefreshAsync();
        _page.BookRequested += async (s, v) => await BookAsync(_host, v.PetId, v);
        _page.CardRequested += async (s, v) => await ShowCardAsync(_host, v.PetId);
        _page.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Vaccination, e.Due));
        _page.PrintDueListRequested += async (s, e) => await PrintDueListAsync();
        _host.ShowPage(_page);
        _page.ShowLoading();
        _ = LoadAsync();
        return _host;
    }

    private async Task LoadAsync()
    {
        try
        {
            _page.SetOptions(await GetVaccineOptionsAsync(), await _users.GetActiveVetsAsync(ClinicId));
        }
        catch (SqlException) { /* shown by the refresh below */ }
        if (StartIntent == "overdue") _page.ApplyFilter(DueFilter.Overdue);          // refreshes
        else if (StartIntent == "due") _page.ApplyFilter(DueFilter.Due30Days);
        else await RefreshAsync();
    }

    /// <summary>Dashboard shortcut: "overdue" or "due" opens the due list with that filter.</summary>
    public string StartIntent { get; set; }

    private async Task RefreshAsync()
    {
        int mine = ++_refreshNumber;
        try
        {
            var summary = await GetSummaryAsync();
            if (_page.CurrentTab == 0)
            {
                var due = await GetDueListAsync(_page.CurrentFilter, _page.CurrentDueVaccineId, _page.CurrentSpecies);
                if (mine != _refreshNumber) return;
                _page.ShowSummary(summary);
                _page.ShowDueList(due);
            }
            else
            {
                var page = await GetAllRecordsAsync(_page.CurrentSearch, _page.CurrentRecordVaccineId, _page.CurrentVetId, _page.CurrentFrom, _page.CurrentTo, _recordsPage);
                if (page.Items.Count == 0 && _recordsPage > 1)
                {
                    _recordsPage--;
                    page = await GetAllRecordsAsync(_page.CurrentSearch, _page.CurrentRecordVaccineId, _page.CurrentVetId, _page.CurrentFrom, _page.CurrentTo, _recordsPage);
                }
                if (mine != _refreshNumber) return;
                _page.ShowSummary(summary);
                _page.ShowRecords(page);
            }
        }
        catch (SqlException)
        {
            if (mine == _refreshNumber) _page.ShowError(DatabaseError);
        }
    }

    private IReadOnlyList<MenuEntry> MenuFor(RecordVaccination v, bool due)
    {
        var entries = new List<MenuEntry>();
        if (due)
        {
            if (OpenPet != null) entries.Add(new MenuEntry("View pet", "paw-print", () => OpenPet(v.PetId)));
        }
        else
        {
            if (v.RecordId is int recordId && OpenRecord != null) entries.Add(new MenuEntry("View record", "clipboard-plus", () => OpenRecord(recordId)));
            if (CanEdit && v.IsEditableBy(_session.UserId, DateTime.Today))
                entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenEditAsync(v)));
        }
        entries.Add(new MenuEntry("Vaccination card", "shield-check", () => _ = ShowCardAsync(_host, v.PetId)));
        if (CanPrint) entries.Add(new MenuEntry("Print card", "printer", () => _ = PrintCardAsync(v.PetId)));
        return entries;
    }

    // Vaccination card (shared with the Pets module) ------------------------------------------------------

    /// <summary>Opens the pet's vaccination card in <paramref name="host"/>'s side panel.</summary>
    public async Task ShowCardAsync(ModuleHostView host, int petId)
    {
        try
        {
            var (pet, vaccinations, _) = await GetCardPrintDataAsync(petId);
            if (pet == null) return;
            var card = new VaccinationCardView(pet, vaccinations, CanPrint, CanBook);
            card.CloseRequested += async (s, e) => await host.Panel.HideAsync();
            card.PrintRequested += async (s, e) => await PrintCardAsync(petId);
            card.BookRequested += async (s, e) =>
            {
                var next = vaccinations.GroupBy(v => v.MedicineId)
                    .Select(g => g.OrderByDescending(v => v.DateGiven).First())
                    .Where(v => v.NextDueDate != null).OrderBy(v => v.NextDueDate).FirstOrDefault();
                await BookAsync(host, petId, next);
            };
            await host.Panel.ShowAsync(card, VaccinationCardView.PanelWidth);
        }
        catch (SqlException)
        {
            if (_host != null) await ShowInfoAsync("Can’t open the card", DatabaseError, "circle-alert");
        }
    }

    /// <summary>Book appointment with the pet filled in, "Vaccination" as the reason and what's due in the details.</summary>
    private async Task BookAsync(ModuleHostView host, int petId, RecordVaccination due)
    {
        if (!CanBook) return;
        try
        {
            var pet = await _pets.GetByIdAsync(ClinicId, petId);
            if (pet == null) return;
            var owner = new Owner { OwnerId = pet.OwnerId, FirstName = pet.OwnerFirstName, LastName = pet.OwnerLastName, ContactNo = pet.OwnerContactNo };
            string details = due == null ? null : "Due: " + due.VaccineName + " " + VaccinationPrintDocument.NextDoseText(due);
            if (details?.Length > AppointmentFormData.ReasonMaxLength - "Vaccination: ".Length)
                details = details.Substring(0, AppointmentFormData.ReasonMaxLength - "Vaccination: ".Length);
            await Appointments.OpenBookFormAsync(host, owner, petId, async a =>
            {
                if (_host != null && host == _host)
                    await ShowInfoAsync("Vaccination booked",
                        "Booked for " + pet.PetName + " on " + a.AppointmentDate.ToString("MMM d") + " at " + a.TimeText + ".", "calendar-plus");
            }, reasonChip: "Vaccination", reasonDetails: details);
        }
        catch (SqlException)
        {
            if (_host != null) await ShowInfoAsync("Can’t book right now", DatabaseError, "circle-alert");
        }
    }

    // Edit (rule 10) ------------------------------------------------------------------------------------

    private async Task OpenEditAsync(RecordVaccination v)
    {
        try
        {
            var form = new VaccinationEditView(v, await GetVaccineOptionsAsync());
            form.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
            form.SaveRequested += async (s, e) =>
            {
                var (medicineId, dose, nextDue) = form.Read();
                form.SetBusy(true);
                try
                {
                    var result = await UpdateVaccinationAsync(v, medicineId, dose, nextDue);
                    if (!result.Ok)
                    {
                        form.ShowResult(result);
                        return;
                    }
                    await _host.Panel.HideAsync();
                    await RefreshAsync();
                }
                catch (SqlException)
                {
                    form.ShowResult(OperationResult.Fail(DatabaseError));
                }
                finally
                {
                    form.SetBusy(false);
                }
            };
            await _host.Panel.ShowAsync(form, VaccinationEditView.PanelWidth);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t edit right now", DatabaseError, "circle-alert");
        }
    }

    // Printing -----------------------------------------------------------------------------------------

    public async Task PrintCardAsync(int petId)
    {
        if (!CanPrint) return;
        try
        {
            var (pet, vaccinations, clinic) = await GetCardPrintDataAsync(petId);
            if (pet == null) return;
            await OpenHtmlAsync("VetCare-vaccination-card-" + petId + ".html",
                VaccinationPrintDocument.BuildCard(pet, vaccinations, clinic, DateTime.Now));
        }
        catch (SqlException)
        {
            if (_host != null) await ShowInfoAsync("Can’t print right now", DatabaseError, "circle-alert");
        }
    }

    private async Task PrintDueListAsync()
    {
        try
        {
            var due = await GetDueListAsync(_page.CurrentFilter, _page.CurrentDueVaccineId, _page.CurrentSpecies);
            var clinic = await _clinics.GetByIdAsync(ClinicId);
            await OpenHtmlAsync("VetCare-vaccines-due.html",
                VaccinationPrintDocument.BuildDueList(due, clinic, _page.DueFilterText, DateTime.Today, DateTime.Now));
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t print right now", DatabaseError, "circle-alert");
        }
    }

    /// <summary>Writes the printable page and opens it in the browser (which shows the print dialog).</summary>
    private async Task OpenHtmlAsync(string fileName, string html)
    {
        try
        {
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(path, html);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Print", new ReadOnlyFile(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_host != null) await ShowInfoAsync("Can’t print right now", "The printable page couldn’t be created: " + ex.Message, "circle-alert");
        }
    }

    // Dialogs ------------------------------------------------------------------------------------------

    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync()
    {
        var action = _pendingConfirm;
        if (action == null) { await CloseDialogAsync(); return; }
        await action();
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
