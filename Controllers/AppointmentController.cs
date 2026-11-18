using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;
using VetCare.Views.Veterinarian;

namespace VetCare.Controllers;

/// <summary>
/// Appointments. Enforces Tasks/VetCare_Appointments_Module.md, section 5:
///  1. only Cashier/Staff book, edit, check in, cancel or mark no-show; only the assigned vet starts
///     and completes a consultation; Administrators view only;
///  2. only the signed-in user's clinic (Session.ClinicID);
///  3. vet, date, time and reason are required; a guest booking needs the owner name, contact no., pet name and
///     species; a returning client needs the pet;
///  4. no double booking: a vet can't have two active appointments in the same slot;
///  5. no booking a date or time that has passed (walk-ins use "now");
///  6. a returning client's pet must be active and its owner must belong to the clinic;
///  7. the vet must be an active Veterinarian of the clinic;
///  8. check-in only on the appointment day; a guest is registered or matched first (guest check-in panel);
///  9. no-show only after the appointment time;
/// 10. Completed needs a medical record for the visit;
/// 11. Completed, Cancelled and No-show can't be edited;
/// 12. rescheduling changes only the date and time, with the same double-booking check;
/// 13. cancelling changes the status (history is kept);
/// 14. slots follow the clinic's hours (Helpers/TimeSlotHelper);
/// 15. guest name and contact no. of cancelled / no-show guest bookings are cleared after 30 days (on sign-in).
/// </summary>
public sealed class AppointmentController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";
    private const string CancelReasonField = "cancelReason";

    private readonly UserSession _session;
    private readonly AppointmentRepository _appointments;
    private readonly UserRepository _users;
    private readonly PetRepository _pets;
    private readonly OwnerRepository _owners;

    private ModuleHostView _host;
    private AppointmentsPage _page;
    private VetSchedulePage _schedule;
    private IReadOnlyList<User> _vets = Array.Empty<User>();
    private int _listPage = 1;
    private int _refreshNumber;
    private Func<IDictionary<string, string>, Task> _pendingConfirm;

    public AppointmentController(UserSession session, AppointmentRepository appointments, UserRepository users,
        PetRepository pets, OwnerRepository owners)
    {
        _session = session;
        _appointments = appointments;
        _users = users;
        _pets = pets;
        _owners = owners;
    }

    /// <summary>Opens a pet's profile in the Pets module ("View pet →"). Null when the role can't open Pets.</summary>
    public Action<int> OpenPet { get; set; }

    /// <summary>Opens another sidebar module (e.g. Medical records). Null when not wired.</summary>
    public Action<string> OpenModule { get; set; }

    /// <summary>Opens the consultation page for an appointment (Medical records). Null when the role can't.</summary>
    public Action<int> OpenConsultation { get; set; }

    /// <summary>Opens a medical record's details (Medical records). Null when the role can't.</summary>
    public Action<int> OpenRecord { get; set; }

    /// <summary>"Create bill" on a completed visit: opens Bills & payments on that visit (or its existing bill).</summary>
    public Action<int> OpenCreateBill { get; set; }

    // Rules ----------------------------------------------------------------------------------------

    /// <summary>Cashier/Staff, Veterinarians and Administrators of a clinic.</summary>
    public bool CanView => _session.ClinicId != null &&
        (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian || _session.IsAdministrator);

    /// <summary>Rule 1: Cashier/Staff book, edit, check in, cancel and mark no-show.</summary>
    public bool CanManage => _session.ClinicId != null && _session.RoleId == RoleIds.CashierStaff;

    private bool IsVet => _session.ClinicId != null && _session.RoleId == RoleIds.Veterinarian;

    /// <summary>Rule 1: only the assigned vet starts and completes the consultation.</summary>
    private bool IsAssignedVet(Appointment a) => IsVet && a.VetId == _session.UserId;

    /// <summary>Rule 2: always the signed-in user's clinic.</summary>
    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 7 of the module document) -----------------------------------------------

    public Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(DateTime date, int? vetId, string status, string search) =>
        _appointments.GetRangeAsync(new AppointmentQuery { ClinicId = ClinicId, From = date, To = date, VetId = vetId, Status = status, Search = search });

    public Task<IReadOnlyList<Appointment>> GetVetScheduleAsync(int vetId, DateTime date) =>
        _appointments.GetRangeAsync(new AppointmentQuery { ClinicId = ClinicId, From = date, To = date, VetId = vetId });

    public Task<Appointment> GetAppointmentByIdAsync(int appointmentId) => _appointments.GetByIdAsync(ClinicId, appointmentId);

    public Task<AppointmentDaySummary> GetTodaySummaryAsync(DateTime date, int? vetId = null) =>
        _appointments.GetDaySummaryAsync(ClinicId, date, vetId);

    /// <summary>Every clinic slot of the day with its state (rule 14; taken = rule 4; past = rule 5).</summary>
    public async Task<IReadOnlyList<(TimeSpan Time, SlotState State)>> GetAvailableSlotsAsync(int vetId, DateTime date,
        int? excludeAppointmentId = null, TimeSpan? keepTime = null, DateTime? keepDate = null)
    {
        var taken = await _appointments.GetActiveTimesAsync(ClinicId, vetId, date, excludeAppointmentId);
        var now = DateTime.Now;
        return TimeSlotHelper.AllSlots().Select(slot =>
        {
            // When editing, the visit's own time stays selectable even if it has started.
            if (keepTime == slot && keepDate?.Date == date.Date) return (slot, SlotState.Available);
            if (TimeSlotHelper.IsPast(date, slot, now)) return (slot, SlotState.Past);
            return (slot, TimeSlotHelper.IsTaken(slot, taken) ? SlotState.Taken : SlotState.Available);
        }).ToList();
    }

    public async Task<OperationResult> BookAppointmentAsync(Appointment a)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can book appointments.");
        var problem = await CheckPetAndVetAsync(a);                                       // rules 6, 7
        if (problem != null) return problem;
        if (a.StartsAt < DateTime.Now) return TimeError("This time has already passed. Pick a later time.");   // rule 5

        a.ClinicId = ClinicId;
        a.CreatedBy = _session.UserId;
        a.Status = AppointmentStatuses.Scheduled;
        a.SyncStatus = SyncStatuses.Synced;
        var (result, _) = await _appointments.InsertAsync(a, exactTimeOnly: false);        // rule 4
        return result == SlotWriteResult.SlotTaken ? SlotTaken() : OperationResult.Success("Appointment booked.");
    }

    /// <summary>
    /// Section 4.C: books for now and checks in right away. A new client (<paramref name="newOwner"/> and/or
    /// <paramref name="newPet"/>) is registered in the same transaction, because they are already at the clinic.
    /// </summary>
    public async Task<OperationResult> BookWalkInAsync(Appointment a, Owner newOwner = null, Pet newPet = null)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can book walk-ins.");
        var now = DateTime.Now;
        a.AppointmentDate = now.Date;
        a.AppointmentTime = TimeSlotHelper.NowToTheMinute(now);
        var problem = newPet != null ? await CheckVetAsync(a) : await CheckPetAndVetAsync(a);
        if (problem != null) return problem;

        a.ClinicId = ClinicId;
        a.CreatedBy = _session.UserId;
        a.Status = AppointmentStatuses.CheckedIn;
        a.CheckedInAt = now;
        a.SyncStatus = SyncStatuses.Synced;
        if (newOwner != null) newOwner.ClinicId = ClinicId;
        var (result, _) = newPet != null
            ? await _appointments.RegisterWalkInAsync(newOwner, newPet, a)
            : await _appointments.InsertAsync(a, exactTimeOnly: true);
        return result == SlotWriteResult.SlotTaken
            ? OperationResult.Invalid(new Dictionary<string, string> { [AppointmentFormData.VetField] = "This vet was just booked at this minute. Pick another vet." })
            : OperationResult.Success(newPet != null ? newPet.PetName + " is checked in. Owner and pet saved to records." : "Walk-in checked in.");
    }

    /// <summary>A registered owner of the clinic with this contact no. (booking suggestion and guest check-in), or null.</summary>
    public async Task<Owner> FindOwnerByContactAsync(string contactNo)
    {
        string digits = Validator.Digits(contactNo);
        return Validator.IsPhMobile(digits) ? await _owners.FindByContactAsync(ClinicId, digits, null) : null;
    }

    /// <summary>The owner's active pets (guest check-in step 2).</summary>
    public Task<IReadOnlyList<Pet>> GetOwnerPetsAsync(int ownerId) => _pets.GetByOwnerAsync(ClinicId, ownerId);

    /// <summary>
    /// Section 4.C2 and rule 8: registers (or matches) the guest's owner and pet, links the pet to the appointment,
    /// clears the guest fields and checks in — one transaction. Pass an existing owner / pet id, or the form values.
    /// </summary>
    public async Task<OperationResult> CheckInGuestAsync(Appointment a, int existingOwnerId, OwnerFormData newOwner, int existingPetId, PetFormData newPet)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can check pets in.");
        if (!a.IsGuest) return OperationResult.Fail("This appointment isn’t a guest booking.");
        if (a.AppointmentDate.Date != DateTime.Today)
            return OperationResult.Fail("Check-in is only possible on the appointment day (" + a.AppointmentDate.ToString("MMM d") + ").");

        var errors = new Dictionary<string, string>();
        Owner owner = null;
        if (existingOwnerId <= 0)
        {
            if (newOwner == null) return OperationResult.Fail("Choose the owner first.");
            foreach (var (key, message) in OwnerController.Validate(newOwner)) errors[AppointmentFormData.NewOwnerPrefix + key] = message;
            if (errors.Count == 0) { owner = OwnerController.OwnerFrom(newOwner); owner.ClinicId = ClinicId; }
        }
        Pet pet = null;
        if (existingPetId <= 0)
        {
            if (newPet == null) return OperationResult.Fail("Choose the pet first.");
            newPet.OwnerId = 1; // the owner is checked above (or created in the same transaction)
            foreach (var (key, message) in PetController.Validate(newPet, DateTime.Today, out pet)) errors[AppointmentFormData.NewPetPrefix + key] = message;
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var result = await _appointments.CheckInGuestAsync(ClinicId, a.AppointmentId, DateTime.Today, existingOwnerId, owner, existingPetId, pet);
        return result switch
        {
            AppointmentRepository.GuestCheckInResult.CheckedIn =>
                OperationResult.Success((pet?.PetName ?? a.PetName) + " is checked in. Owner and pet saved to records."),
            AppointmentRepository.GuestCheckInResult.InvalidOwnerOrPet => OperationResult.Fail("That owner or pet was archived or isn’t in your clinic. Pick again."),
            _ => OperationResult.Fail("This appointment changed in the meantime. Refresh and try again.")
        };
    }

    /// <summary>Rule 15 (runs on sign-in): clears old guest names and contact numbers. Returns the rows cleared.</summary>
    public async Task<int> ClearOldGuestInfoAsync() =>
        _session.ClinicId == null ? 0 : await _appointments.ClearOldGuestInfoAsync(ClinicId);

    public async Task<OperationResult> UpdateAppointmentAsync(Appointment a)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can edit appointments.");
        var existing = await GetAppointmentByIdAsync(a.AppointmentId);
        if (existing == null) return OperationResult.Fail("This appointment no longer exists.");
        if (existing.Status is not (AppointmentStatuses.Scheduled or AppointmentStatuses.CheckedIn))      // rule 11
            return OperationResult.Fail("A " + existing.Status.ToLowerInvariant() + " appointment can’t be edited.");

        var problem = await CheckPetAndVetAsync(a);
        if (problem != null) return problem;
        bool moved = a.AppointmentDate.Date != existing.AppointmentDate.Date || a.AppointmentTime != existing.AppointmentTime;
        if (moved && a.StartsAt < DateTime.Now) return TimeError("This time has already passed. Pick a later time.");

        a.ClinicId = ClinicId;
        return await _appointments.UpdateAsync(a) switch
        {
            SlotWriteResult.Saved => OperationResult.Success("Appointment updated."),
            SlotWriteResult.SlotTaken => SlotTaken(),
            _ => OperationResult.Fail("This appointment can no longer be edited.")
        };
    }

    /// <summary>Rule 12: same appointment, new date and time.</summary>
    public async Task<OperationResult> RescheduleAsync(int appointmentId, DateTime newDate, TimeSpan newTime)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can reschedule appointments.");
        var existing = await GetAppointmentByIdAsync(appointmentId);
        if (existing == null) return OperationResult.Fail("This appointment no longer exists.");
        if (existing.Status != AppointmentStatuses.Scheduled) return OperationResult.Fail("Only scheduled appointments can be rescheduled.");
        if (newDate.Date + newTime < DateTime.Now) return TimeError("This time has already passed. Pick a later time.");

        existing.AppointmentDate = newDate.Date;
        existing.AppointmentTime = newTime;
        return await _appointments.UpdateAsync(existing) switch
        {
            SlotWriteResult.Saved => OperationResult.Success("Appointment moved to " + newDate.ToString("MMM d") + ", " + TimeSlotHelper.Format(newTime) + "."),
            SlotWriteResult.SlotTaken => SlotTaken(),
            _ => OperationResult.Fail("This appointment can no longer be rescheduled.")
        };
    }

    public async Task<OperationResult> CheckInAsync(int appointmentId)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can check pets in.");
        var a = await GetAppointmentByIdAsync(appointmentId);
        if (a == null) return OperationResult.Fail("This appointment no longer exists.");
        if (a.IsGuest) return OperationResult.Fail("Register or match the owner and pet first.");          // rule 8
        if (a.AppointmentDate.Date != DateTime.Today)                                     // rule 8
            return OperationResult.Fail("Check-in is only possible on the appointment day (" + a.AppointmentDate.ToString("MMM d") + ").");
        return await ChangeStatusAsync(a, new[] { AppointmentStatuses.Scheduled }, AppointmentStatuses.CheckedIn, a.PetName + " is checked in.");
    }

    public async Task<OperationResult> StartConsultationAsync(int appointmentId)
    {
        var a = await GetAppointmentByIdAsync(appointmentId);
        if (a == null) return OperationResult.Fail("This appointment no longer exists.");
        if (!IsAssignedVet(a)) return OperationResult.Fail("Only " + a.VetName + " can start this consultation.");
        return await ChangeStatusAsync(a, new[] { AppointmentStatuses.CheckedIn }, AppointmentStatuses.InConsultation, "Consultation started.");
    }

    public async Task<OperationResult> CompleteAsync(int appointmentId)
    {
        var a = await GetAppointmentByIdAsync(appointmentId);
        if (a == null) return OperationResult.Fail("This appointment no longer exists.");
        if (!IsAssignedVet(a)) return OperationResult.Fail("Only " + a.VetName + " can complete this visit.");
        if (!await _appointments.HasMedicalRecordAsync(appointmentId))                    // rule 10
            return OperationResult.Fail(NeedsRecordMessage);
        return await ChangeStatusAsync(a, new[] { AppointmentStatuses.InConsultation }, AppointmentStatuses.Completed, "Visit completed.");
    }

    public async Task<OperationResult> CancelAsync(int appointmentId, string reason)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can cancel appointments.");
        var a = await GetAppointmentByIdAsync(appointmentId);
        if (a == null) return OperationResult.Fail("This appointment no longer exists.");
        string note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note?.Length > 255) return OperationResult.Invalid(new Dictionary<string, string> { [CancelReasonField] = "Use 255 characters or fewer." });
        return await ChangeStatusAsync(a, new[] { AppointmentStatuses.Scheduled, AppointmentStatuses.CheckedIn },     // rule 13
            AppointmentStatuses.Cancelled, "Appointment cancelled.", note);
    }

    public async Task<OperationResult> MarkNoShowAsync(int appointmentId)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can mark a no-show.");
        var a = await GetAppointmentByIdAsync(appointmentId);
        if (a == null) return OperationResult.Fail("This appointment no longer exists.");
        if (a.StartsAt > DateTime.Now)                                                    // rule 9
            return OperationResult.Fail("A no-show can only be marked after " + a.TimeText + " on " + a.AppointmentDate.ToString("MMM d") + ".");
        return await ChangeStatusAsync(a, new[] { AppointmentStatuses.Scheduled }, AppointmentStatuses.NoShow, "Marked as no-show.");
    }

    private const string NeedsRecordMessage = "Add a medical record before completing this visit.";

    private async Task<OperationResult> ChangeStatusAsync(Appointment a, IReadOnlyCollection<string> from, string to, string success, string cancelReason = null)
    {
        bool changed = await _appointments.SetStatusAsync(ClinicId, a.AppointmentId, from, to, cancelReason);
        return changed ? OperationResult.Success(success)
            : OperationResult.Fail("This appointment changed in the meantime (it is now " + a.Status + "). Refresh and try again.");
    }

    /// <summary>Rules 6 and 7.</summary>
    private async Task<OperationResult> CheckPetAndVetAsync(Appointment a)
    {
        // A guest booking has no pet yet (rule 3 checks the guest fields instead).
        if (a.PetId is int petId && !await _appointments.PetBookableAsync(ClinicId, petId))
            return OperationResult.Invalid(new Dictionary<string, string> { [AppointmentFormData.PetField] = "This pet was archived or isn’t in your clinic." });
        return await CheckVetAsync(a);
    }

    /// <summary>Rule 7.</summary>
    private async Task<OperationResult> CheckVetAsync(Appointment a)
    {
        var vets = await _users.GetActiveVetsAsync(ClinicId);
        if (vets.All(v => v.UserId != a.VetId))
            return OperationResult.Invalid(new Dictionary<string, string> { [AppointmentFormData.VetField] = "Choose an active vet of your clinic." });
        return null;
    }

    private static OperationResult SlotTaken() =>
        TimeError("This slot was just taken — pick another time");

    private static OperationResult TimeError(string message) =>
        OperationResult.Invalid(new Dictionary<string, string> { [AppointmentFormData.TimeField] = message });

    /// <summary>
    /// Walk-ins: the vet who is free in the current half hour and not in a consultation, with the
    /// fewest pets waiting. Falls back to the vet with the fewest waiting.
    /// </summary>
    public async Task<User> GetNextAvailableVetAsync(IReadOnlyList<User> vets)
    {
        if (vets.Count == 0) return null;
        var now = DateTime.Now;
        var today = await GetAppointmentsAsync(now.Date, null, null, null);
        var slot = TimeSlotHelper.SlotOf(now.TimeOfDay);

        int Waiting(User v) => today.Count(a => a.VetId == v.UserId && a.Status == AppointmentStatuses.CheckedIn);
        bool Busy(User v) => today.Any(a => a.VetId == v.UserId &&
            (a.Status == AppointmentStatuses.InConsultation ||
             (a.Status == AppointmentStatuses.Scheduled && a.AppointmentTime >= slot && a.AppointmentTime < slot + TimeSlotHelper.SlotLength)));

        return vets.Where(v => !Busy(v)).OrderBy(Waiting).FirstOrDefault() ?? vets.OrderBy(Waiting).First();
    }

    // Form validation (rules 3 and 5) ---------------------------------------------------------------

    /// <summary>Checks the form and builds the appointment when valid.</summary>
    public static Dictionary<string, string> Validate(AppointmentFormData data, out Appointment appointment)
    {
        var errors = new Dictionary<string, string>();
        appointment = null;
        bool reschedule = data.Mode == AppointmentFormMode.Reschedule;

        bool walkIn = data.Mode == AppointmentFormMode.WalkIn;
        if (!reschedule)
        {
            if (data.IsGuest && walkIn)
            {
                // Walk-in, new client: the full Add owner and Add pet forms (registered right away).
                foreach (var (key, message) in OwnerController.Validate(data.NewOwner ?? new OwnerFormData()))
                    errors[AppointmentFormData.NewOwnerPrefix + key] = message;
                var petData = data.NewPet ?? new PetFormData();
                petData.OwnerId = 1; // the owner is registered in the same transaction
                foreach (var (key, message) in PetController.Validate(petData, DateTime.Today, out _))
                    errors[AppointmentFormData.NewPetPrefix + key] = message;
            }
            else if (data.IsGuest)
            {
                // Guest booking: only these four are kept, on the appointment (rule 3).
                string owner = data.GuestOwnerName?.Trim(), pet = data.GuestPetName?.Trim();
                if (string.IsNullOrEmpty(owner)) errors[AppointmentFormData.GuestOwnerField] = "Required";
                else if (owner.Length > 100) errors[AppointmentFormData.GuestOwnerField] = "Use 100 characters or fewer.";
                if (string.IsNullOrWhiteSpace(data.GuestContactNo)) errors[AppointmentFormData.GuestContactField] = "Required";
                else if (!Validator.IsPhMobile(data.GuestContactNo))
                    errors[AppointmentFormData.GuestContactField] = "Enter a valid 11-digit mobile number starting with 09";
                if (string.IsNullOrEmpty(pet)) errors[AppointmentFormData.GuestPetField] = "Required";
                else if (pet.Length > 50) errors[AppointmentFormData.GuestPetField] = "Use 50 characters or fewer.";
                if (!PetSpecies.All.Contains(data.GuestSpecies)) errors[AppointmentFormData.GuestSpeciesField] = "Required";
            }
            else
            {
                if (data.OwnerId <= 0) errors[AppointmentFormData.OwnerField] = "Required";
                if (data.PetId <= 0) errors[AppointmentFormData.PetField] = "Required";
            }
            if (data.VetId == 0) errors[AppointmentFormData.VetField] = "Required";
        }
        if (data.Mode != AppointmentFormMode.WalkIn)
        {
            if (data.Date == null) errors[AppointmentFormData.DateField] = "Required";
            if (data.Time == null) errors[AppointmentFormData.TimeField] = "Required";
        }

        string reason = null;
        if (!reschedule)
        {
            reason = data.ComposeReason();
            if (string.IsNullOrWhiteSpace(reason))
                errors[AppointmentFormData.ReasonField] = data.ReasonChip == "Other" ? "Describe the reason" : "Required";
            else if (reason.Length > AppointmentFormData.ReasonMaxLength)
                errors[AppointmentFormData.ReasonField] = "Use 100 characters or fewer.";
        }

        if (errors.Count > 0) return errors;

        bool guestBooking = data.IsGuest && !walkIn && !reschedule;
        appointment = new Appointment
        {
            AppointmentId = data.AppointmentId,
            PetId = data.IsGuest && !reschedule ? null : data.PetId,
            GuestOwnerName = guestBooking ? data.GuestOwnerName.Trim() : null,
            GuestContactNo = guestBooking ? Validator.Digits(data.GuestContactNo) : null,
            GuestPetName = guestBooking ? data.GuestPetName.Trim() : null,
            GuestSpecies = guestBooking ? data.GuestSpecies : null,
            VetId = data.VetId,
            AppointmentDate = data.Date?.Date ?? DateTime.Today,
            AppointmentTime = data.Time ?? TimeSpan.Zero,
            Reason = reason
        };
        return errors;
    }

    // View -------------------------------------------------------------------------------------------

    /// <param name="openAppointmentId">Open this appointment's details straight away.</param>
    public View CreateView(int? openAppointmentId = null)
    {
        if (!CanView)
            return new Label
            {
                Text = "Appointments are available to Cashier/Staff, Veterinarians and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync(e.Values);

        if (IsVet)
        {
            // Section 4.E: the vet sees only their own schedule.
            _schedule = new VetSchedulePage();
            _schedule.DateChanged += async (s, d) => await RefreshAsync();
            _schedule.AppointmentSelected += async (s, a) => await ShowDetailsAsync(a.AppointmentId);
            _schedule.ActionRequested += async (s, e) => await RunActionAsync(e.Appointment, e.Action);
            _host.ShowPage(_schedule);
            _schedule.ShowLoading();
        }
        else
        {
            _page = new AppointmentsPage();
            _page.SetCanEdit(CanManage);
            _page.BookRequested += async (s, e) => await OpenFormAsync(_host, new AppointmentFormData { Mode = AppointmentFormMode.Book }, null, AfterSaveAsync);
            _page.WalkInRequested += async (s, e) => await OpenWalkInAsync();
            _page.DateChanged += async (s, d) => await RefreshAsync();
            _page.FiltersChanged += async (s, e) => { _listPage = 1; await RefreshAsync(); };
            _page.ViewChanged += async (s, e) => { _listPage = 1; await RefreshAsync(); };
            _page.PageRequested += async (s, p) => { _listPage = p; await RefreshAsync(); };
            _page.AppointmentSelected += async (s, a) => await ShowDetailsAsync(a.AppointmentId);
            _page.ActionRequested += async (s, e) => await RunActionAsync(e.Appointment, e.Action);
            _page.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Appointment));
            _page.SlotRequested += async (s, e) =>
            {
                if (!CanManage) return;
                await OpenFormAsync(_host, new AppointmentFormData
                {
                    Mode = AppointmentFormMode.Book,
                    VetId = e.VetId,
                    Date = e.Date,
                    Time = e.Time
                }, null, AfterSaveAsync);
            };
            _host.ShowPage(_page);
            _page.ShowLoading();
        }

        _ = LoadAsync(openAppointmentId);
        _toast = new Toast();
        return new Grid { Children = { _host, _toast } };
    }

    private Toast _toast;

    // Guest check-in (section 4.C2) ------------------------------------------------------------------------

    private async Task OpenGuestCheckInAsync(Appointment a)
    {
        if (!CanManage) return;
        if (a.AppointmentDate.Date != DateTime.Today)                                     // rule 8
        {
            await ShowInfoAsync("Can’t check in yet", "Check-in is only possible on the appointment day (" + a.AppointmentDate.ToString("MMM d") + ").", "circle-alert");
            return;
        }

        var match = await FindOwnerByContactAsync(a.GuestContactNo);
        var view = new GuestCheckInView(a, match);
        view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        view.NextRequested += async (s, e) =>
        {
            if (!view.UseMatch)
            {
                var errors = OwnerController.Validate(view.NewOwner);                      // owner rules 3 and 4
                if (errors.Count > 0)
                {
                    view.ShowResult(OperationResult.Invalid(errors.ToDictionary(x => AppointmentFormData.NewOwnerPrefix + x.Key, x => x.Value)));
                    return;
                }
                view.ShowPetStep(Array.Empty<Pet>());
                return;
            }
            view.SetBusy(true);
            try { view.ShowPetStep(await GetOwnerPetsAsync(view.ExistingOwnerId)); }
            catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { view.SetBusy(false); }
        };
        view.CheckInRequested += async (s, e) =>
        {
            view.SetBusy(true);
            try
            {
                var result = await CheckInGuestAsync(a, view.ExistingOwnerId, view.NewOwner, view.ExistingPetId, view.NewPet);
                if (!result.Ok) { view.ShowResult(result); return; }
                await _host.Panel.HideAsync();
                _toast?.Show(result.Message);
                await RefreshAsync();
            }
            catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { view.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(view, GuestCheckInView.PanelWidth);
    }

    private async Task LoadAsync(int? openAppointmentId)
    {
        await RefreshAsync();
        if (openAppointmentId != null) await ShowDetailsAsync(openAppointmentId.Value);
    }

    /// <summary>Reloads whatever the current page shows. Older, slower loads are dropped.</summary>
    private async Task RefreshAsync()
    {
        int mine = ++_refreshNumber;
        try
        {
            if (_schedule != null)
            {
                var date = _schedule.SelectedDate;
                var summary = await GetTodaySummaryAsync(date, _session.UserId);
                var mineToday = await GetVetScheduleAsync(_session.UserId, date);
                if (mine != _refreshNumber) return;
                _schedule.ShowSummary(summary, date == DateTime.Today);
                _schedule.ShowAppointments(mineToday, PrimaryActionFor);
                return;
            }

            if (_vets.Count == 0)
            {
                _vets = await _users.GetActiveVetsAsync(ClinicId);
                if (mine != _refreshNumber) return;
                _page.SetVets(_vets); // may raise FiltersChanged → a newer refresh takes over
                if (mine != _refreshNumber) return;
            }

            var day = _page.SelectedDate;
            var daySummary = await GetTodaySummaryAsync(day);
            switch (_page.CurrentView)
            {
                case AppointmentsView.Queue:
                {
                    var list = await GetAppointmentsAsync(day, _page.CurrentVetId, _page.CurrentStatus, _page.CurrentSearch);
                    if (mine != _refreshNumber) return;
                    _page.ShowSummary(daySummary, day == DateTime.Today);
                    _page.ShowQueue(list, PrimaryActionFor);
                    break;
                }
                case AppointmentsView.Calendar when _page.WeekMode:
                {
                    var start = _page.WeekStart;
                    var list = await _appointments.GetRangeAsync(new AppointmentQuery
                    {
                        ClinicId = ClinicId, From = start, To = start.AddDays(6),
                        VetId = _page.CurrentVetId, Status = _page.CurrentStatus, Search = _page.CurrentSearch
                    });
                    if (mine != _refreshNumber) return;
                    _page.ShowSummary(daySummary, day == DateTime.Today);
                    _page.ShowCalendarWeek(list, start, CanManage);
                    break;
                }
                case AppointmentsView.Calendar:
                {
                    var list = await GetAppointmentsAsync(day, _page.CurrentVetId, _page.CurrentStatus, _page.CurrentSearch);
                    if (mine != _refreshNumber) return;
                    var vets = _page.CurrentVetId is int vetId ? _vets.Where(v => v.UserId == vetId).ToList() : _vets;
                    _page.ShowSummary(daySummary, day == DateTime.Today);
                    _page.ShowCalendarDay(vets, list, day, CanManage);
                    break;
                }
                default:
                {
                    var page = await _appointments.SearchAsync(new AppointmentQuery
                    {
                        ClinicId = ClinicId, VetId = _page.CurrentVetId, Status = _page.CurrentStatus,
                        Search = _page.CurrentSearch, Page = _listPage, PageSize = 10
                    });
                    if (page.Items.Count == 0 && _listPage > 1)
                    {
                        _listPage--;
                        page = await _appointments.SearchAsync(new AppointmentQuery
                        {
                            ClinicId = ClinicId, VetId = _page.CurrentVetId, Status = _page.CurrentStatus,
                            Search = _page.CurrentSearch, Page = _listPage, PageSize = 10
                        });
                    }
                    if (mine != _refreshNumber) return;
                    _page.ShowSummary(daySummary, day == DateTime.Today);
                    _page.ShowList(page);
                    break;
                }
            }
        }
        catch (SqlException)
        {
            if (mine != _refreshNumber) return;
            if (_schedule != null) _schedule.ShowError(DatabaseError);
            else _page.ShowError(DatabaseError);
        }
    }

    // Actions by status and role ------------------------------------------------------------------------

    /// <summary>The one button on a queue / schedule card (null when this role has nothing to do there).</summary>
    private AppointmentAction PrimaryActionFor(Appointment a) => a.Status switch
    {
        AppointmentStatuses.Scheduled when CanManage && a.AppointmentDate.Date == DateTime.Today => new("checkin", "Check in", "NavyOutlineButton"),
        AppointmentStatuses.CheckedIn when IsAssignedVet(a) => new("start", "Start consultation", "WorkspaceButton"),
        AppointmentStatuses.InConsultation when IsAssignedVet(a) => new("complete", "Complete", "MintButton"),
        AppointmentStatuses.Completed when CanManage => new("bill", "Create bill", "Link"),
        _ => null
    };

    /// <summary>Footer buttons of the details panel. Buttons the role can't use are left out.</summary>
    private IReadOnlyList<AppointmentAction> DetailActionsFor(Appointment a)
    {
        var actions = new List<AppointmentAction>();
        switch (a.Status)
        {
            case AppointmentStatuses.Scheduled when CanManage:
                actions.Add(new("edit", "Edit", "OutlineButton"));
                actions.Add(new("reschedule", "Reschedule", "OutlineButton"));
                if (a.StartsAt <= DateTime.Now) actions.Add(new("noshow", "Mark no-show", "OutlineButton"));
                actions.Add(new("cancel", "Cancel", "DangerOutlineButton"));
                if (a.AppointmentDate.Date == DateTime.Today) actions.Add(new("checkin", "Check in", "WorkspaceButton"));
                break;
            case AppointmentStatuses.CheckedIn:
                if (CanManage)
                {
                    actions.Add(new("edit", "Edit", "OutlineButton"));
                    actions.Add(new("cancel", "Cancel", "DangerOutlineButton"));
                }
                if (IsAssignedVet(a)) actions.Add(new("start", "Start consultation", "WorkspaceButton"));
                break;
            case AppointmentStatuses.InConsultation when IsAssignedVet(a):
                if (OpenConsultation != null) actions.Add(new("consult", "Open consultation", "OutlineButton"));
                actions.Add(new("complete", "Complete", "MintButton"));
                break;
            case AppointmentStatuses.Completed when CanManage:
                actions.Add(new("bill", "Create bill", "WorkspaceButton"));
                break;
        }
        return actions;
    }

    /// <summary>List row menu: View details, Check in, Reschedule, Cancel (red), Mark no-show.</summary>
    private IReadOnlyList<MenuEntry> MenuFor(Appointment a)
    {
        var entries = new List<MenuEntry> { new("View details", "eye", () => _ = ShowDetailsAsync(a.AppointmentId)) };
        if (!CanManage || a.Status != AppointmentStatuses.Scheduled && a.Status != AppointmentStatuses.CheckedIn) return entries;

        if (a.Status == AppointmentStatuses.Scheduled)
        {
            if (a.AppointmentDate.Date == DateTime.Today) entries.Add(new MenuEntry("Check in", "circle-check", () => _ = RunActionAsync(a, "checkin")));
            entries.Add(new MenuEntry("Reschedule", "calendar", () => _ = RunActionAsync(a, "reschedule")));
            if (a.StartsAt <= DateTime.Now) entries.Add(new MenuEntry("Mark no-show", "user-x", () => _ = RunActionAsync(a, "noshow")));
        }
        entries.Add(new MenuEntry("Cancel", "x", () => _ = RunActionAsync(a, "cancel"), Danger: true));
        return entries;
    }

    private async Task RunActionAsync(Appointment a, string action)
    {
        try
        {
            switch (action)
            {
                case "checkin" when a.IsGuest:
                    await OpenGuestCheckInAsync(a);
                    break;
                case "checkin":
                    await ApplyAsync(a.AppointmentId, () => CheckInAsync(a.AppointmentId));
                    break;
                case "start":
                    var started = await StartConsultationAsync(a.AppointmentId);
                    if (started.Ok && OpenConsultation != null)
                    {
                        // Section 4.B: "Start consultation" opens the consultation page.
                        OpenConsultation(a.AppointmentId);
                        break;
                    }
                    await AfterStatusChangeAsync(a.AppointmentId, started);
                    break;
                case "consult":
                    OpenConsultation?.Invoke(a.AppointmentId);
                    break;
                case "complete":
                    await CompleteWithCheckAsync(a);
                    break;
                case "edit":
                    await OpenEditAsync(a);
                    break;
                case "reschedule":
                    await OpenRescheduleAsync(a);
                    break;
                case "cancel":
                    await ConfirmCancelAsync(a);
                    break;
                case "noshow":
                    await ConfirmNoShowAsync(a);
                    break;
                case "bill":
                    if (OpenCreateBill != null) OpenCreateBill(a.AppointmentId);
                    else await ShowInfoAsync("Create bill", "Open Bills & payments to create the bill for this visit.", "circle-dollar-sign");
                    break;
            }
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Something went wrong", DatabaseError, "circle-alert");
        }
    }

    /// <summary>Runs a status change, then refreshes the page and the open details panel (or shows why not).</summary>
    private async Task ApplyAsync(int appointmentId, Func<Task<OperationResult>> change)
    {
        var result = await change();
        await AfterStatusChangeAsync(appointmentId, result);
    }

    private async Task AfterStatusChangeAsync(int appointmentId, OperationResult result)
    {
        if (!result.Ok)
        {
            if (_host.Panel.IsOpen && _detailsId == appointmentId) _details?.ShowMessage(result.Message);
            else await ShowInfoAsync("Can’t do that", result.Message, "circle-alert");
            return;
        }

        await RefreshAsync();
        if (_host.Panel.IsOpen && _detailsId == appointmentId) await ShowDetailsAsync(appointmentId);
    }

    /// <summary>Rule 10: without a medical record, explain and offer to open Medical records.</summary>
    private async Task CompleteWithCheckAsync(Appointment a)
    {
        if (!await _appointments.HasMedicalRecordAsync(a.AppointmentId))
        {
            if (OpenConsultation == null)
            {
                await ShowInfoAsync("Can’t complete yet", NeedsRecordMessage, "clipboard-plus");
                return;
            }

            _pendingConfirm = async _ =>
            {
                await CloseDialogAsync();
                OpenConsultation(a.AppointmentId);
            };
            await _host.Dialog.ShowConfirmAsync("Can’t complete yet", NeedsRecordMessage, "Open medical record",
                danger: false, icon: "clipboard-plus");
            return;
        }

        await ApplyAsync(a.AppointmentId, () => CompleteAsync(a.AppointmentId));
    }

    // Details panel --------------------------------------------------------------------------------------

    private AppointmentDetailsView _details;
    private int _detailsId;

    private async Task ShowDetailsAsync(int appointmentId)
    {
        try
        {
            var a = await GetAppointmentByIdAsync(appointmentId);
            if (a == null)
            {
                await ShowInfoAsync("Appointment not found", "This appointment no longer exists.", "circle-alert");
                return;
            }
            // A vet only sees their own visits (section 4.E).
            if (IsVet && a.VetId != _session.UserId)
            {
                await ShowInfoAsync("Not your appointment", "This visit is booked with " + a.VetName + ".", "circle-alert");
                return;
            }

            var view = new AppointmentDetailsView();
            view.CloseRequested += async (s, e) => await _host.Panel.HideAsync();
            view.ActionRequested += async (s, action) => await RunActionAsync(a, action);
            view.PetRequested += (s, petId) => OpenPet?.Invoke(petId);
            view.MedicalRecordRequested += (s, recordId) => OpenRecord?.Invoke(recordId);
            view.Show(a, DetailActionsFor(a), OpenPet != null);

            _details = view;
            _detailsId = appointmentId;
            await _host.Panel.ShowAsync(view, AppointmentDetailsView.PanelWidth);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t open this appointment", DatabaseError, "circle-alert");
        }
    }

    // Forms ------------------------------------------------------------------------------------------------

    /// <summary>After a save inside the module: refresh, and show the details of what was saved.</summary>
    private async Task AfterSaveAsync(Appointment a)
    {
        await RefreshAsync();
        if (a.AppointmentId > 0) await ShowDetailsAsync(a.AppointmentId);
    }

    /// <summary>
    /// Opens "Book appointment" from another module (pet profile, owner profile, pet form), with the
    /// owner and pet already filled in.
    /// </summary>
    /// <param name="date">A date to fill in (e.g. a follow-up), or null.</param>
    /// <param name="vetId">A vet to fill in, or 0.</param>
    public Task OpenBookFormAsync(ModuleHostView host, Owner owner, int petId, Func<Appointment, Task> afterSave,
        DateTime? date = null, int vetId = 0, string reasonChip = null, string reasonDetails = null) =>
        OpenFormAsync(host, new AppointmentFormData
        {
            Mode = AppointmentFormMode.Book,
            OwnerId = owner?.OwnerId ?? 0,
            PetId = petId,
            Date = date,
            VetId = vetId,
            ReasonChip = reasonChip,
            ReasonDetails = reasonDetails
        }, owner, afterSave);

    private Task OpenWalkInAsync() => OpenWalkInAsync(_host, AfterSaveAsync);

    /// <summary>Opens the Walk-in panel in <paramref name="host"/>'s side panel (Appointments page or the staff dashboard).</summary>
    public Task OpenWalkInAsync(ModuleHostView host, Func<Appointment, Task> afterSave)
    {
        var now = DateTime.Now;
        return OpenFormAsync(host, new AppointmentFormData
        {
            Mode = AppointmentFormMode.WalkIn,
            Date = now.Date,
            Time = TimeSlotHelper.NowToTheMinute(now)
        }, null, afterSave);
    }

    private Task OpenEditAsync(Appointment a)
    {
        var (chip, details) = AppointmentFormData.SplitReason(a.Reason);
        return OpenFormAsync(_host, new AppointmentFormData
        {
            Mode = AppointmentFormMode.Edit,
            AppointmentId = a.AppointmentId,
            OwnerId = a.OwnerId,
            PetId = a.PetId ?? 0,
            IsGuest = a.IsGuest,
            GuestOwnerName = a.GuestOwnerName,
            GuestContactNo = a.GuestContactNo,
            GuestPetName = a.GuestPetName,
            GuestSpecies = a.GuestSpecies,
            VetId = a.VetId,
            Date = a.AppointmentDate,
            Time = a.AppointmentTime,
            ReasonChip = chip,
            ReasonDetails = details
        }, a.IsGuest ? null : OwnerOf(a), AfterSaveAsync, timeLocked: a.Status == AppointmentStatuses.CheckedIn);
    }

    private Task OpenRescheduleAsync(Appointment a) =>
        OpenFormAsync(_host, new AppointmentFormData
        {
            Mode = AppointmentFormMode.Reschedule,
            AppointmentId = a.AppointmentId,
            OwnerId = a.OwnerId,
            PetId = a.PetId ?? 0,
            IsGuest = a.IsGuest,
            VetId = a.VetId,
            VetName = a.VetName,
            Date = a.AppointmentDate.Date >= DateTime.Today ? a.AppointmentDate : DateTime.Today
        }, null, AfterSaveAsync);

    private static Owner OwnerOf(Appointment a) => new()
    {
        OwnerId = a.OwnerId,
        FirstName = a.OwnerFirstName,
        LastName = a.OwnerLastName,
        ContactNo = a.OwnerContactNo
    };

    /// <summary>Opens the appointment panel in <paramref name="host"/>'s side panel and wires it to the data.</summary>
    public async Task OpenFormAsync(ModuleHostView host, AppointmentFormData initial, Owner owner, Func<Appointment, Task> afterSave,
        bool timeLocked = false)
    {
        if (!CanManage) return;

        var form = new AppointmentFormView(initial, owner, timeLocked);
        int searchNumber = 0, slotNumber = 0;

        form.OwnerSearchRequested += async (s, text) =>
        {
            int mine = ++searchNumber;
            try
            {
                var result = await _owners.SearchAsync(new OwnerQuery { ClinicId = ClinicId, Search = text, Sort = OwnerSort.NameAToZ, PageSize = 6 });
                if (mine == searchNumber) form.ShowOwnerResults(result.Items);
            }
            catch (SqlException)
            {
                if (mine == searchNumber) form.ShowOwnerSearchError(DatabaseError);
            }
        };
        form.PetsRequested += async (s, ownerId) => await LoadPetsAsync(form, ownerId, initial.PetId);
        int lookupNumber = 0;
        form.ContactLookupRequested += async (s, contact) =>
        {
            int mine = ++lookupNumber;
            try
            {
                var match = await FindOwnerByContactAsync(contact);
                if (mine == lookupNumber) form.ShowContactMatch(match);
            }
            catch (SqlException) { /* the suggestion is optional */ }
        };
        form.SlotsRequested += async (s, e) =>
        {
            int mine = ++slotNumber;
            try
            {
                var slots = await GetAvailableSlotsAsync(e.VetId, e.Date,
                    initial.AppointmentId == 0 ? null : initial.AppointmentId,
                    initial.Mode == AppointmentFormMode.Edit ? initial.Time : null,
                    initial.Mode == AppointmentFormMode.Edit ? initial.Date : null);
                if (mine == slotNumber) form.ShowSlots(slots);
            }
            catch (SqlException)
            {
                if (mine == slotNumber) form.ClearSlots(DatabaseError);
            }
        };
        form.CancelRequested += async (s, e) => await host.Panel.HideAsync();
        form.SaveRequested += async (s, e) => await SaveFormAsync(host, form, afterSave);

        await host.Panel.ShowAsync(form, AppointmentFormView.PanelWidth);

        // Fill what the panel needs: vets (with "Next available" for walk-ins), the owner's pets, the slots.
        try
        {
            if (initial.Mode != AppointmentFormMode.Reschedule)
            {
                var vets = await _users.GetActiveVetsAsync(ClinicId);
                var next = initial.Mode == AppointmentFormMode.WalkIn ? await GetNextAvailableVetAsync(vets) : null;
                form.ShowVets(vets, initial.VetId, next);
                if (form.InitialOwnerId != 0) await LoadPetsAsync(form, form.InitialOwnerId, initial.PetId);
            }
            else form.RefreshSlots();
        }
        catch (SqlException)
        {
            form.ShowResult(OperationResult.Fail(DatabaseError));
        }
    }

    private async Task LoadPetsAsync(AppointmentFormView form, int ownerId, int selectedPetId)
    {
        try
        {
            var pets = await _pets.GetByOwnerAsync(ClinicId, ownerId);                     // active pets only (rule 6)
            form.ShowPets(pets, selectedPetId);
        }
        catch (SqlException)
        {
            form.ShowResult(OperationResult.Fail(DatabaseError));
        }
    }

    /// <summary>Walk-in, new client: the owner and pet are built from the (already valid) form values.</summary>
    private Task<OperationResult> BookWalkInNewClientAsync(AppointmentFormData data, Appointment appointment)
    {
        var owner = OwnerController.OwnerFrom(data.NewOwner);
        data.NewPet.OwnerId = 1;
        PetController.Validate(data.NewPet, DateTime.Today, out var pet);
        return BookWalkInAsync(appointment, owner, pet);
    }

    private async Task SaveFormAsync(ModuleHostView host, AppointmentFormView form, Func<Appointment, Task> afterSave)
    {
        var data = form.Read();
        var errors = Validate(data, out var appointment);                              // rules 3 and 5
        if (errors.Count > 0)
        {
            form.ShowResult(OperationResult.Invalid(errors));
            return;
        }

        form.SetBusy(true);
        try
        {
            if (data.VetId == AppointmentFormData.NextAvailableVet)
                appointment.VetId = (await GetNextAvailableVetAsync(await _users.GetActiveVetsAsync(ClinicId)))?.UserId ?? 0;

            var result = data.Mode switch
            {
                AppointmentFormMode.WalkIn when data.IsGuest => await BookWalkInNewClientAsync(data, appointment),
                AppointmentFormMode.WalkIn => await BookWalkInAsync(appointment),
                AppointmentFormMode.Edit => await UpdateAppointmentAsync(appointment),
                AppointmentFormMode.Reschedule => await RescheduleAsync(data.AppointmentId, appointment.AppointmentDate, appointment.AppointmentTime),
                _ => await BookAppointmentAsync(appointment)
            };
            if (!result.Ok)
            {
                form.ShowResult(result);
                if (result.FieldErrors?.ContainsKey(AppointmentFormData.TimeField) == true) form.RefreshSlots(); // show the newly taken slot
                return;
            }

            if (data.Mode == AppointmentFormMode.Reschedule) appointment.AppointmentId = data.AppointmentId;
            await host.Panel.HideAsync();
            if (host == _host && data.Mode == AppointmentFormMode.WalkIn) _toast?.Show(result.Message);
            if (afterSave != null) await afterSave(appointment);
        }
        catch (SqlException)
        {
            form.ShowResult(OperationResult.Fail(DatabaseError));
        }
        finally
        {
            form.SetBusy(false);
        }
    }

    // Dialogs ----------------------------------------------------------------------------------------------

    private async Task ConfirmCancelAsync(Appointment a)
    {
        _pendingConfirm = async values =>
        {
            values.TryGetValue(CancelReasonField, out var reason);
            var result = await CancelAsync(a.AppointmentId, reason);
            if (!result.Ok)
            {
                _host.Dialog.ShowResult(result);
                return;
            }
            await CloseDialogAsync();
            await AfterStatusChangeAsync(a.AppointmentId, result);
        };

        await _host.Dialog.ShowConfirmWithNoteAsync(
            "Cancel " + a.PetName + "’s appointment on " + a.AppointmentDate.ToString("MMM d") + " at " + a.TimeText + "?",
            "The visit stays in the history as Cancelled.",
            "Cancel appointment", danger: true, icon: "calendar-days",
            CancelReasonField, "Reason for cancelling", "Optional", 255, cancelText: "Keep it");
    }

    private async Task ConfirmNoShowAsync(Appointment a)
    {
        _pendingConfirm = async _ =>
        {
            var result = await MarkNoShowAsync(a.AppointmentId);
            if (!result.Ok)
            {
                _host.Dialog.ShowResult(result);
                return;
            }
            await CloseDialogAsync();
            await AfterStatusChangeAsync(a.AppointmentId, result);
        };

        await _host.Dialog.ShowConfirmAsync("Mark this appointment as no-show?",
            a.PetName + " (" + a.OwnerName + ") didn’t arrive for " + a.TimeText + " on " + a.AppointmentDate.ToString("MMM d") + ".",
            "Mark no-show", danger: false, icon: "user-x");
    }

    /// <summary>A message with a single OK button.</summary>
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
