using System.Globalization;
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
/// Medical records. Enforces Tasks/VetCare_Medical_Records_Module.md, section 4:
///  1. only Veterinarians create or edit records; Staff and Admin view, Staff can print;
///  2. only records of the signed-in user's clinic (through Appointment.ClinicID);
///  3. a record is created only for an appointment In Consultation assigned to the signed-in vet;
///  4. one record per appointment;
///  5. diagnosis and treatment are required;
///  6. weight 0.01–200 kg, temperature 30.0–45.0 °C (warning outside the species' normal range);
///  7. saving a weight updates Pet.Weight;
///  8. prescriptions: a medicine of the clinic's stock list, quantity above 0 (warning above stock);
///  9. vaccinations: a medicine of category Vaccine, next due date after today;
/// 10. "Save &amp; complete visit" saves everything in one transaction and completes the appointment;
/// 11. only the vet who wrote a record edits it, on the same day; afterwards corrections are notes;
/// 12. records are never deleted.
/// </summary>
public sealed class MedicalRecordController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";
    private const string NoteField = "note";

    private readonly UserSession _session;
    private readonly MedicalRecordRepository _records;
    private readonly AppointmentRepository _appointments;
    private readonly PetRepository _pets;
    private readonly MedicineRepository _medicines;
    private readonly VaccinationRepository _vaccinations;
    private readonly UserRepository _users;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private MedicalRecordsPage _list;
    private int _page = 1;
    private int _refreshNumber;
    private Func<IDictionary<string, string>, Task> _pendingConfirm;

    public MedicalRecordController(UserSession session, MedicalRecordRepository records, AppointmentRepository appointments,
        PetRepository pets, MedicineRepository medicines, VaccinationRepository vaccinations, UserRepository users, ClinicRepository clinics)
    {
        _session = session;
        _records = records;
        _appointments = appointments;
        _pets = pets;
        _medicines = medicines;
        _vaccinations = vaccinations;
        _users = users;
        _clinics = clinics;
    }

    /// <summary>Back to the Appointments module (from the consultation page).</summary>
    public Action OpenAppointments { get; set; }

    /// <summary>Opens a pet's profile (Pets module). Null when the role can't.</summary>
    public Action<int> OpenPet { get; set; }

    /// <summary>The Appointments booking form (staff "Book follow-up").</summary>
    public AppointmentController Appointments { get; set; }

    // Rules ------------------------------------------------------------------------------------------

    public bool CanView => _session.ClinicId != null &&
        (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian || _session.IsAdministrator);

    /// <summary>Rule 1: only Veterinarians write records.</summary>
    public bool CanWrite => _session.ClinicId != null && _session.RoleId == RoleIds.Veterinarian;

    /// <summary>Rule 1: Staff print copies for owners (and vets may too); Administrators only view.</summary>
    public bool CanPrint => _session.ClinicId != null && (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian);

    private int ClinicId => _session.ClinicId ?? 0;
    private int UserId => _session.UserId;

    /// <summary>Rule 11.</summary>
    private bool CanEditRecord(MedicalRecord r) => CanWrite && r.IsEditableBy(UserId, DateTime.Today);

    // Data methods (section 6 of the module document) -------------------------------------------------

    public Task<PagedResult<MedicalRecord>> GetRecordsAsync(string search, int? vetId, DateTime? from, DateTime? to, bool myRecordsOnly, int page = 1) =>
        _records.SearchAsync(new MedicalRecordQuery
        {
            ClinicId = ClinicId,
            Search = search,
            VetId = myRecordsOnly ? UserId : vetId,
            From = from,
            To = to,
            Page = page,
            PageSize = 10
        });

    public Task<MedicalRecord> GetRecordByIdAsync(int recordId) => _records.GetByIdAsync(ClinicId, recordId);

    public Task<MedicalRecord> GetRecordByAppointmentAsync(int appointmentId) => _records.GetByAppointmentAsync(ClinicId, appointmentId);

    public Task<IReadOnlyList<MedicalRecord>> GetPetHistoryAsync(int petId) => _records.GetPetHistoryAsync(ClinicId, petId);

    /// <summary>Pet summary, previous visits, vaccine status and the saved draft for the consultation page.</summary>
    public async Task<ConsultationData> GetConsultationDataAsync(int appointmentId, bool editMode)
    {
        var appointment = await _appointments.GetByIdAsync(ClinicId, appointmentId);
        if (appointment?.PetId is not int petId) return null;   // a guest booking has no consultation until check-in
        var pet = await _pets.GetByIdAsync(ClinicId, petId) ?? PetFrom(appointment);
        var record = await GetRecordByAppointmentAsync(appointmentId);

        return new ConsultationData
        {
            Appointment = appointment,
            Pet = pet,
            Record = record,
            Prescriptions = record == null ? Array.Empty<RecordPrescription>() : await _records.GetPrescriptionsAsync(record.RecordId),
            Vaccinations = record == null ? Array.Empty<RecordVaccination>() : await _vaccinations.GetByRecordAsync(ClinicId, record.RecordId),
            PetVaccinations = await _vaccinations.GetByPetAsync(ClinicId, petId),
            PreviousVisits = await _records.GetPetHistoryAsync(ClinicId, petId, record?.RecordId, take: 5),
            Medicines = await _medicines.GetActiveAsync(ClinicId),
            EditMode = editMode
        };
    }

    public async Task<RecordDetails> GetPrintDataAsync(int recordId)
    {
        var record = await GetRecordByIdAsync(recordId);
        if (record == null) return null;
        bool editable = CanEditRecord(record);
        return new RecordDetails
        {
            Record = record,
            Prescriptions = await _records.GetPrescriptionsAsync(recordId),
            Vaccinations = await _vaccinations.GetByRecordAsync(ClinicId, recordId),
            Notes = await _records.GetNotesAsync(recordId),
            CanEdit = editable,
            CanAddNote = CanWrite && record.VetId == UserId && !editable && !record.IsDraft,
            CanPrint = CanPrint && !record.IsDraft,
            CanBookFollowUp = Appointments?.CanManage == true && !record.IsDraft,
            Locked = !record.IsDraft && record.RecordDate.Date < DateTime.Today
        };
    }

    /// <summary>Rules 3, 4, 7, 10 and 11, after <see cref="Validate"/>.</summary>
    public async Task<OperationResult> SaveAsync(MedicalRecord record, IReadOnlyList<RecordPrescription> prescriptions,
        IReadOnlyList<RecordVaccination> vaccinations, bool complete, bool editMode)
    {
        if (!CanWrite) return OperationResult.Fail("Only Veterinarians can write medical records.");
        record.VetId = UserId;

        var existing = await GetRecordByAppointmentAsync(record.AppointmentId);
        if (editMode && (existing == null || !CanEditRecord(existing)))
            return OperationResult.Fail("This record is locked. Add a note instead.");                  // rule 11

        var (result, recordId, detail) = await _records.SaveConsultationAsync(ClinicId, record, prescriptions, vaccinations, complete);
        if (result == RecordSaveResult.OutOfStock)
            return OperationResult.Fail(detail + " is out of stock, so nothing was saved. Remove it or add stock first.");
        if (result != RecordSaveResult.Saved)
            return OperationResult.Fail(complete
                ? "This visit is no longer in consultation with you, so it can’t be completed here."
                : "This visit is no longer in consultation with you, so the record can’t be saved.");
        record.RecordId = recordId;
        return OperationResult.Success(complete ? "Visit completed." : "Saved.");
    }

    public async Task<OperationResult> AddNoteAsync(int recordId, string note)
    {
        if (!CanWrite) return OperationResult.Fail("Only Veterinarians can add notes.");
        var record = await GetRecordByIdAsync(recordId);
        if (record == null) return OperationResult.Fail("This record no longer exists.");
        if (record.VetId != UserId) return OperationResult.Fail("Only " + record.VetName + " can add notes to this record.");
        string text = note?.Trim();
        if (string.IsNullOrEmpty(text)) return OperationResult.Invalid(new Dictionary<string, string> { [NoteField] = "Required" });
        await _records.AddNoteAsync(recordId, UserId, text);
        return OperationResult.Success("Note added.");
    }

    // Validation (rules 5, 6, 8, 9) ---------------------------------------------------------------------

    /// <param name="alreadyGiven">Vaccines already saved on a completed record (their dose is taken from stock already).</param>
    public static Dictionary<string, string> Validate(ConsultationFormData data, IReadOnlyList<Medicine> medicines, DateTime today,
        out MedicalRecord record, out List<RecordPrescription> prescriptions, out List<RecordVaccination> vaccinations,
        IReadOnlyCollection<int> alreadyGiven = null)
    {
        var stillCharged = alreadyGiven?.ToList() ?? new List<int>();
        var errors = new Dictionary<string, string>();
        record = null;
        prescriptions = new List<RecordPrescription>();
        vaccinations = new List<RecordVaccination>();

        decimal? weight = null, temperature = null;
        if (!string.IsNullOrWhiteSpace(data.Weight))
        {
            if (!TryNumber(data.Weight, out var w) || w < VitalSignsHelper.MinWeight || w > VitalSignsHelper.MaxWeight)
                errors[ConsultationFormData.WeightField] = "Enter a weight between 0.01 and 200 kg";
            else weight = Math.Round(w, 2);
        }
        if (!string.IsNullOrWhiteSpace(data.Temperature))
        {
            if (!TryNumber(data.Temperature, out var t) || t < VitalSignsHelper.MinTemperature || t > VitalSignsHelper.MaxTemperature)
                errors[ConsultationFormData.TemperatureField] = "Enter a temperature between 30 and 45 °C";
            else temperature = Math.Round(t, 1);
        }

        if (string.IsNullOrWhiteSpace(data.Diagnosis)) errors[ConsultationFormData.DiagnosisField] = "Diagnosis is required";
        if (string.IsNullOrWhiteSpace(data.Treatment)) errors[ConsultationFormData.TreatmentField] = "Treatment is required";

        for (int i = 0; i < data.Prescriptions.Count; i++)
        {
            var row = data.Prescriptions[i];
            string key = ConsultationFormData.PrescriptionField(i);
            if (row.IsDispensed)
            {
                prescriptions.Add(new RecordPrescription { PrescriptionId = row.PrescriptionId, MedicineId = row.MedicineId, IsDispensed = true });
                continue;
            }
            if (row.IsEmpty) continue;
            var medicine = medicines.FirstOrDefault(m => m.MedicineId == row.MedicineId && !m.IsVaccine);
            if (medicine == null) { errors[key] = "Pick a medicine from the clinic’s stock list."; continue; }   // rule 8
            if (medicine.IsExpired) { errors[key] = "This medicine is expired. Pick another one."; continue; }   // pharmacy rule 10
            if (!int.TryParse(row.Quantity?.Trim(), out int qty) || qty <= 0) { errors[key] = "Enter a quantity above 0."; continue; }
            if (row.Dosage?.Trim().Length > 100) { errors[key] = "Dosage: use 100 characters or fewer."; continue; }
            if (row.Instructions?.Trim().Length > 255) { errors[key] = "Instructions: use 255 characters or fewer."; continue; }
            prescriptions.Add(new RecordPrescription
            {
                MedicineId = medicine.MedicineId,
                MedicineName = medicine.MedicineName,
                StockQty = medicine.StockQty,
                Quantity = qty,
                Dosage = Optional(row.Dosage),
                Instructions = Optional(row.Instructions)
            });
        }

        for (int i = 0; i < data.Vaccinations.Count; i++)
        {
            var row = data.Vaccinations[i];
            string key = ConsultationFormData.VaccinationField(i);
            if (row.IsEmpty) continue;
            var vaccine = medicines.FirstOrDefault(m => m.MedicineId == row.MedicineId && m.IsVaccine);
            if (vaccine == null) { errors[key] = "Pick a vaccine (a medicine of the category Vaccine)."; continue; }   // rule 9
            // Vaccination rule 4: at least 1 in stock (a dose already taken for this record doesn't need more).
            bool alreadyTaken = stillCharged.Remove(vaccine.MedicineId);
            if (!alreadyTaken && vaccine.IsExpired) { errors[key] = "This vaccine is expired"; continue; }   // pharmacy rule 10
            if (!alreadyTaken && !vaccine.InStock) { errors[key] = "This vaccine is out of stock"; continue; }
            if (!int.TryParse(row.DoseNo?.Trim(), out int dose) || dose < 1) { errors[key] = "Enter a dose number of 1 or more."; continue; }
            if (row.NextDue == null || row.NextDue.Value.Date <= today.Date) { errors[key] = "Next due date must be after today"; continue; }
            vaccinations.Add(new RecordVaccination { MedicineId = vaccine.MedicineId, VaccineName = vaccine.MedicineName, DoseNo = dose, NextDueDate = row.NextDue.Value.Date });
        }

        if (data.FollowUp && (data.FollowUpDate == null || data.FollowUpDate.Value.Date <= today.Date))
            errors[ConsultationFormData.FollowUpField] = "Pick a follow-up date after today";

        if (errors.Count > 0) return errors;

        record = new MedicalRecord
        {
            Weight = weight,
            Temperature = temperature,
            Diagnosis = data.Diagnosis.Trim(),
            Treatment = data.Treatment.Trim(),
            Notes = Optional(data.Notes)
        };
        return errors;
    }

    private static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // View ---------------------------------------------------------------------------------------------

    /// <param name="recordId">Open this record's details straight away.</param>
    /// <param name="consultationAppointmentId">Open the consultation page for this appointment.</param>
    public View CreateView(int? recordId = null, int? consultationAppointmentId = null)
    {
        if (!CanView)
            return new Label
            {
                Text = "Medical records are available to Veterinarians, Cashier/Staff and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync(e.Values);
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();

        _list = new MedicalRecordsPage();
        _list.SetIsVet(CanWrite);
        _list.FiltersChanged += async (s, e) => { _page = 1; await RefreshListAsync(); };
        _list.PageRequested += async (s, p) => { _page = p; await RefreshListAsync(); };
        _list.RecordSelected += async (s, r) => await ShowDetailsAsync(r.RecordId);
        _list.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Record));

        if (consultationAppointmentId != null) _ = OpenConsultationAsync(consultationAppointmentId.Value, editMode: false);
        else if (recordId != null) _ = ShowDetailsAsync(recordId.Value);
        else _ = ShowListAsync();
        return _host;
    }

    private IReadOnlyList<MenuEntry> MenuFor(MedicalRecord r)
    {
        var entries = new List<MenuEntry> { new("View", "eye", () => _ = ShowDetailsAsync(r.RecordId)) };
        if (r.IsDraft && r.VetId == UserId && CanWrite)
            entries.Add(new MenuEntry("Continue consultation", "stethoscope", () => _ = OpenConsultationAsync(r.AppointmentId, editMode: false)));
        else if (CanEditRecord(r))
            entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenConsultationAsync(r.AppointmentId, editMode: true)));
        if (CanPrint && !r.IsDraft) entries.Add(new MenuEntry("Print", "printer", () => _ = PrintAsync(r.RecordId)));
        return entries;
    }

    private async Task ShowListAsync()
    {
        _host.ShowPage(_list);
        _list.ShowLoading();
        try
        {
            if (_list.CurrentVetId == null) _list.SetVets(await _users.GetActiveVetsAsync(ClinicId));
        }
        catch (SqlException) { /* the list shows the error below */ }
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        int mine = ++_refreshNumber;
        try
        {
            var page = await GetRecordsAsync(_list.CurrentSearch, _list.CurrentVetId, _list.CurrentFrom, _list.CurrentTo, _list.MyRecordsOnly, _page);
            if (page.Items.Count == 0 && _page > 1)
            {
                _page--;
                page = await GetRecordsAsync(_list.CurrentSearch, _list.CurrentVetId, _list.CurrentFrom, _list.CurrentTo, _list.MyRecordsOnly, _page);
            }
            var summary = await _records.GetSummaryAsync(ClinicId, DateTime.Today);
            if (mine != _refreshNumber) return;
            _list.ShowSummary(summary);
            _list.ShowRecords(page);
        }
        catch (SqlException)
        {
            if (mine == _refreshNumber) _list.ShowError(DatabaseError);
        }
    }

    // Details ------------------------------------------------------------------------------------------

    private async Task ShowDetailsAsync(int recordId)
    {
        try
        {
            var details = await GetPrintDataAsync(recordId);
            if (details == null)
            {
                await ShowListAsync();
                await ShowInfoAsync("Record not found", "This medical record isn’t in your clinic.", "circle-alert");
                return;
            }

            var page = new MedicalRecordDetailsPage();
            page.BackRequested += async (s, e) => await ShowListAsync();
            page.PrintRequested += async (s, e) => await PrintAsync(recordId);
            page.EditRequested += async (s, e) => await OpenConsultationAsync(details.Record.AppointmentId, editMode: true);
            page.AddNoteRequested += async (s, e) => await AskNoteAsync(recordId);
            page.FollowUpRequested += async (s, e) => await BookFollowUpFromDetailsAsync(details.Record);
            page.Show(details);
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            if (_host.CurrentPage == null) _host.ShowPage(_list);
            await ShowInfoAsync("Can’t open this record", DatabaseError, "circle-alert");
        }
    }

    // Consultation ---------------------------------------------------------------------------------------

    private async Task OpenConsultationAsync(int appointmentId, bool editMode)
    {
        try
        {
            if (!CanWrite)
            {
                await ShowListAsync();
                await ShowInfoAsync("View only", "Only Veterinarians write medical records.", "circle-alert");
                return;
            }

            var data = await GetConsultationDataAsync(appointmentId, editMode);
            string problem = data == null ? "This appointment isn’t in your clinic."
                : editMode ? (data.Record == null || !CanEditRecord(data.Record) ? "This record is locked. Add a note instead." : null)
                : data.Appointment.Status != AppointmentStatuses.InConsultation ? "This visit isn’t in consultation (it is " + data.Appointment.Status + ")."  // rule 3
                : data.Appointment.VetId != UserId ? "This visit is assigned to " + data.Appointment.VetName + "."
                : null;
            if (problem != null)
            {
                if (_host.CurrentPage == null) await ShowListAsync();
                await ShowInfoAsync("Can’t open the consultation", problem, "circle-alert");
                return;
            }

            var page = new ConsultationPage(data);
            page.BackRequested += async (s, e) =>
            {
                if (editMode) await ShowDetailsAsync(data.Record.RecordId);
                else if (OpenAppointments != null) OpenAppointments();
                else await ShowListAsync();
            };
            page.PetHistoryRequested += (s, petId) => OpenPet?.Invoke(petId);
            page.SaveRequested += async (s, complete) => await SaveConsultationAsync(page, data, complete);
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            if (_host.CurrentPage == null) _host.ShowPage(_list);
            await ShowInfoAsync("Can’t open the consultation", DatabaseError, "circle-alert");
        }
    }

    private async Task SaveConsultationAsync(ConsultationPage page, ConsultationData data, bool complete)
    {
        var form = page.Read();
        // On a completed visit, the vaccines already saved were taken from stock; they don't need stock again.
        var alreadyGiven = data.Appointment.Status == AppointmentStatuses.Completed
            ? data.Vaccinations.Select(v => v.MedicineId).ToList()
            : new List<int>();
        var errors = Validate(form, data.Medicines, DateTime.Today, out var record, out var prescriptions, out var vaccinations, alreadyGiven);
        if (errors.Count > 0)
        {
            page.ShowResult(OperationResult.Invalid(errors));
            return;
        }
        record.AppointmentId = data.Appointment.AppointmentId;

        if (complete)
        {
            // Confirm first: "Complete Bantay's visit? This saves the record, sends …"
            int rx = prescriptions.Count(p => !p.IsDispensed), vx = vaccinations.Count;
            string message = "This saves the record" +
                (rx > 0 ? ", sends " + rx + (rx == 1 ? " prescription" : " prescriptions") + " to the pharmacy" : string.Empty) +
                (vx > 0 ? ", records " + vx + (vx == 1 ? " vaccine" : " vaccines") : string.Empty) +
                ", and marks the appointment as completed.";
            _pendingConfirm = async _ =>
            {
                await CloseDialogAsync();
                await DoSaveAsync(page, data, form, record, prescriptions, vaccinations, complete: true);
            };
            await _host.Dialog.ShowConfirmAsync("Complete " + data.Pet.PetName + "’s visit?", message, "Complete visit",
                danger: false, icon: "circle-check", cancelText: "Go back");
            return;
        }

        await DoSaveAsync(page, data, form, record, prescriptions, vaccinations, complete: false);
    }

    private async Task DoSaveAsync(ConsultationPage page, ConsultationData data, ConsultationFormData form, MedicalRecord record,
        List<RecordPrescription> prescriptions, List<RecordVaccination> vaccinations, bool complete)
    {
        page.SetBusy(true);
        try
        {
            var result = await SaveAsync(record, prescriptions, vaccinations, complete, data.EditMode);
            if (!result.Ok)
            {
                page.ShowResult(result);
                return;
            }

            if (data.EditMode)
            {
                await ShowDetailsAsync(record.RecordId);
                return;
            }
            if (!complete)
            {
                page.ShowSaved(DateTime.Now);
                return;
            }

            string followUp = form.FollowUp && form.FollowUpDate != null
                ? " " + await BookFollowUpAsync(data.Appointment, form.FollowUpDate.Value, form.SameVet)
                : string.Empty;
            await ShowDetailsAsync(record.RecordId);
            await ShowInfoAsync("Visit completed", "Ready for billing." + followUp, "circle-check");
        }
        catch (SqlException)
        {
            page.ShowResult(OperationResult.Fail(DatabaseError));
        }
        finally
        {
            page.SetBusy(false);
        }
    }

    /// <summary>
    /// The follow-up from the consultation form: the first free slot that day with the same vet
    /// (or, if not "Same vet", any active vet). Returns a sentence for the completion message.
    /// </summary>
    private async Task<string> BookFollowUpAsync(Appointment visit, DateTime date, bool sameVet)
    {
        try
        {
            var vets = await _users.GetActiveVetsAsync(ClinicId);
            var candidates = vets.Where(v => v.UserId == visit.VetId).Concat(sameVet ? Array.Empty<User>() : vets.Where(v => v.UserId != visit.VetId)).ToList();
            var now = DateTime.Now;
            foreach (var vet in candidates)
            {
                var taken = await _appointments.GetActiveTimesAsync(ClinicId, vet.UserId, date, null);
                foreach (var slot in TimeSlotHelper.AllSlots())
                {
                    if (TimeSlotHelper.IsPast(date, slot, now) || TimeSlotHelper.IsTaken(slot, taken)) continue;
                    var followUp = new Appointment
                    {
                        ClinicId = ClinicId,
                        PetId = visit.PetId,
                        VetId = vet.UserId,
                        CreatedBy = UserId,
                        AppointmentDate = date.Date,
                        AppointmentTime = slot,
                        Reason = "Follow-up",
                        Status = AppointmentStatuses.Scheduled,
                        SyncStatus = SyncStatuses.Synced
                    };
                    var (saved, _) = await _appointments.InsertAsync(followUp, exactTimeOnly: false);
                    if (saved == SlotWriteResult.Saved)
                        return "Follow-up booked for " + date.ToString("MMM d") + " at " + TimeSlotHelper.Format(slot) + " with Dr. " + vet.FullName + ".";
                }
            }
            return "No free slot on " + date.ToString("MMM d") + " — book the follow-up from Appointments.";
        }
        catch (SqlException)
        {
            return "The follow-up couldn’t be booked — book it from Appointments.";
        }
    }

    /// <summary>Staff "Book follow-up" on the record: the booking form with the pet, vet and "Follow-up" filled in.</summary>
    private async Task BookFollowUpFromDetailsAsync(MedicalRecord r)
    {
        if (Appointments == null) return;
        var owner = new Owner { OwnerId = r.OwnerId, FirstName = r.OwnerFirstName, LastName = r.OwnerLastName, ContactNo = r.OwnerContactNo };
        await Appointments.OpenBookFormAsync(_host, owner, r.PetId, async a =>
            await ShowInfoAsync("Follow-up booked",
                "Follow-up booked for " + r.PetName + " on " + a.AppointmentDate.ToString("MMM d") + " at " + a.TimeText + ".", "calendar-plus"),
            vetId: r.VetId, reasonChip: "Follow-up");
    }

    // Notes and printing ----------------------------------------------------------------------------------

    private async Task AskNoteAsync(int recordId)
    {
        _pendingConfirm = async values =>
        {
            values.TryGetValue(NoteField, out var note);
            var result = await AddNoteAsync(recordId, note);
            if (!result.Ok)
            {
                _host.Dialog.ShowResult(result);
                return;
            }
            await CloseDialogAsync();
            await ShowDetailsAsync(recordId);
        };
        await _host.Dialog.ShowConfirmWithNoteAsync("Add a note",
            "The record stays as it was written; the note is added below it with today’s date.",
            "Add note", danger: false, icon: "notebook-pen", NoteField, "Note", "What needs correcting or adding?", 2000);
    }

    /// <summary>Section 3.D: opens the printable copy in the browser, which shows the print dialog.</summary>
    private async Task PrintAsync(int recordId)
    {
        if (!CanPrint) return;
        try
        {
            var details = await GetPrintDataAsync(recordId);
            if (details == null) return;
            var clinic = await _clinics.GetByIdAsync(ClinicId);
            var due = await _vaccinations.GetNextDueByPetsAsync(ClinicId, new[] { details.Record.PetId });
            string html = RecordPrintDocument.Build(details, clinic, due.TryGetValue(details.Record.PetId, out var d) ? d : null, DateTime.Now);

            string path = Path.Combine(FileSystem.CacheDirectory, "VetCare-medical-record-" + recordId + ".html");
            await File.WriteAllTextAsync(path, html);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Medical record", new ReadOnlyFile(path)));
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t print right now", DatabaseError, "circle-alert");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowInfoAsync("Can’t print right now", "The printable copy couldn’t be created: " + ex.Message, "circle-alert");
        }
    }

    // Helpers -------------------------------------------------------------------------------------------

    private static Pet PetFrom(Appointment a) => new()
    {
        PetId = a.PetId ?? 0,
        PetName = a.PetName,
        Species = a.Species,
        Breed = a.Breed,
        Sex = a.PetSex,
        OwnerId = a.OwnerId,
        OwnerFirstName = a.OwnerFirstName,
        OwnerLastName = a.OwnerLastName,
        OwnerContactNo = a.OwnerContactNo
    };

    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync(IDictionary<string, string> values)
    {
        var action = _pendingConfirm;
        if (action == null)
        {
            await CloseDialogAsync();
            return;
        }
        _host.Dialog.SetBusy(true);
        try
        {
            await action(values ?? new Dictionary<string, string>());
        }
        catch (SqlException)
        {
            _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError));
        }
        finally
        {
            _host.Dialog.SetBusy(false);
        }
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
