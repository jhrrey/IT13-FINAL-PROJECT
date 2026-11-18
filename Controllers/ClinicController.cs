using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Data;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.SuperAdmin;

namespace VetCare.Controllers;

/// <summary>
/// Clinic management (Tasks/VetCare_Clinic_Management_Module.md, section 5):
///  1. only the Super Admin opens it;  2. name, address, contact no. and email are required, the email is unique
///     and the contact no. is a valid PH number;  3. a duplicate warning when another clinic matches;
///  4. only Pending clinics are approved or rejected;  5. approving activates the clinic and its Administrator;
///  6. rejecting needs a reason, rejected sign-ups are cleared after 30 days;  7. deactivating needs a reason
///     (users can't sign in, devices can't sync, data is kept);  8. activating needs an active Administrator;
///  9. clinics are never deleted;  10. counts only;  11. status changes need a connection.
/// </summary>
public sealed class ClinicController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";
    private const string OfflineMessage = "You're offline. This change needs an internet connection so every clinic computer gets it.";
    private const string ReasonField = "reason";

    private readonly UserSession _session;
    private readonly ClinicRepository _clinics;
    private readonly UserRepository _users;
    private readonly DeviceRepository _devices;

    private ModuleHostView _host;
    private Toast _toast;
    private ClinicsPage _list;
    private ClinicDetailsPage _details;
    private Func<IDictionary<string, string>, Task> _pendingConfirm;
    private string _warnedDuplicate;

    public ClinicController(UserSession session, ClinicRepository clinics, UserRepository users, DeviceRepository devices)
    {
        _session = session;
        _clinics = clinics;
        _users = users;
        _devices = devices;
    }

    /// <summary>"tab:pending" opens the Pending approval tab.</summary>
    public string StartIntent { get; init; }

    private bool Allowed => _session.IsSuperAdmin;   // rule 1

    // Data (section 7) ---------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<Clinic>> GetClinicsAsync(string search, string status) => _clinics.SearchAsync(search, status);

    /// <summary>Pending sign-ups, oldest first, each flagged when another clinic matches (rule 3).</summary>
    public async Task<IReadOnlyList<Clinic>> GetPendingClinicsAsync()
    {
        var pending = await _clinics.GetPendingAsync();
        foreach (var c in pending)
            c.PossibleDuplicate = (await CheckDuplicateAsync(c.ClinicName, c.Address, c.Email, c.ContactNo, c.ClinicId)).Count > 0;
        return pending;
    }

    public Task<ClinicSummary> GetClinicSummaryAsync() => _clinics.GetSummaryAsync();

    public Task<Clinic> GetClinicByIdAsync(int clinicId) => _clinics.GetByIdAsync(clinicId);

    public Task<ClinicOverview> GetClinicOverviewAsync(int clinicId) => _clinics.GetOverviewAsync(clinicId, DateTime.Today);

    public Task<IReadOnlyList<ClinicDuplicate>> CheckDuplicateAsync(string name, string address, string email, string contactNo, int? excludeId) =>
        _clinics.FindDuplicatesAsync(name?.Trim(), address?.Trim(), email?.Trim(), Validator.Digits(contactNo), excludeId);

    /// <summary>Super Admin's Add clinic: the clinic and its first Administrator are active right away.</summary>
    public async Task<OperationResult> AddClinicAsync(ClinicFormData data)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can add clinics.");
        var errors = ValidateClinic(data, adding: true);
        if (!errors.ContainsKey(ClinicFormView.EmailField) && await _clinics.EmailUsedAsync(data.Email, null))
            errors[ClinicFormView.EmailField] = "This email is already used by another clinic.";
        if (!errors.ContainsKey(ClinicFormView.AdminUsernameField) && await _users.UsernameExistsAsync(data.AdminUsername))
            errors[ClinicFormView.AdminUsernameField] = "This username is already taken.";
        if (!errors.ContainsKey(ClinicFormView.AdminEmailField) && await _users.EmailExistsAsync(data.AdminEmail))
            errors[ClinicFormView.AdminEmailField] = "An account with this email already exists.";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var clinic = new Clinic { ClinicName = data.ClinicName, Address = data.Address, ContactNo = data.ContactNo, Email = data.Email, Status = AccountStatus.Active };
        var admin = new User
        {
            RoleId = RoleIds.Administrator,
            FirstName = data.AdminFirstName,
            LastName = data.AdminLastName,
            Username = data.AdminUsername,
            Email = data.AdminEmail,
            PasswordHash = PasswordHasher.Hash(data.Password),
            MustChangePassword = SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset),
            Status = AccountStatus.Active
        };
        try
        {
            await _clinics.RegisterWithAdministratorAsync(clinic, admin, _session.UserId);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return OperationResult.Invalid(new Dictionary<string, string>
            {
                [ex.Message.Contains("UX_User_Email") ? ClinicFormView.AdminEmailField : ClinicFormView.AdminUsernameField] =
                    ex.Message.Contains("UX_User_Email") ? "An account with this email already exists." : "This username is already taken."
            });
        }
        return OperationResult.Success(clinic.ClinicName + " was added and is active.");
    }

    public async Task<OperationResult> UpdateClinicAsync(ClinicFormData data)
    {
        if (!Allowed || data.ClinicId == null) return OperationResult.Fail("Only the Super Admin can edit clinics.");
        var existing = await _clinics.GetByIdAsync(data.ClinicId.Value);
        if (existing == null) return OperationResult.Fail("This clinic no longer exists.");
        var errors = ValidateClinic(data, adding: false);
        if (!errors.ContainsKey(ClinicFormView.EmailField) && await _clinics.EmailUsedAsync(data.Email, existing.ClinicId))
            errors[ClinicFormView.EmailField] = "This email is already used by another clinic.";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        existing.ClinicName = data.ClinicName;
        existing.Address = data.Address;
        existing.ContactNo = data.ContactNo;
        existing.Email = data.Email;
        await _clinics.UpdateAsync(existing);
        return OperationResult.Success(existing.ClinicName + " was updated.");
    }

    public async Task<OperationResult> ApproveClinicAsync(int clinicId)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can approve clinics.");
        if (!await IsOnlineAsync()) return OperationResult.Fail(OfflineMessage);                         // rule 11
        var clinic = await _clinics.GetByIdAsync(clinicId);
        if (clinic?.Status != AccountStatus.Pending) return OperationResult.Fail("Only pending sign-ups can be approved."); // rule 4
        if (!await _clinics.ApproveAsync(clinicId, _session.UserId)) return OperationResult.Fail("Only pending sign-ups can be approved.");
        return OperationResult.Success(clinic.ClinicName + " is now active.");                         // rule 5
    }

    public async Task<OperationResult> RejectClinicAsync(int clinicId, string reason)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can reject clinics.");
        if (string.IsNullOrWhiteSpace(reason)) return ReasonRequired();                                // rule 6
        if (!await IsOnlineAsync()) return OperationResult.Fail(OfflineMessage);
        var clinic = await _clinics.GetByIdAsync(clinicId);
        if (clinic?.Status != AccountStatus.Pending || !await _clinics.RejectAsync(clinicId, reason.Trim()))
            return OperationResult.Fail("Only pending sign-ups can be rejected.");
        return OperationResult.Success(clinic.ClinicName + " was rejected.");
    }

    public async Task<OperationResult> DeactivateClinicAsync(int clinicId, string reason)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can deactivate clinics.");
        if (string.IsNullOrWhiteSpace(reason)) return ReasonRequired();                                // rule 7
        if (!await IsOnlineAsync()) return OperationResult.Fail(OfflineMessage);
        var clinic = await _clinics.GetByIdAsync(clinicId);
        if (clinic?.Status != AccountStatus.Active) return OperationResult.Fail("Only active clinics can be deactivated.");
        await _clinics.DeactivateAsync(clinicId, reason.Trim());
        return OperationResult.Success(clinic.ClinicName + " was deactivated. Its data is kept.");
    }

    public async Task<OperationResult> ActivateClinicAsync(int clinicId)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can activate clinics.");
        if (!await IsOnlineAsync()) return OperationResult.Fail(OfflineMessage);
        var clinic = await _clinics.GetByIdAsync(clinicId);
        if (clinic?.Status != AccountStatus.Inactive) return OperationResult.Fail("Only inactive clinics can be activated.");
        if (await _users.CountActiveAdministratorsAsync(clinicId, 0) == 0)                               // rule 8
            return OperationResult.Fail("Add or activate an administrator first.");
        await _clinics.ActivateAsync(clinicId);
        return OperationResult.Success(clinic.ClinicName + " is active again.");
    }

    public async Task<IReadOnlyList<User>> GetClinicAdminsAsync(int clinicId)
    {
        var page = await _users.SearchAsync(new UserQuery
        {
            ClinicId = clinicId,
            RoleId = RoleIds.Administrator,
            VisibleRoleIds = new[] { RoleIds.Administrator },
            PageSize = 200
        });
        return page.Items;
    }

    public async Task<OperationResult> AddAdminAsync(int clinicId, UserFormData data)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can add administrators here.");
        var errors = ValidateAdmin(data.FirstName, data.LastName, data.Username, data.Email, data.Password, data.ConfirmPassword,
            UserFormData.FirstNameField, UserFormData.LastNameField, UserFormData.UsernameField, UserFormData.EmailField,
            UserFormData.PasswordField, UserFormData.ConfirmPasswordField);
        if (!errors.ContainsKey(UserFormData.UsernameField) && await _users.UsernameExistsAsync(data.Username))
            errors[UserFormData.UsernameField] = "This username is already taken.";
        if (!errors.ContainsKey(UserFormData.EmailField) && await _users.EmailExistsAsync(data.Email))
            errors[UserFormData.EmailField] = "An account with this email already exists.";
        if (!string.IsNullOrWhiteSpace(data.ContactNo) && !Validator.IsPhone(data.ContactNo))
            errors[UserFormData.ContactNoField] = "Please enter a valid contact number.";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var clinic = await _clinics.GetByIdAsync(clinicId);
        if (clinic == null || clinic.Status is not (AccountStatus.Active or AccountStatus.Pending))
            return OperationResult.Fail("Admins can only be added to active or pending clinics.");
        var admin = new User
        {
            ClinicId = clinicId,
            RoleId = RoleIds.Administrator,
            FirstName = data.FirstName,
            LastName = data.LastName,
            Username = data.Username,
            Email = data.Email.Trim().ToLowerInvariant(),
            ContactNo = string.IsNullOrWhiteSpace(data.ContactNo) ? null : data.ContactNo.Trim(),
            PasswordHash = PasswordHasher.Hash(data.Password),
            MustChangePassword = SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset),
            // A pending sign-up's admins are turned on when the clinic is approved.
            Status = clinic.Status == AccountStatus.Pending ? AccountStatus.Inactive : data.IsActive ? AccountStatus.Active : AccountStatus.Inactive
        };
        await _users.InsertAsync(admin);
        return OperationResult.Success(admin.FullName + " was added as an administrator.");
    }

    /// <summary>Generates a temporary password (shown once); the admin sets their own at the next sign-in.</summary>
    public async Task<(OperationResult Result, string Password)> ResetAdminPasswordAsync(User admin)
    {
        if (!Allowed || admin.RoleId != RoleIds.Administrator) return (OperationResult.Fail("You can’t reset this password."), null);
        if (!await IsOnlineAsync()) return (OperationResult.Fail("Connect to the internet to reset a password."), null);
        string password = PasswordGenerator.Generate();
        await _users.UpdatePasswordAsync(admin.UserId, PasswordHasher.Hash(password), mustChange: SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset));
        return (OperationResult.Success("Password reset for " + admin.FullName + "."), password);
    }

    public async Task<OperationResult> SetAdminActiveAsync(User admin, bool active)
    {
        if (!Allowed || admin.RoleId != RoleIds.Administrator || admin.ClinicId == null) return OperationResult.Fail("You can’t change this account.");
        var clinic = await _clinics.GetByIdAsync(admin.ClinicId.Value);
        if (!active && clinic?.Status == AccountStatus.Active && admin.IsActive &&
            await _users.CountActiveAdministratorsAsync(admin.ClinicId.Value, admin.UserId) == 0)
            return OperationResult.Fail(clinic.ClinicName + " must keep at least one active Administrator. Add or activate another Administrator first.");
        await _users.SetStatusAsync(admin.UserId, active ? AccountStatus.Active : AccountStatus.Inactive);
        return OperationResult.Success(admin.FullName + (active ? " was activated." : " was deactivated."));
    }

    public Task<IReadOnlyList<ClinicDevice>> GetClinicDevicesAsync(int clinicId) => _devices.GetByClinicAsync(clinicId);

    public async Task<OperationResult> SetDeviceActiveAsync(ClinicDevice device, bool isActive)
    {
        if (!Allowed) return OperationResult.Fail("Only the Super Admin can change devices here.");
        if (!await IsOnlineAsync()) return OperationResult.Fail(OfflineMessage);
        await _devices.SetActiveAsync(device.ClinicId, device.DeviceId, isActive);
        return OperationResult.Success(device.DeviceName + (isActive ? " can sync again." : " can no longer sync or sign in."));
    }

    public Task<IReadOnlyList<SyncLogEntry>> GetClinicSyncLogsAsync(int clinicId, SyncLogFilter f) =>
        _devices.GetSyncLogsAsync(clinicId, f.From, f.To, f.DeviceId, f.Status);

    /// <summary>Rule 11: true when the central database can be reached.</summary>
    public static async Task<bool> IsOnlineAsync()
    {
        try
        {
            await using var connection = await Database.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    // View ----------------------------------------------------------------------------------------------------------

    public View CreateView()
    {
        if (!Allowed)
            return new Label { Text = "Clinic management is only available to the Super Admin.", FontSize = 16, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(40) };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync(e.Values);

        _list = new ClinicsPage();
        _list.AddRequested += async (s, e) => await OpenFormAsync(null);
        _list.FiltersChanged += async (s, e) => await RefreshListAsync();
        _list.TabChanged += async (s, e) => await RefreshListAsync();
        _list.ClinicSelected += async (s, c) => await OpenDetailsAsync(c.ClinicId);
        _list.ReviewRequested += async (s, c) => await OpenReviewAsync(c.ClinicId);
        _list.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Clinic));
        _host.ShowPage(_list);
        _list.ShowLoading();

        _toast = new Toast();
        _ = StartAsync();
        return new Grid { Children = { _host, _toast } };
    }

    private async Task StartAsync()
    {
        try { await _clinics.PurgeRejectedAsync(DateTime.Now); }                                         // rule 6
        catch (SqlException) { /* the list shows the database error */ }
        // Dashboard shortcuts: "tab:pending", "add", "clinic:{id}", "clinic:{id}:devices", "clinic:{id}:sync[:failed]".
        string intent = StartIntent ?? string.Empty;
        if (intent == "tab:pending") { _list.OpenTab(ClinicsPage.PendingTab); return; }
        await RefreshListAsync();
        if (intent == "add") { await OpenFormAsync(null); return; }
        var parts = intent.Split(':');
        if (parts[0] == "clinic" && parts.Length > 1 && int.TryParse(parts[1], out int id))
        {
            int tab = parts.Length > 2 && parts[2] == "devices" ? ClinicDetailsPage.DevicesTab
                : parts.Length > 2 && parts[2] == "sync" ? ClinicDetailsPage.SyncTab
                : ClinicDetailsPage.OverviewTab;
            if (tab == ClinicDetailsPage.SyncTab && parts.Length > 3 && parts[3] == "failed")
            {
                await OpenDetailsAsync(id);
                _details?.SetSyncFilter(DateTime.Today, "Failed");
                _details?.OpenTab(ClinicDetailsPage.SyncTab);
            }
            else await OpenDetailsAsync(id, tab);
        }
    }

    private async Task RefreshListAsync()
    {
        _list.ShowLoading();
        try
        {
            _list.ShowSummary(await GetClinicSummaryAsync());
            if (_list.CurrentTab == ClinicsPage.PendingTab) _list.ShowPending(await GetPendingClinicsAsync());
            else _list.ShowClinics(await GetClinicsAsync(_list.SearchText, _list.StatusFilter));
        }
        catch (SqlException)
        {
            _list.ShowError(DatabaseError);
        }
    }

    private IReadOnlyList<MenuEntry> MenuFor(Clinic c)
    {
        var entries = new List<MenuEntry> { new("View details", "eye", () => _ = OpenDetailsAsync(c.ClinicId)) };
        if (c.Status == AccountStatus.Pending) entries.Add(new MenuEntry("Review sign-up", "user-check", () => _ = OpenReviewAsync(c.ClinicId)));
        if (c.Status != AccountStatus.Rejected) entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenFormAsync(c.ClinicId)));
        if (c.Status == AccountStatus.Active) entries.Add(new MenuEntry("Deactivate", "circle-pause", () => _ = ConfirmDeactivateAsync(c.ClinicId), Danger: true));
        if (c.Status == AccountStatus.Inactive) entries.Add(new MenuEntry("Activate", "circle-check", () => _ = ConfirmActivateAsync(c.ClinicId)));
        return entries;
    }

    /// <summary>After a change: refresh the details page if it's showing that clinic, otherwise the list.</summary>
    private async Task AfterChangeAsync(int clinicId)
    {
        if (_details != null && _host.CurrentPage == _details && _details.Clinic.ClinicId == clinicId)
            await OpenDetailsAsync(clinicId, _details.CurrentTab);
        else
            await RefreshListAsync();
    }

    // Details page --------------------------------------------------------------------------------------------------

    private async Task OpenDetailsAsync(int clinicId, int tab = ClinicDetailsPage.OverviewTab)
    {
        await _host.Panel.HideAsync();
        Clinic clinic;
        try { clinic = await GetClinicByIdAsync(clinicId); }
        catch (SqlException) { await ShowInfoAsync("Can’t open this clinic", DatabaseError, "circle-alert"); return; }
        if (clinic == null) { await RefreshListAsync(); return; }

        var page = new ClinicDetailsPage(clinic);
        page.BackRequested += async (s, e) => { _details = null; _host.ShowPage(_list); await RefreshListAsync(); };
        page.EditRequested += async (s, e) => await OpenFormAsync(clinicId);
        page.ReviewRequested += async (s, e) => await OpenReviewAsync(clinicId);
        page.StatusToggleRequested += async (s, e) =>
        {
            if (clinic.Status == AccountStatus.Active) await ConfirmDeactivateAsync(clinicId);
            else await ConfirmActivateAsync(clinicId);
        };
        page.TabChanged += async (s, t) => await LoadTabAsync(page);
        page.SyncFiltersChanged += async (s, e) => await LoadTabAsync(page);
        page.AddAdminRequested += async (s, e) => await OpenAdminFormAsync(clinic);
        page.AdminMenuRequested += (s, e) => _host.Menu.Show(e.Anchor, AdminMenuFor(e.Admin));
        page.DeviceToggleRequested += async (s, d) => await ConfirmDeviceToggleAsync(d);
        _details = page;
        _host.ShowPage(page);
        if (tab != ClinicDetailsPage.OverviewTab) page.OpenTab(tab);
        else await LoadTabAsync(page);
    }

    private async Task LoadTabAsync(ClinicDetailsPage page)
    {
        int id = page.Clinic.ClinicId;
        page.ShowLoading();
        try
        {
            switch (page.CurrentTab)
            {
                case ClinicDetailsPage.AdminsTab:
                    page.ShowAdmins(await GetClinicAdminsAsync(id));
                    break;
                case ClinicDetailsPage.DevicesTab:
                    page.ShowDevices(await GetClinicDevicesAsync(id));
                    break;
                case ClinicDetailsPage.SyncTab:
                    page.SetDevices(await GetClinicDevicesAsync(id));
                    page.ShowSyncLog(await GetClinicSyncLogsAsync(id, page.ReadSyncFilter()));
                    break;
                default:
                    page.ShowOverview(await GetClinicOverviewAsync(id));
                    break;
            }
        }
        catch (SqlException)
        {
            page.ShowError(DatabaseError);
        }
    }

    // Review sign-up ------------------------------------------------------------------------------------------------

    private async Task OpenReviewAsync(int clinicId)
    {
        Clinic clinic;
        IReadOnlyList<ClinicDuplicate> duplicates;
        try
        {
            clinic = await GetClinicByIdAsync(clinicId);
            if (clinic == null || clinic.Status != AccountStatus.Pending) { await AfterChangeAsync(clinicId); return; }
            duplicates = await CheckDuplicateAsync(clinic.ClinicName, clinic.Address, clinic.Email, clinic.ContactNo, clinic.ClinicId);
        }
        catch (SqlException) { await ShowInfoAsync("Can’t open this sign-up", DatabaseError, "circle-alert"); return; }

        var view = new ReviewClinicView(clinic, duplicates);
        view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        view.ViewClinicRequested += async (s, id) => await OpenDetailsAsync(id);
        view.ApproveRequested += async (s, e) =>
        {
            _pendingConfirm = async _ => await RunDialogAsync(() => ApproveClinicAsync(clinicId), clinicId, closePanel: true);
            string admin = string.IsNullOrEmpty(clinic.AdminName) ? "Their administrator" : "Their administrator, " + clinic.AdminName + ",";
            await _host.Dialog.ShowConfirmAsync("Approve " + clinic.ClinicName + "?", admin + " will be able to log in right away.",
                "Approve", danger: false, icon: "circle-check");
        };
        view.RejectRequested += async (s, e) =>
        {
            _pendingConfirm = async values =>
            {
                values.TryGetValue(ReasonField, out var reason);
                await RunDialogAsync(() => RejectClinicAsync(clinicId, reason), clinicId, closePanel: true);
            };
            await _host.Dialog.ShowConfirmWithNoteAsync("Reject " + clinic.ClinicName + "?",
                "Rejected sign-ups are removed after " + SettingsCache.GetInt(SettingKeys.RejectedSignupDays) + " days.", "Reject", danger: true, icon: "x",
                ReasonField, "Reason (sent to the applicant) *", "e.g. We couldn't verify the clinic's details.", 255);
        };
        await _host.Panel.ShowAsync(view, ReviewClinicView.PanelWidth);
        view.SetOffline(!await IsOnlineAsync());
    }

    // Add / Edit clinic ---------------------------------------------------------------------------------------------

    private async Task OpenFormAsync(int? clinicId)
    {
        Clinic existing = null;
        if (clinicId != null)
        {
            try { existing = await GetClinicByIdAsync(clinicId.Value); }
            catch (SqlException) { await ShowInfoAsync("Can’t edit this clinic", DatabaseError, "circle-alert"); return; }
            if (existing == null) return;
        }

        _warnedDuplicate = null;
        var form = new ClinicFormView(existing);
        form.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        form.SaveRequested += async (s, e) => await SaveFormAsync(form);
        await _host.Panel.ShowAsync(form, ClinicFormView.PanelWidth);
    }

    private async Task SaveFormAsync(ClinicFormView form)
    {
        var data = form.Read();
        form.SetBusy(true);
        try
        {
            // Rule 3: warn once about a matching clinic; saving again keeps it.
            var errors = ValidateClinic(data, adding: data.ClinicId == null);
            if (errors.Count == 0)
            {
                var duplicates = await CheckDuplicateAsync(data.ClinicName, data.Address, data.Email, data.ContactNo, data.ClinicId);
                string signature = string.Join("|", duplicates.Select(d => d.ClinicId + d.MatchedOn));
                form.ShowDuplicates(duplicates);
                if (duplicates.Count > 0 && _warnedDuplicate != signature)
                {
                    _warnedDuplicate = signature;
                    form.ShowResult(OperationResult.Success());
                    return;
                }
            }

            var result = data.ClinicId == null ? await AddClinicAsync(data) : await UpdateClinicAsync(data);
            form.ShowResult(result);
            if (!result.Ok) return;
            await _host.Panel.HideAsync();
            _toast.Show(result.Message);
            if (data.ClinicId != null) await AfterChangeAsync(data.ClinicId.Value);
            else await RefreshListAsync();
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

    // Deactivate / Activate -----------------------------------------------------------------------------------------

    private async Task ConfirmDeactivateAsync(int clinicId)
    {
        Clinic clinic;
        try { clinic = await GetClinicByIdAsync(clinicId); }
        catch (SqlException) { await ShowInfoAsync("Can’t deactivate right now", DatabaseError, "circle-alert"); return; }
        if (clinic == null) return;

        string users = clinic.UserCount == 1 ? "1 user" : clinic.UserCount + " users";
        string devices = clinic.DeviceCount == 1 ? "1 computer" : clinic.DeviceCount + " computers";
        _pendingConfirm = async values =>
        {
            values.TryGetValue(ReasonField, out var reason);
            await RunDialogAsync(() => DeactivateClinicAsync(clinicId, reason), clinicId);
        };
        await _host.Dialog.ShowConfirmWithNoteAsync("Deactivate " + clinic.ClinicName + "?",
            "Its " + users + " won't be able to log in and its " + devices + " will stop syncing. All data will be kept.",
            "Deactivate", danger: true, icon: "circle-pause", ReasonField, "Reason *", "e.g. The clinic stopped using VetCare.", 255);
        _host.Dialog.SetBusy(false, offline: !await IsOnlineAsync());
    }

    private async Task ConfirmActivateAsync(int clinicId)
    {
        Clinic clinic;
        int activeAdmins;
        try
        {
            clinic = await GetClinicByIdAsync(clinicId);
            if (clinic == null) return;
            activeAdmins = await _users.CountActiveAdministratorsAsync(clinicId, 0);
        }
        catch (SqlException) { await ShowInfoAsync("Can’t activate right now", DatabaseError, "circle-alert"); return; }

        if (activeAdmins == 0)                                                                            // rule 8
        {
            await ShowInfoAsync("Can’t activate " + clinic.ClinicName, "Add or activate an administrator first.", "user-cog");
            return;
        }
        _pendingConfirm = async _ => await RunDialogAsync(() => ActivateClinicAsync(clinicId), clinicId);
        await _host.Dialog.ShowConfirmAsync("Activate " + clinic.ClinicName + " again?",
            "Users with active accounts can log in right away.", "Activate", danger: false, icon: "circle-check");
        _host.Dialog.SetBusy(false, offline: !await IsOnlineAsync());
    }

    // Administrators ------------------------------------------------------------------------------------------------

    private async Task OpenAdminFormAsync(Clinic clinic)
    {
        var form = new AdminFormView(new AdminFormSetup
        {
            Clinics = new[] { clinic },
            ClinicLocked = true,
            Values = new UserFormData { ClinicId = clinic.ClinicId, IsActive = true }
        });
        form.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        form.SaveRequested += async (s, e) =>
        {
            form.SetBusy(true);
            try
            {
                var result = await AddAdminAsync(clinic.ClinicId, form.Read());
                form.ShowResult(result);
                if (!result.Ok) return;
                await _host.Panel.HideAsync();
                _toast.Show(result.Message);
                await AfterChangeAsync(clinic.ClinicId);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                form.ShowResult(Field(ex.Message.Contains("UX_User_Email") ? UserFormData.EmailField : UserFormData.UsernameField,
                    ex.Message.Contains("UX_User_Email") ? "An account with this email already exists." : "This username is already taken."));
            }
            catch (SqlException) { form.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { form.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(form, AdminFormView.PanelWidth);
        form.SetOffline(!await IsOnlineAsync());
    }

    private IReadOnlyList<MenuEntry> AdminMenuFor(User admin)
    {
        int clinicId = admin.ClinicId ?? 0;
        var entries = new List<MenuEntry>
        {
            new("Reset password", "key-round", () => _ = ConfirmResetPasswordAsync(admin))
        };
        if (admin.IsActive)
            entries.Add(new MenuEntry("Deactivate", "user-x", () =>
            {
                _pendingConfirm = async _ => await RunDialogAsync(() => SetAdminActiveAsync(admin, false), clinicId);
                _ = _host.Dialog.ShowConfirmAsync("Deactivate " + admin.FullName + "?", "They won't be able to sign in until activated again.",
                    "Deactivate", danger: true, icon: "user-x");
            }, Danger: true));
        else
            entries.Add(new MenuEntry("Activate", "user-check", () =>
            {
                _pendingConfirm = async _ => await RunDialogAsync(() => SetAdminActiveAsync(admin, true), clinicId);
                _ = _host.Dialog.ShowConfirmAsync("Activate " + admin.FullName + "?", "They can sign in once the clinic is active.",
                    "Activate", danger: false, icon: "user-check");
            }));
        return entries;
    }

    /// <summary>The Reset password panel: generate a temporary password, shown once with Copy.</summary>
    private async Task ConfirmResetPasswordAsync(User admin)
    {
        var view = new ResetPasswordView(admin);
        view.DoneRequested += async (s, e) => await _host.Panel.HideAsync();
        view.GenerateRequested += async (s, e) =>
        {
            view.SetBusy(true);
            try
            {
                var (result, password) = await ResetAdminPasswordAsync(admin);
                if (!result.Ok) view.ShowMessage(result.Message);
                else view.ShowPassword(password);
            }
            catch (SqlException) { view.ShowMessage(DatabaseError); }
            finally { view.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(view, ResetPasswordView.PanelWidth);
    }

    // Devices -------------------------------------------------------------------------------------------------------

    private async Task ConfirmDeviceToggleAsync(ClinicDevice device)
    {
        bool turnOn = !device.IsActive;
        _pendingConfirm = async _ => await RunDialogAsync(() => SetDeviceActiveAsync(device, turnOn), device.ClinicId);
        if (turnOn)
            await _host.Dialog.ShowConfirmAsync("Activate " + device.DeviceName + "?", "It will be able to sync and log in again.", "Activate", danger: false, icon: "monitor");
        else
            await _host.Dialog.ShowConfirmAsync("Deactivate " + device.DeviceName + "?", "It won't be able to sync or log in again.", "Deactivate", danger: true, icon: "monitor");
        _host.Dialog.SetBusy(false, offline: !await IsOnlineAsync());
    }

    // Dialog plumbing -----------------------------------------------------------------------------------------------

    /// <summary>Runs a dialog action; on success closes the dialog (and the panel), shows a toast and refreshes.</summary>
    private async Task RunDialogAsync(Func<Task<OperationResult>> action, int clinicId, bool closePanel = false)
    {
        _host.Dialog.SetBusy(true);
        try
        {
            var result = await action();
            if (!result.Ok)
            {
                _host.Dialog.SetBusy(false);
                _host.Dialog.ShowResult(result);
                return;
            }
            await CloseDialogAsync();
            if (closePanel) await _host.Panel.HideAsync();
            _toast.Show(result.Message);
            await AfterChangeAsync(clinicId);
        }
        catch (SqlException)
        {
            _host.Dialog.SetBusy(false);
            _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError));
        }
    }

    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync(IDictionary<string, string> values)
    {
        var action = _pendingConfirm;
        if (action == null) { await CloseDialogAsync(); return; }
        await action(values ?? new Dictionary<string, string>());
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }

    // Validation ----------------------------------------------------------------------------------------------------

    private static Dictionary<string, string> ValidateClinic(ClinicFormData d, bool adding)
    {
        var errors = new Dictionary<string, string>();
        void Required(string field, string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = "Required";
            else if (value.Trim().Length > max) errors[field] = "Use " + max + " characters or fewer.";
        }

        Required(ClinicFormView.NameField, d.ClinicName, 100);
        Required(ClinicFormView.AddressField, d.Address, 255);
        Required(ClinicFormView.ContactField, d.ContactNo, 20);
        Required(ClinicFormView.EmailField, d.Email, 100);
        if (!errors.ContainsKey(ClinicFormView.ContactField) && !Validator.IsPhContact(d.ContactNo))
            errors[ClinicFormView.ContactField] = "Enter a valid contact number";
        if (!errors.ContainsKey(ClinicFormView.EmailField) && !Validator.IsEmail(d.Email))
            errors[ClinicFormView.EmailField] = "Enter a valid email address";

        if (adding)
        {
            foreach (var (k, v) in ValidateAdmin(d.AdminFirstName, d.AdminLastName, d.AdminUsername, d.AdminEmail, d.Password, d.ConfirmPassword,
                         ClinicFormView.AdminFirstField, ClinicFormView.AdminLastField, ClinicFormView.AdminUsernameField,
                         ClinicFormView.AdminEmailField, ClinicFormView.PasswordField, ClinicFormView.ConfirmField))
                errors[k] = v;
        }
        return errors;
    }

    private static Dictionary<string, string> ValidateAdmin(string first, string last, string username, string email, string password, string confirm,
        string firstField, string lastField, string usernameField, string emailField, string passwordField, string confirmField)
    {
        var errors = new Dictionary<string, string>();
        void Required(string field, string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = "Required";
            else if (value.Trim().Length > max) errors[field] = "Use " + max + " characters or fewer.";
        }

        Required(firstField, first, 50);
        Required(lastField, last, 50);
        Required(usernameField, username, 50);
        Required(emailField, email, 100);
        if (!errors.ContainsKey(usernameField) && !Validator.IsUsername(username))
            errors[usernameField] = "Use letters, numbers, dots, dashes or underscores (no spaces).";
        if (!errors.ContainsKey(emailField) && !Validator.IsEmail(email))
            errors[emailField] = "Enter a valid email address";
        if (Validator.PasswordProblem(password) is string problem) errors[passwordField] = problem;
        if (string.IsNullOrEmpty(confirm)) errors[confirmField] = "Required";
        else if (confirm != password) errors[confirmField] = "Passwords don't match";
        return errors;
    }

    private static OperationResult ReasonRequired() => Field(ReasonField, "Please enter a reason.");

    private static OperationResult Field(string field, string message) =>
        OperationResult.Invalid(new Dictionary<string, string> { [field] = message });
}
