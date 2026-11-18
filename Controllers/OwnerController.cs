using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Controllers;

/// <summary>
/// Owner Records ("Owners"). Enforces Tasks/VetCare_Owner_Module.md, section 4:
///  1. only Cashier/Staff add, edit or archive; Administrators view only;
///  2. only owners of the signed-in user's clinic (Session.ClinicID);
///  3. first name, last name and contact number are required;
///  4. contact: PH mobile (11 digits, starts with 09); email, if given, must be valid;
///  5. same contact number already in the clinic → warning before saving;
///  6. owners are archived (IsDeleted = 1), never deleted;
///  7. no archiving with an unpaid bill or an upcoming appointment;
///  8. archived owners' pets are left out of the Pets list and new bookings (PetRepository);
///  9. new clients don't need to be registered to book: they are booked as guests and registered (owner and pet)
///     only when they check in (AppointmentController); no-shows never reach the Owner records.
/// Adding a pet from here uses the Pets module's form (PetController), with the owner filled in.
/// </summary>
public sealed class OwnerController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly OwnerRepository _owners;
    private readonly OwnerLinksRepository _links;
    private readonly PetController _petForms;

    private ModuleHostView _host;
    private OwnersPage _list;
    private int _page = 1;
    private Func<Task> _pendingConfirm;

    public OwnerController(UserSession session, OwnerRepository owners, OwnerLinksRepository links, PetController petForms)
    {
        _session = session;
        _owners = owners;
        _links = links;
        _petForms = petForms;
    }

    /// <summary>Opens a pet's profile in the Pets module (a pet card on the owner profile was clicked).</summary>
    public Action<int> OpenPet { get; set; }

    /// <summary>Opens a bill in Bills & payments (Billing history → "View receipt").</summary>
    public Action<int> OpenBill { get; set; }

    // Rules ---------------------------------------------------------------------------------

    /// <summary>Who may open the module: Cashier/Staff and Administrators of a clinic.</summary>
    public bool CanView => _session.ClinicId != null && (_session.RoleId == RoleIds.CashierStaff || _session.IsAdministrator);

    /// <summary>Rule 1: only Cashier/Staff change owner records.</summary>
    public bool CanEdit => _session.ClinicId != null && _session.RoleId == RoleIds.CashierStaff;

    /// <summary>Rule 2: always the signed-in user's clinic.</summary>
    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 6 of the module document) -----------------------------------------

    public Task<PagedResult<Owner>> GetOwnersAsync(string search, OwnerSort sortBy, int page = 1) =>
        _owners.SearchAsync(new OwnerQuery { ClinicId = ClinicId, Search = search, Sort = sortBy, Page = page, PageSize = 10 });

    public Task<Owner> GetOwnerByIdAsync(int ownerId) => _owners.GetByIdAsync(ClinicId, ownerId);

    /// <summary>Pet count, next appointment and unpaid balance.</summary>
    public Task<OwnerStats> GetOwnerSummaryAsync(int ownerId) => _links.GetStatsAsync(ClinicId, ownerId);

    /// <summary>Rule 5: an existing active owner of this clinic with the same contact number, or null.</summary>
    public Task<Owner> CheckDuplicateAsync(string contactNo, int? excludeOwnerId = null) =>
        _owners.FindByContactAsync(ClinicId, Validator.Digits(contactNo), excludeOwnerId);

    public async Task<OperationResult> AddOwnerAsync(Owner owner)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can add owners.");
        owner.ClinicId = ClinicId;
        owner.SyncStatus = SyncStatuses.Synced;
        await _owners.InsertAsync(owner);
        return OperationResult.Success(owner.FullName + " was added.");
    }

    public async Task<OperationResult> UpdateOwnerAsync(Owner owner)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can edit owners.");
        var existing = await GetOwnerByIdAsync(owner.OwnerId);
        if (existing == null) return OperationResult.Fail("This owner no longer exists.");

        owner.ClinicId = ClinicId;
        await _owners.UpdateAsync(owner);
        return OperationResult.Success(owner.FullName + " was updated.");
    }

    public async Task<OperationResult> ArchiveOwnerAsync(int ownerId)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can archive owners.");
        var existing = await GetOwnerByIdAsync(ownerId);
        if (existing == null) return OperationResult.Fail("This owner no longer exists.");

        var stats = await GetOwnerSummaryAsync(ownerId);                       // rule 7
        if (stats.HasUnpaidBalance || stats.HasUpcomingAppointment)
            return OperationResult.Fail("This owner has an unpaid bill or an upcoming appointment. Settle these first.");

        await _owners.ArchiveAsync(ClinicId, ownerId);                          // rule 6
        return OperationResult.Success(existing.FullName + " was archived.");
    }

    // View ----------------------------------------------------------------------------------------

    /// <param name="openOwnerId">Open this owner's profile straight away (e.g. "View owner" on a pet profile).</param>
    /// <summary>Dashboard shortcut: "add" opens the Add owner panel once the list is shown.</summary>
    public string StartIntent { get; set; }

    public View CreateView(int? openOwnerId = null)
    {
        if (!CanView)
            return new Label
            {
                Text = "Owner records are available to Cashier/Staff and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync();

        _list = new OwnersPage();
        _list.SetCanEdit(CanEdit);
        _list.AddRequested += async (s, e) => await OpenFormAsync(null);
        _list.FiltersChanged += async (s, e) => { _page = 1; await RefreshListAsync(); };
        _list.PageRequested += async (s, page) => { _page = page; await RefreshListAsync(); };
        _list.OwnerSelected += async (s, owner) => await ShowProfileAsync(owner.OwnerId);
        _list.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Owner));

        _host.ShowPage(_list);
        if (openOwnerId != null) _ = ShowProfileAsync(openOwnerId.Value);
        else _ = ShowListAsync();
        if (StartIntent == "add" && CanEdit) _host.Dispatcher.Dispatch(async () => { await Task.Delay(250); await OpenFormAsync(null); });
        return _host;
    }

    /// <summary>Row actions: View profile, Edit, Add pet, Book appointment, Archive (Cashier/Staff only for changes).</summary>
    private IReadOnlyList<MenuEntry> MenuFor(Owner owner)
    {
        var entries = new List<MenuEntry> { new("View profile", "eye", () => _ = ShowProfileAsync(owner.OwnerId)) };
        if (!CanEdit) return entries;

        entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenFormAsync(owner)));
        entries.Add(new MenuEntry("Add pet", "paw-print", () => _ = OpenPetFormAsync(owner)));
        entries.Add(new MenuEntry("Book appointment", "calendar-days", () => _ = BookForOwnerAsync(owner)));
        entries.Add(new MenuEntry("Archive", "archive", () => _ = ConfirmArchiveAsync(owner), Danger: true));
        return entries;
    }

    private async Task ShowListAsync()
    {
        _host.ShowPage(_list);
        _list.ShowLoading();
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        try
        {
            var page = await GetOwnersAsync(_list.CurrentSearch, _list.CurrentSort, _page);
            if (page.Items.Count == 0 && _page > 1)
            {
                _page--;
                page = await GetOwnersAsync(_list.CurrentSearch, _list.CurrentSort, _page);
            }

            var (total, newThisMonth) = await _owners.GetCountsAsync(ClinicId);
            var (upcoming, unpaid) = await _links.GetClinicCountsAsync(ClinicId);
            _list.ShowSummary(new OwnerListSummary
            {
                Total = total,
                NewThisMonth = newThisMonth,
                WithUpcomingAppointments = upcoming,
                WithUnpaidBalance = unpaid
            });

            var petSummaries = await _links.GetPetSummariesAsync(ClinicId, page.Items.Select(o => o.OwnerId).ToList());
            var pets = page.Items.ToDictionary(
                o => o.OwnerId,
                o => petSummaries.TryGetValue(o.OwnerId, out var p) ? new PetSummary(p.Count, p.Names) : new PetSummary(0, Array.Empty<string>()));

            _list.ShowOwners(page, pets, searching: !string.IsNullOrWhiteSpace(_list.CurrentSearch));
        }
        catch (SqlException)
        {
            _list.ShowError(DatabaseError);
        }
    }

    private async Task ShowProfileAsync(int ownerId)
    {
        try
        {
            var owner = await GetOwnerByIdAsync(ownerId);
            if (owner == null)
            {
                await ShowInfoAsync("Owner not found", "This owner was archived or no longer exists.", "circle-alert");
                await RefreshListAsync();
                return;
            }

            var profile = new OwnerProfile
            {
                Owner = owner,
                Stats = await GetOwnerSummaryAsync(ownerId),
                Pets = await _links.GetPetsAsync(ClinicId, ownerId),
                Appointments = await _links.GetAppointmentsAsync(ClinicId, ownerId),
                Bills = await _links.GetBillsAsync(ClinicId, ownerId),
                LinksAvailable = _links.BillsAvailable,
                AppointmentsAvailable = _links.AppointmentsAvailable,
                PetsAvailable = _links.PetsAvailable,
                CanEdit = CanEdit
            };

            var page = new OwnerDetailsPage();
            page.BackRequested += async (s, e) => await ShowListAsync();
            page.EditRequested += async (s, e) => await OpenFormAsync(owner);
            page.AddPetRequested += async (s, e) => await OpenPetFormAsync(owner);
            page.PetSelected += (s, petId) => OpenPet?.Invoke(petId);
            if (OpenBill != null) page.BillRequested += (s, billId) => OpenBill(billId);
            page.BookRequested += async (s, e) => await BookForOwnerAsync(owner);
            page.Show(profile);

            await _host.Panel.HideAsync();
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t open this owner", DatabaseError, "circle-alert");
        }
    }

    private async Task OpenFormAsync(Owner owner)
    {
        if (!CanEdit) return;

        var form = new OwnerFormView(owner == null
            ? new OwnerFormData()
            : new OwnerFormData
            {
                OwnerId = owner.OwnerId,
                FirstName = owner.FirstName,
                LastName = owner.LastName,
                ContactNo = owner.ContactNo,
                Email = owner.Email,
                Address = owner.Address
            });

        form.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        form.SaveRequested += async (s, addPet) => await SaveFormAsync(form, addPet);
        form.ViewDuplicateRequested += async (s, ownerId) => await ShowProfileAsync(ownerId);

        await _host.Panel.ShowAsync(form);
    }

    private async Task SaveFormAsync(OwnerFormView form, bool addPetAfter)
    {
        var data = form.Read();
        var errors = Validate(data);                                           // rules 3 and 4
        if (errors.Count > 0)
        {
            form.ShowResult(OperationResult.Invalid(errors));
            return;
        }

        var owner = OwnerFrom(data);

        form.SetBusy(true);
        try
        {
            // Rule 5: warn once about a duplicate contact number; the next save goes through.
            if (!data.DuplicateConfirmed)
            {
                var duplicate = await CheckDuplicateAsync(owner.ContactNo, data.IsNew ? null : data.OwnerId);
                if (duplicate != null)
                {
                    form.ShowDuplicate(duplicate);
                    return;
                }
            }

            var result = data.IsNew ? await AddOwnerAsync(owner) : await UpdateOwnerAsync(owner);
            if (!result.Ok)
            {
                form.ShowResult(result);
                return;
            }

            await _host.Panel.HideAsync();

            // Stay where the user was: refresh the profile if it is open, otherwise the list.
            if (_host.CurrentPage is OwnerDetailsPage) await ShowProfileAsync(owner.OwnerId);
            else await RefreshListAsync();

            if (addPetAfter) await OpenPetFormAsync(owner);
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

    /// <summary>The Pets module's Add pet form, with <paramref name="owner"/> already filled in.</summary>
    private Task OpenPetFormAsync(Owner owner)
    {
        if (!CanEdit || _petForms == null) return Task.CompletedTask;

        return _petForms.OpenFormAsync(_host, null, owner, async (pet, bookAfter) =>
        {
            // Stay where the user was: the owner's profile shows the new pet; the list its count.
            if (_host.CurrentPage is OwnerDetailsPage) await ShowProfileAsync(pet.OwnerId);
            else await RefreshListAsync();

            if (bookAfter)
                await _petForms.BookForPetAsync(_host, pet.PetId, async a =>
                {
                    if (_host.CurrentPage is OwnerDetailsPage) await ShowProfileAsync(pet.OwnerId);
                    await ShowInfoAsync("Appointment booked", PetController.BookedMessage(a, pet.PetName), "calendar-days");
                });
        });
    }

    /// <summary>"Book appointment" with this owner filled in (the pet is picked in the form).</summary>
    private async Task BookForOwnerAsync(Owner owner)
    {
        var appointments = _petForms?.Appointments;
        if (!CanEdit || appointments == null) return;

        await appointments.OpenBookFormAsync(_host, owner, 0, async a =>
        {
            if (_host.CurrentPage is OwnerDetailsPage) await ShowProfileAsync(owner.OwnerId);
            else await RefreshListAsync();
            await ShowInfoAsync("Appointment booked",
                "Appointment booked on " + a.AppointmentDate.ToString("MMM d") + " at " + a.TimeText + ".", "calendar-days");
        });
    }

    /// <summary>Rules 3 and 4.</summary>
    /// <summary>The owner to save from valid form values (contact no. as digits, email in lower case).</summary>
    internal static Owner OwnerFrom(OwnerFormData data) => new()
    {
        OwnerId = data.OwnerId,
        FirstName = data.FirstName.Trim(),
        LastName = data.LastName.Trim(),
        ContactNo = Validator.Digits(data.ContactNo),
        Email = string.IsNullOrWhiteSpace(data.Email) ? null : data.Email.Trim().ToLowerInvariant(),
        Address = string.IsNullOrWhiteSpace(data.Address) ? null : data.Address.Trim()
    };

    /// <summary>Rules 3 and 4 (also used by Appointments for walk-in and guest check-in registration).</summary>
    internal static Dictionary<string, string> Validate(OwnerFormData data)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(data.FirstName)) errors[OwnerFormData.FirstNameField] = "Required";
        else if (data.FirstName.Trim().Length > 50) errors[OwnerFormData.FirstNameField] = "Use 50 characters or fewer.";

        if (string.IsNullOrWhiteSpace(data.LastName)) errors[OwnerFormData.LastNameField] = "Required";
        else if (data.LastName.Trim().Length > 50) errors[OwnerFormData.LastNameField] = "Use 50 characters or fewer.";

        if (string.IsNullOrWhiteSpace(data.ContactNo)) errors[OwnerFormData.ContactNoField] = "Required";
        else if (!Validator.IsPhMobile(data.ContactNo))
            errors[OwnerFormData.ContactNoField] = "Enter a valid 11-digit mobile number starting with 09";

        if (!string.IsNullOrWhiteSpace(data.Email) && (data.Email.Trim().Length > 100 || !Validator.IsEmail(data.Email)))
            errors[OwnerFormData.EmailField] = "Enter a valid email";

        if (!string.IsNullOrWhiteSpace(data.Address) && data.Address.Trim().Length > 255)
            errors[OwnerFormData.AddressField] = "Use 255 characters or fewer.";

        return errors;
    }

    // Archive (rules 6 and 7) ----------------------------------------------------------------------

    private async Task ConfirmArchiveAsync(Owner owner)
    {
        try
        {
            var stats = await GetOwnerSummaryAsync(owner.OwnerId);
            if (stats.HasUnpaidBalance || stats.HasUpcomingAppointment)
            {
                await ShowInfoAsync("Can’t archive " + owner.FullName,
                    "This owner has an unpaid bill or an upcoming appointment. Settle these first.", "circle-alert");
                return;
            }
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t archive " + owner.FullName, DatabaseError, "circle-alert");
            return;
        }

        _pendingConfirm = async () =>
        {
            var result = await ArchiveOwnerAsync(owner.OwnerId);
            if (!result.Ok)
            {
                _host.Dialog.ShowResult(result);
                return;
            }

            await CloseDialogAsync();
            await ShowListAsync();
        };

        await _host.Dialog.ShowConfirmAsync(
            "Archive " + owner.FullName + "?",
            "Their records will be kept but hidden from new bookings.",
            "Archive", danger: true, icon: "archive");
    }

    /// <summary>A message with a single OK button.</summary>
    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync()
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
            await action();
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
