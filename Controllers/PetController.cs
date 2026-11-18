using System.Globalization;
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
/// Pet Profiles ("Pets"). Enforces Tasks/VetCare_Pet_Module.md, section 4:
///  1. only Cashier/Staff add, edit or archive; Veterinarians and Administrators view only;
///  2. only pets whose owner belongs to the signed-in user's clinic (Owner.ClinicID = Session.ClinicID);
///  3. owner, pet name, species and sex are required;
///  4. the birth date can't be in the future; an estimated age becomes an approximate birth date;
///  5. weight must be greater than 0 and less than 200 kg;
///  6. (a vet's new consultation weight updates the pet) – done by the Medical records module;
///  7. pets are archived (IsDeleted = 1), never deleted;
///  8. no archiving with an upcoming appointment;
///  9. archived pets (and pets of archived owners) are left out of the list and new bookings;
/// 10. pets can move to another owner, only within the same clinic.
/// </summary>
public sealed class PetController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly PetRepository _pets;
    private readonly PetLinksRepository _links;
    private readonly OwnerRepository _owners;

    private ModuleHostView _host;
    private PetsPage _list;
    private int _page = 1;
    private Func<Task> _pendingConfirm;

    public PetController(UserSession session, PetRepository pets, PetLinksRepository links, OwnerRepository owners)
    {
        _session = session;
        _pets = pets;
        _links = links;
        _owners = owners;
    }

    /// <summary>Opens an owner's profile in the Owners module. Null when the role can't open Owners (the Veterinarian).</summary>
    public Action<int> OpenOwner { get; set; }

    /// <summary>Opens a medical record (a consultation on the Medical history tab). Null when the role can't.</summary>
    public Action<int> OpenRecord { get; set; }

    /// <summary>The Vaccinations module's card (shown in this module's side panel). Null when not available.</summary>
    public VaccinationController Vaccinations { get; set; }

    /// <summary>The Appointments module's booking form ("Book appointment" here). Null hides nothing but books nothing.</summary>
    public AppointmentController Appointments { get; set; }

    /// <summary>
    /// Opens "Book appointment" in <paramref name="host"/> with this pet and its owner filled in,
    /// then runs <paramref name="afterBooked"/>.
    /// </summary>
    public async Task BookForPetAsync(ModuleHostView host, int petId, Func<Appointment, Task> afterBooked)
    {
        if (Appointments == null) return;
        var pet = await GetPetByIdAsync(petId);
        if (pet == null) return;
        var owner = new Owner
        {
            OwnerId = pet.OwnerId,
            FirstName = pet.OwnerFirstName,
            LastName = pet.OwnerLastName,
            ContactNo = pet.OwnerContactNo
        };
        await Appointments.OpenBookFormAsync(host, owner, pet.PetId, afterBooked);
    }

    /// <summary>"Appointment booked for Bantay on Nov 20 at 10:00 AM."</summary>
    public static string BookedMessage(Appointment a, string petName) =>
        "Appointment booked for " + petName + " on " + a.AppointmentDate.ToString("MMM d") + " at " + a.TimeText + ".";

    // Rules ---------------------------------------------------------------------------------

    /// <summary>Who may open the module: Cashier/Staff, Veterinarians and Administrators of a clinic.</summary>
    public bool CanView => _session.ClinicId != null &&
        (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian || _session.IsAdministrator);

    /// <summary>Rule 1: only Cashier/Staff change pet records.</summary>
    public bool CanEdit => _session.ClinicId != null && _session.RoleId == RoleIds.CashierStaff;

    /// <summary>Rule 2: always the signed-in user's clinic.</summary>
    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 6 of the module document) -----------------------------------------

    public Task<PagedResult<Pet>> GetPetsAsync(string search, string species, string sex, int page = 1) =>
        _pets.SearchAsync(new PetQuery { ClinicId = ClinicId, Search = search, Species = species, Sex = sex, Page = page, PageSize = 12 });

    public Task<IReadOnlyList<Pet>> GetPetsOfOwnerAsync(int ownerId) => _pets.GetByOwnerAsync(ClinicId, ownerId);

    public Task<Pet> GetPetByIdAsync(int petId) => _pets.GetByIdAsync(ClinicId, petId);

    /// <summary>Last visit, next appointment, next vaccine due and total visits.</summary>
    public Task<PetStats> GetPetSummaryAsync(int petId) => _links.GetStatsAsync(ClinicId, petId);

    public Task<IReadOnlyList<PetVisit>> GetMedicalHistoryAsync(int petId) => _links.GetMedicalHistoryAsync(ClinicId, petId);

    public Task<IReadOnlyList<PetVaccination>> GetVaccinationsAsync(int petId) => _links.GetVaccinationsAsync(ClinicId, petId);

    public Task<IReadOnlyList<PetWeight>> GetWeightHistoryAsync(int petId) => _links.GetWeightHistoryAsync(ClinicId, petId);

    public async Task<OperationResult> AddPetAsync(Pet pet)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can add pets.");
        if (!await _pets.OwnerInClinicAsync(ClinicId, pet.OwnerId))                       // rule 2
            return OwnerNotInClinic();

        pet.SyncStatus = SyncStatuses.Synced;
        await _pets.InsertAsync(pet);
        return OperationResult.Success(pet.PetName + " was added.");
    }

    /// <summary>Saves the pet; a different owner is a transfer (rule 10).</summary>
    public async Task<OperationResult> UpdatePetAsync(Pet pet)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can edit pets.");
        var existing = await GetPetByIdAsync(pet.PetId);
        if (existing == null) return OperationResult.Fail("This pet was archived or no longer exists.");

        if (pet.OwnerId != existing.OwnerId && !await _pets.OwnerInClinicAsync(ClinicId, pet.OwnerId))
            return OwnerNotInClinic();

        if (!await _pets.UpdateAsync(ClinicId, pet)) return OperationResult.Fail("This pet was archived or no longer exists.");
        return OperationResult.Success(pet.PetName + " was updated.");
    }

    /// <summary>Rule 10: moves the pet to another active owner of the same clinic.</summary>
    public async Task<OperationResult> TransferPetAsync(int petId, int newOwnerId)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can transfer pets.");
        var existing = await GetPetByIdAsync(petId);
        if (existing == null) return OperationResult.Fail("This pet was archived or no longer exists.");
        if (!await _pets.OwnerInClinicAsync(ClinicId, newOwnerId)) return OwnerNotInClinic();

        await _pets.TransferAsync(ClinicId, petId, newOwnerId);
        return OperationResult.Success(existing.PetName + " was transferred.");
    }

    public async Task<OperationResult> ArchivePetAsync(int petId)
    {
        if (!CanEdit) return OperationResult.Fail("Only Cashier/Staff can archive pets.");
        var existing = await GetPetByIdAsync(petId);
        if (existing == null) return OperationResult.Fail("This pet was archived or no longer exists.");

        var stats = await GetPetSummaryAsync(petId);                                      // rule 8
        if (stats.HasUpcomingAppointment) return OperationResult.Fail(UpcomingAppointmentMessage(existing));

        await _pets.ArchiveAsync(ClinicId, petId);                                        // rule 7
        return OperationResult.Success(existing.PetName + " was archived.");
    }

    private static OperationResult OwnerNotInClinic() => OperationResult.Invalid(new Dictionary<string, string>
    {
        [PetFormData.OwnerField] = "Choose an active owner of your clinic."
    });

    private static string UpcomingAppointmentMessage(Pet pet) =>
        pet.PetName + " has an upcoming appointment. Cancel or complete it first.";

    // View ----------------------------------------------------------------------------------------

    /// <param name="openPetId">Open this pet's profile straight away (e.g. a pet card on an owner profile).</param>
    public View CreateView(int? openPetId = null)
    {
        if (!CanView)
            return new Label
            {
                Text = "Pet profiles are available to Cashier/Staff, Veterinarians and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync();

        _list = new PetsPage();
        _list.SetCanEdit(CanEdit);
        _list.AddRequested += async (s, e) => await OpenFormAsync(_host, null, null, AfterSaveAsync);
        _list.FiltersChanged += async (s, e) => { _page = 1; await RefreshListAsync(); };
        _list.PageRequested += async (s, page) => { _page = page; await RefreshListAsync(); };
        _list.PetSelected += async (s, pet) => await ShowProfileAsync(pet.PetId);
        _list.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Pet));

        if (openPetId != null) _ = ShowProfileAsync(openPetId.Value);
        else _ = ShowListAsync();
        return _host;
    }

    /// <summary>Card / row actions: View profile, Edit, Book appointment, Archive (changes are Cashier/Staff only).</summary>
    private IReadOnlyList<MenuEntry> MenuFor(Pet pet)
    {
        var entries = new List<MenuEntry> { new("View profile", "eye", () => _ = ShowProfileAsync(pet.PetId)) };
        if (!CanEdit) return entries;

        entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenFormAsync(_host, pet, null, AfterSaveAsync)));
        entries.Add(new MenuEntry("Book appointment", "calendar-days", () => _ = BookFromPetsAsync(pet.PetId, pet.PetName)));
        entries.Add(new MenuEntry("Archive", "archive", () => _ = ConfirmArchiveAsync(pet), Danger: true));
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
            var page = await GetPetsAsync(_list.CurrentSearch, _list.CurrentSpecies, _list.CurrentSex, _page);
            if (page.Items.Count == 0 && _page > 1)
            {
                _page--;
                page = await GetPetsAsync(_list.CurrentSearch, _list.CurrentSpecies, _list.CurrentSex, _page);
            }

            var (total, dogs, cats) = await _pets.GetCountsAsync(ClinicId);
            _list.ShowSummary(new PetListSummary
            {
                Total = total,
                Dogs = dogs,
                Cats = cats,
                VaccinesDueThisMonth = await _links.GetVaccinesDueThisMonthAsync(ClinicId)
            });

            var info = await _links.GetListInfoAsync(ClinicId, page.Items.Select(p => p.PetId).ToList());
            var items = page.Items.Select(p => info.TryGetValue(p.PetId, out var i)
                ? new PetListItem(p, i.NextVaccineDue, i.LastVisit)
                : new PetListItem(p, null, null)).ToList();

            _list.ShowPets(new PagedResult<PetListItem>(items, page.TotalCount, page.Page, page.PageSize));
        }
        catch (SqlException)
        {
            _list.ShowError(DatabaseError);
        }
    }

    private async Task ShowProfileAsync(int petId)
    {
        try
        {
            var pet = await GetPetByIdAsync(petId);
            if (pet == null)
            {
                await ShowListAsync();
                await ShowInfoAsync("Pet not found", "This pet was archived or no longer exists.", "circle-alert");
                return;
            }

            var profile = new PetProfile
            {
                Pet = pet,
                Stats = await GetPetSummaryAsync(petId),
                Visits = await GetMedicalHistoryAsync(petId),
                Vaccinations = await GetVaccinationsAsync(petId),
                Appointments = await _links.GetAppointmentsAsync(ClinicId, petId),
                Prescriptions = await _links.GetPrescriptionsAsync(ClinicId, petId),
                Weights = await GetWeightHistoryAsync(petId),
                MedicalRecordsAvailable = _links.MedicalRecordsAvailable,
                AppointmentsAvailable = _links.AppointmentsAvailable,
                VaccinationsAvailable = _links.VaccinationsAvailable,
                PrescriptionsAvailable = _links.PrescriptionsAvailable,
                CanEdit = CanEdit,
                CanOpenOwner = OpenOwner != null,
                CanOpenVaccinationCard = Vaccinations?.CanView == true
            };

            var page = new PetDetailsPage();
            page.BackRequested += async (s, e) => await ShowListAsync();
            page.EditRequested += async (s, e) => await OpenFormAsync(_host, pet, null, AfterSaveAsync);
            page.BookRequested += async (s, e) => await BookFromPetsAsync(pet.PetId, pet.PetName);
            page.OwnerRequested += (s, ownerId) => OpenOwner?.Invoke(ownerId);
            page.RecordRequested += (s, recordId) => OpenRecord?.Invoke(recordId);
            page.VaccinationCardRequested += async (s, petId) =>
            {
                if (Vaccinations != null) await Vaccinations.ShowCardAsync(_host, petId);
            };
            page.Show(profile);

            await _host.Panel.HideAsync();
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            if (_host.CurrentPage == null) _host.ShowPage(_list);
            await ShowInfoAsync("Can’t open this pet", DatabaseError, "circle-alert");
        }
    }

    /// <summary>After a save inside the Pets module: stay where the user was.</summary>
    private async Task AfterSaveAsync(Pet pet, bool bookAfter)
    {
        if (_host.CurrentPage is PetDetailsPage) await ShowProfileAsync(pet.PetId);
        else await RefreshListAsync();

        if (bookAfter) await BookFromPetsAsync(pet.PetId, pet.PetName);
    }

    /// <summary>Book appointment from the Pets list or profile; afterwards the profile shows the new visit.</summary>
    private async Task BookFromPetsAsync(int petId, string petName)
    {
        if (!CanEdit || Appointments == null) return;
        try
        {
            await BookForPetAsync(_host, petId, async a =>
            {
                if (_host.CurrentPage is PetDetailsPage) await ShowProfileAsync(petId);
                await ShowInfoAsync("Appointment booked", BookedMessage(a, petName), "calendar-days");
            });
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t book right now", DatabaseError, "circle-alert");
        }
    }

    // Add / Edit form (also used by the Owners module) ------------------------------------------

    /// <summary>
    /// Opens the Add / Edit pet form in <paramref name="host"/>'s side panel.
    /// </summary>
    /// <param name="pet">The pet to edit, or null to add one.</param>
    /// <param name="owner">For a new pet: the owner to fill in (opened from an owner profile), or null.</param>
    /// <param name="afterSave">Runs after a successful save (the saved pet; true for "Save and book appointment").</param>
    public async Task OpenFormAsync(ModuleHostView host, Pet pet, Owner owner, Func<Pet, bool, Task> afterSave)
    {
        if (!CanEdit) return;

        PetFormData data;
        if (pet == null)
        {
            data = new PetFormData();
        }
        else
        {
            data = new PetFormData
            {
                PetId = pet.PetId,
                OwnerId = pet.OwnerId,
                OriginalOwnerId = pet.OwnerId,
                PetName = pet.PetName,
                Species = pet.Species,
                Breed = pet.Breed,
                Sex = pet.Sex,
                BirthDate = pet.BirthDate,
                Color = pet.Color,
                Weight = pet.Weight?.ToString("0.##", CultureInfo.InvariantCulture)
            };
            owner = new Owner
            {
                OwnerId = pet.OwnerId,
                FirstName = pet.OwnerFirstName,
                LastName = pet.OwnerLastName,
                ContactNo = pet.OwnerContactNo
            };
        }

        var form = new PetFormView(data, owner);
        int searchNumber = 0;
        form.OwnerSearchRequested += async (s, text) =>
        {
            int mine = ++searchNumber;
            try
            {
                var result = await _owners.SearchAsync(new OwnerQuery
                {
                    ClinicId = ClinicId,                                                    // rule 2
                    Search = text,
                    Sort = OwnerSort.NameAToZ,
                    PageSize = 6
                });
                if (mine == searchNumber) form.ShowOwnerResults(result.Items);         // ignore older, slower searches
            }
            catch (SqlException)
            {
                if (mine == searchNumber) form.ShowOwnerSearchError(DatabaseError);
            }
        };
        form.CancelRequested += async (s, e) => await host.Panel.HideAsync();
        form.SaveRequested += async (s, book) => await SaveFormAsync(host, form, book, afterSave);

        await host.Panel.ShowAsync(form, PetFormView.PanelWidth);
    }

    private async Task SaveFormAsync(ModuleHostView host, PetFormView form, bool bookAfter, Func<Pet, bool, Task> afterSave)
    {
        var data = form.Read();
        var errors = Validate(data, DateTime.Today, out var pet);                     // rules 3, 4, 5
        if (errors.Count > 0)
        {
            form.ShowResult(OperationResult.Invalid(errors));
            return;
        }

        form.SetBusy(true);
        try
        {
            var result = data.IsNew ? await AddPetAsync(pet) : await UpdatePetAsync(pet);
            if (!result.Ok)
            {
                form.ShowResult(result);
                return;
            }

            await host.Panel.HideAsync();
            if (afterSave != null) await afterSave(pet, bookAfter);
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

    /// <summary>Rules 3, 4 and 5, plus the Pet table's column lengths. Builds the pet when valid.</summary>
    public static Dictionary<string, string> Validate(PetFormData data, DateTime today, out Pet pet)
    {
        var errors = new Dictionary<string, string>();
        pet = null;

        if (data.OwnerId <= 0) errors[PetFormData.OwnerField] = "Required";

        string name = data.PetName?.Trim();
        if (string.IsNullOrEmpty(name)) errors[PetFormData.PetNameField] = "Required";
        else if (name.Length > 50) errors[PetFormData.PetNameField] = "Use 50 characters or fewer.";

        if (string.IsNullOrEmpty(data.Species) || !PetSpecies.All.Contains(data.Species)) errors[PetFormData.SpeciesField] = "Required";
        if (string.IsNullOrEmpty(data.Sex) || !PetSpecies.Sexes.Contains(data.Sex)) errors[PetFormData.SexField] = "Required";

        string breed = Optional(data.Breed);
        if (breed?.Length > 50) errors[PetFormData.BreedField] = "Use 50 characters or fewer.";
        string color = Optional(data.Color);
        if (color?.Length > 30) errors[PetFormData.ColorField] = "Use 30 characters or fewer.";

        // Rule 4: birth date, or an estimated age turned into an approximate birth date.
        DateTime? birthDate = null;
        if (data.UseEstimatedAge)
        {
            string yearsText = data.AgeYears?.Trim(), monthsText = data.AgeMonths?.Trim();
            if (!string.IsNullOrEmpty(yearsText) || !string.IsNullOrEmpty(monthsText))
            {
                int years = 0, months = 0;
                bool yearsOk = string.IsNullOrEmpty(yearsText) || int.TryParse(yearsText, out years);
                bool monthsOk = string.IsNullOrEmpty(monthsText) || int.TryParse(monthsText, out months);
                if (!yearsOk || !monthsOk || years < 0 || months < 0) errors[PetFormData.AgeField] = "Enter the age in whole years and months.";
                else if (months > 11) errors[PetFormData.AgeField] = "Months must be 0 to 11.";
                else if (years > 40) errors[PetFormData.AgeField] = "Enter an age of 40 years or less.";
                else if (years == 0 && months == 0) errors[PetFormData.AgeField] = "Enter an age of at least 1 month.";
                else birthDate = AgeCalculator.EstimateBirthDate(years, months, today);
            }
        }
        else if (data.BirthDate != null)
        {
            if (data.BirthDate.Value.Date > today.Date) errors[PetFormData.BirthDateField] = "Birth date can’t be in the future";
            else birthDate = data.BirthDate.Value.Date;
        }

        // Rule 5: optional, but if given more than 0 and less than 200 kg (2 decimals, like the column).
        decimal? weight = null;
        string weightText = data.Weight?.Trim();
        if (!string.IsNullOrEmpty(weightText))
        {
            bool parsed = decimal.TryParse(weightText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                       || decimal.TryParse(weightText, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
            value = Math.Round(value, 2);
            if (!parsed || value <= 0 || value >= 200) errors[PetFormData.WeightField] = "Enter a weight above 0 and below 200 kg";
            else weight = value;
        }

        if (errors.Count > 0) return errors;

        pet = new Pet
        {
            PetId = data.PetId,
            OwnerId = data.OwnerId,
            PetName = name,
            Species = data.Species,
            Breed = breed,
            Sex = data.Sex,
            BirthDate = birthDate,
            Color = color,
            Weight = weight
        };
        return errors;
    }

    private static string Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Archive (rules 7 and 8) ----------------------------------------------------------------------

    private async Task ConfirmArchiveAsync(Pet pet)
    {
        try
        {
            var stats = await GetPetSummaryAsync(pet.PetId);
            if (stats.HasUpcomingAppointment)
            {
                await ShowInfoAsync("Can’t archive " + pet.PetName, UpcomingAppointmentMessage(pet), "circle-alert");
                return;
            }
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t archive " + pet.PetName, DatabaseError, "circle-alert");
            return;
        }

        _pendingConfirm = async () =>
        {
            var result = await ArchivePetAsync(pet.PetId);
            if (!result.Ok)
            {
                _host.Dialog.ShowResult(result);
                return;
            }

            await CloseDialogAsync();
            await ShowListAsync();
        };

        await _host.Dialog.ShowConfirmAsync(
            "Archive " + pet.PetName + "?",
            "Their health records will be kept but they won’t appear in new bookings.",
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
