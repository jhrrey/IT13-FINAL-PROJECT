using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.SuperAdmin;

namespace VetCare.Controllers;

/// <summary>
/// Clinic admins (Tasks/VetCare_Clinic_Admin_Accounts_Module.md, section 4), the Super Admin side of UserController:
///  1. Super Admin only;  2. Administrator accounts only;  3. an admin can't move to another clinic;
///  4. admins are added to Active or Pending clinics only;  5. unique username and email;  6. password rule, hashed;
///  7. every Active clinic keeps at least one active admin;  8. vets and staff aren't affected;
///  9. admins of Pending / Inactive clinics can't sign in (AuthController);  10. deactivated, never deleted;
/// 11. no signing in as an admin;  12. adding an admin and resetting a password need a connection.
/// </summary>
public sealed partial class UserController
{
    private ModuleHostView _adminHost;
    private Toast _adminToast;
    private ClinicAdminsPage _adminPage;
    private Dictionary<int, int> _activeAdminsByClinic = new();
    private Func<IDictionary<string, string>, Task> _adminConfirm;

    // Data (section 6) -------------------------------------------------------------------------------------------

    /// <summary>Every Administrator matching the search, clinic and (calculated) status.</summary>
    public async Task<IReadOnlyList<User>> GetClinicAdminsAsync(string search, int? clinicId, string displayStatus)
    {
        if (!_session.IsSuperAdmin) return Array.Empty<User>();
        var page = await _users.SearchAsync(new UserQuery
        {
            Search = search,
            RoleId = RoleIds.Administrator,
            ClinicId = clinicId,
            VisibleRoleIds = new[] { RoleIds.Administrator },
            PageSize = 2000
        });
        return page.Items.Where(u => displayStatus == null || u.DisplayStatus == displayStatus).ToList();
    }

    public Task<(int Total, int Active, int Inactive, int ClinicsWithoutAdmin)> GetClinicAdminSummaryAsync() => _users.GetAdminSummaryAsync();

    public Task<IReadOnlyList<Clinic>> GetClinicsWithoutActiveAdminAsync() => _users.GetClinicsWithoutActiveAdminAsync();

    public Task<int> CountActiveAdminsAsync(int clinicId) => _users.CountActiveAdministratorsAsync(clinicId, 0);

    /// <summary>Rule 4: the clinics an admin can be added to (Active and Pending).</summary>
    public async Task<IReadOnlyList<Clinic>> GetAssignableClinicsAsync() =>
        (await _clinics.GetAllAsync()).Where(c => c.Status is AccountStatus.Active or AccountStatus.Pending).ToList();

    public async Task<OperationResult> AddClinicAdminAsync(UserFormData data)
    {
        if (!_session.IsSuperAdmin) return OperationResult.Fail("Only the Super Admin can add clinic admins.");
        data.RoleId = RoleIds.Administrator;                                                                // rule 2
        var errors = Validate(data);
        _clinicList = await GetAssignableClinicsAsync();
        var clinic = _clinicList.FirstOrDefault(c => c.ClinicId == data.ClinicId);
        if (clinic == null) errors[UserFormData.ClinicField] = "Choose an active or pending clinic.";       // rule 4
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        if (!await IsOnlineAsync()) return OperationResult.Fail("Connect to the internet to create or change admin accounts."); // rule 12

        var user = new User
        {
            ClinicId = clinic.ClinicId,
            RoleId = RoleIds.Administrator,
            FirstName = data.FirstName.Trim(),
            LastName = data.LastName.Trim(),
            Username = data.Username.Trim(),
            Email = data.Email.Trim().ToLowerInvariant(),
            ContactNo = string.IsNullOrWhiteSpace(data.ContactNo) ? null : data.ContactNo.Trim(),
            // A pending clinic's admins are turned on when the clinic is approved.
            Status = clinic.Status == AccountStatus.Pending ? AccountStatus.Inactive : data.IsActive ? AccountStatus.Active : AccountStatus.Inactive
        };
        var result = await AddUserAsync(user, data.Password);                                               // rules 5, 6
        return result.Ok ? OperationResult.Success(user.FullName + " was added to " + clinic.ClinicName + ".") : result;
    }

    public async Task<OperationResult> UpdateClinicAdminAsync(UserFormData data)
    {
        if (!_session.IsSuperAdmin) return OperationResult.Fail("Only the Super Admin can edit clinic admins.");
        var existing = await _users.GetByIdAsync(data.UserId);
        if (existing == null || existing.RoleId != RoleIds.Administrator) return OperationResult.Fail("This admin no longer exists.");
        if (data.ClinicId != null && data.ClinicId != existing.ClinicId)                                    // rule 3
            return Field(UserFormData.ClinicField, "Admins can't move to another clinic. Add a new admin there instead.");

        data.RoleId = RoleIds.Administrator;
        var errors = Validate(data);
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        bool deactivating = existing.IsActive && !data.IsActive;
        if (deactivating && await IsOnlyActiveAdminAsync(existing)) return OperationResult.Fail(OnlyAdminMessage(existing)); // rule 7

        var duplicate = await DuplicateErrorsAsync(data.Username.Trim(), data.Email.Trim().ToLowerInvariant(), existing.UserId);
        if (duplicate.Count > 0) return OperationResult.Invalid(duplicate);

        existing.FirstName = data.FirstName.Trim();
        existing.LastName = data.LastName.Trim();
        existing.Username = data.Username.Trim();
        existing.Email = data.Email.Trim().ToLowerInvariant();
        existing.ContactNo = string.IsNullOrWhiteSpace(data.ContactNo) ? null : data.ContactNo.Trim();
        existing.Status = data.IsActive ? AccountStatus.Active : AccountStatus.Inactive;
        try
        {
            await _users.UpdateAsync(existing);
            return OperationResult.Success(existing.FullName + " was updated.");
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            return OperationResult.Invalid(DuplicateFromIndex(ex));
        }
    }

    /// <summary>Generates a temporary password (shown once); the admin sets their own at the next sign-in.</summary>
    public async Task<(OperationResult Result, string Password)> ResetClinicAdminPasswordAsync(int userId)
    {
        if (!_session.IsSuperAdmin) return (OperationResult.Fail("Only the Super Admin can reset admin passwords."), null);
        var admin = await _users.GetByIdAsync(userId);
        if (admin == null || admin.RoleId != RoleIds.Administrator) return (OperationResult.Fail("This admin no longer exists."), null);
        if (!await IsOnlineAsync()) return (OperationResult.Fail("Connect to the internet to reset a password."), null); // rule 12

        string password = PasswordGenerator.Generate();
        await _users.UpdatePasswordAsync(userId, PasswordHasher.Hash(password), mustChange: SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset));
        return (OperationResult.Success("Password reset for " + admin.FullName + "."), password);
    }

    public async Task<OperationResult> SetClinicAdminActiveAsync(User admin, bool active)
    {
        if (!_session.IsSuperAdmin || admin.RoleId != RoleIds.Administrator) return OperationResult.Fail("You can’t change this account.");
        var fresh = await _users.GetByIdAsync(admin.UserId);
        if (fresh == null) return OperationResult.Fail("This admin no longer exists.");
        if (!active && fresh.IsActive && await IsOnlyActiveAdminAsync(fresh)) return OperationResult.Fail(OnlyAdminMessage(fresh)); // rule 7
        await _users.SetStatusAsync(fresh.UserId, active ? AccountStatus.Active : AccountStatus.Inactive); // rules 8, 10
        return OperationResult.Success(fresh.FullName + (active ? " was activated." : " was deactivated."));
    }

    /// <summary>Rule 7: true for the only active admin of an Active clinic.</summary>
    private async Task<bool> IsOnlyActiveAdminAsync(User admin) =>
        admin.ClinicId != null && admin.IsActive && admin.ClinicStatus == AccountStatus.Active &&
        await _users.CountActiveAdministratorsAsync(admin.ClinicId.Value, admin.UserId) == 0;

    private bool IsOnlyActiveAdmin(User admin) =>
        admin.ClinicId != null && admin.IsActive && admin.ClinicStatus == AccountStatus.Active &&
        _activeAdminsByClinic.TryGetValue(admin.ClinicId.Value, out int n) && n <= 1;

    private static string OnlyAdminMessage(User admin) =>
        admin.FullName + " is the only active admin of " + (admin.ClinicName ?? "this clinic") + ". Add another admin first.";

    // View ---------------------------------------------------------------------------------------------------------

    private View CreateClinicAdminsView()
    {
        _adminHost = new ModuleHostView();
        _adminHost.Panel.DismissRequested += async (s, e) => await _adminHost.Panel.HideAsync();
        _adminHost.Dialog.CancelRequested += async (s, e) => await CloseAdminDialogAsync();
        _adminHost.Dialog.Confirmed += async (s, e) =>
        {
            var action = _adminConfirm;
            if (action == null) { await CloseAdminDialogAsync(); return; }
            await action(e.Values ?? new Dictionary<string, string>());
        };

        _adminPage = new ClinicAdminsPage();
        _adminPage.AddRequested += async (s, clinicId) => await OpenAdminFormAsync(null, clinicId);
        _adminPage.FiltersChanged += async (s, e) => await RefreshAdminsAsync();
        _adminPage.AdminSelected += async (s, a) => await ShowAdminDetailsAsync(a.UserId);
        _adminPage.MenuRequested += (s, e) => _adminHost.Menu.Show(e.Anchor, AdminMenuFor(e.Admin));
        _adminHost.ShowPage(_adminPage);
        _adminPage.ShowLoading();

        _adminToast = new Toast();
        _ = StartClinicAdminsAsync();
        return new Grid { Children = { _adminHost, _adminToast } };
    }

    private async Task StartClinicAdminsAsync()
    {
        try
        {
            _clinicList = await GetAssignableClinicsAsync();
            _adminPage.SetClinics(await _clinics.GetAllAsync());
        }
        catch (SqlException) { /* RefreshAdminsAsync shows the error */ }
        await RefreshAdminsAsync();

        // "add" or "add:{clinicId}" (e.g. from the dashboard or a clinic without an admin).
        string intent = StartIntent ?? string.Empty;
        if (intent == "add") await OpenAdminFormAsync(null, null);
        else if (intent.StartsWith("add:") && int.TryParse(intent.Substring(4), out int id)) await OpenAdminFormAsync(null, id);
    }

    private async Task RefreshAdminsAsync(bool keepPage = false)
    {
        try
        {
            var summary = await GetClinicAdminSummaryAsync();
            _adminPage.ShowSummary(summary);
            _adminPage.ShowWarning(await GetClinicsWithoutActiveAdminAsync());

            // Active-admin counts per clinic over every admin (not just the filtered ones), for rule 7.
            var all = await GetClinicAdminsAsync(null, null, null);
            _activeAdminsByClinic = all.Where(a => a.IsActive && a.ClinicId != null)
                .GroupBy(a => a.ClinicId.Value).ToDictionary(g => g.Key, g => g.Count());

            var admins = await GetClinicAdminsAsync(_adminPage.SearchText, _adminPage.ClinicFilter, _adminPage.StatusFilter);
            _adminPage.ShowAdmins(admins, IsOnlyActiveAdmin, keepPage);
        }
        catch (SqlException)
        {
            _adminPage.ShowError(DatabaseError);
        }
    }

    private IReadOnlyList<MenuEntry> AdminMenuFor(User admin)
    {
        var entries = new List<MenuEntry>
        {
            new("View", "eye", () => _ = ShowAdminDetailsAsync(admin.UserId)),
            new("Edit", "pencil", () => _ = OpenAdminFormAsync(admin, null)),
            new("Reset password", "key-round", () => _ = OpenResetPasswordAsync(admin))
        };
        if (admin.IsActive)
            entries.Add(new MenuEntry("Deactivate", "user-x", () => _ = ConfirmAdminStatusAsync(admin, false), Danger: true,
                Enabled: !IsOnlyActiveAdmin(admin), DisabledHint: "This is the clinic's only active admin"));
        else
            entries.Add(new MenuEntry("Activate", "user-check", () => _ = ConfirmAdminStatusAsync(admin, true)));
        return entries;
    }

    private async Task ShowAdminDetailsAsync(int userId)
    {
        User admin;
        try { admin = await _users.GetByIdAsync(userId); }
        catch (SqlException) { _adminPage.ShowError(DatabaseError); return; }
        if (admin == null) return;

        var view = new AdminDetailsView(admin);
        view.CloseRequested += async (s, e) => await _adminHost.Panel.HideAsync();
        view.EditRequested += async (s, e) => await OpenAdminFormAsync(admin, null);
        view.ResetPasswordRequested += async (s, e) => await OpenResetPasswordAsync(admin);
        view.StatusChangeRequested += async (s, e) => await ConfirmAdminStatusAsync(admin, !admin.IsActive);
        await _adminHost.Panel.ShowAsync(view, AdminDetailsView.PanelWidth);
    }

    private async Task OpenAdminFormAsync(User admin, int? clinicId)
    {
        try
        {
            _clinicList = await GetAssignableClinicsAsync();
            if (admin != null) admin = await _users.GetByIdAsync(admin.UserId);
        }
        catch (SqlException) { _adminPage.ShowError(DatabaseError); return; }

        var clinics = _clinicList.ToList();
        // Editing an admin of an Inactive clinic: show that clinic (locked) even though new admins can't be added there.
        if (admin?.ClinicId != null && clinics.All(c => c.ClinicId != admin.ClinicId))
            clinics.Add(new Clinic { ClinicId = admin.ClinicId.Value, ClinicName = admin.ClinicName, Status = admin.ClinicStatus });

        var setup = new AdminFormSetup
        {
            IsNew = admin == null,
            Clinics = clinics,
            ClinicLocked = admin != null,
            Values = admin == null
                ? new UserFormData { ClinicId = clinicId ?? _adminPage.ClinicFilter, IsActive = true }
                : new UserFormData
                {
                    UserId = admin.UserId,
                    ClinicId = admin.ClinicId,
                    FirstName = admin.FirstName,
                    LastName = admin.LastName,
                    Username = admin.Username,
                    Email = admin.Email,
                    ContactNo = admin.ContactNo,
                    IsActive = admin.IsActive
                }
        };
        var form = new AdminFormView(setup);
        form.CancelRequested += async (s, e) => await _adminHost.Panel.HideAsync();
        form.SaveRequested += async (s, e) =>
        {
            form.SetBusy(true);
            try
            {
                var data = form.Read();
                var result = setup.IsNew ? await AddClinicAdminAsync(data) : await UpdateClinicAdminAsync(data);
                form.ShowResult(result);
                if (!result.Ok) return;
                await _adminHost.Panel.HideAsync();
                _adminToast.Show(result.Message);
                await RefreshAdminsAsync(keepPage: !setup.IsNew);
            }
            catch (SqlException) { form.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { form.SetBusy(false); }
        };
        await _adminHost.Panel.ShowAsync(form, AdminFormView.PanelWidth);
        if (setup.IsNew) form.SetOffline(!await IsOnlineAsync());                                            // rule 12
    }

    private async Task OpenResetPasswordAsync(User admin)
    {
        var view = new ResetPasswordView(admin);
        bool generated = false;
        view.DoneRequested += async (s, e) =>
        {
            await _adminHost.Panel.HideAsync();
            if (generated) await RefreshAdminsAsync(keepPage: true);
        };
        view.GenerateRequested += async (s, e) =>
        {
            view.SetBusy(true);
            try
            {
                var (result, password) = await ResetClinicAdminPasswordAsync(admin.UserId);
                if (!result.Ok) { view.ShowMessage(result.Message); return; }
                generated = true;
                view.ShowPassword(password);
            }
            catch (SqlException) { view.ShowMessage(DatabaseError); }
            finally { view.SetBusy(false); }
        };
        await _adminHost.Panel.ShowAsync(view, ResetPasswordView.PanelWidth);
    }

    private async Task ConfirmAdminStatusAsync(User admin, bool activate)
    {
        if (!activate)
        {
            bool only;
            try { only = await IsOnlyActiveAdminAsync(await _users.GetByIdAsync(admin.UserId) ?? admin); }
            catch (SqlException) { _adminPage.ShowError(DatabaseError); return; }
            if (only)
            {
                // Blocked (rule 7): offer to add another admin for that clinic.
                int? clinicId = admin.ClinicId;
                _adminConfirm = async _ =>
                {
                    await CloseAdminDialogAsync();
                    await OpenAdminFormAsync(null, clinicId);
                };
                await _adminHost.Dialog.ShowConfirmAsync("Can’t deactivate " + admin.FullName, OnlyAdminMessage(admin),
                    "Add admin", danger: false, icon: "user-cog", showCancel: true, cancelText: "OK");
                return;
            }
        }

        _adminConfirm = async _ =>
        {
            _adminHost.Dialog.SetBusy(true);
            try
            {
                var result = await SetClinicAdminActiveAsync(admin, activate);
                if (!result.Ok) { _adminHost.Dialog.SetBusy(false); _adminHost.Dialog.ShowResult(result); return; }
                await CloseAdminDialogAsync();
                await _adminHost.Panel.HideAsync();
                _adminToast.Show(result.Message);
                await RefreshAdminsAsync(keepPage: true);
            }
            catch (SqlException)
            {
                _adminHost.Dialog.SetBusy(false);
                _adminHost.Dialog.ShowResult(OperationResult.Fail(DatabaseError));
            }
        };
        if (activate)
            await _adminHost.Dialog.ShowConfirmAsync("Activate " + admin.FullName + "?",
                "They can log in again once their clinic's computers sync.", "Activate", danger: false, icon: "user-check");
        else
            await _adminHost.Dialog.ShowConfirmAsync("Deactivate " + admin.FullName + "?",
                "They won't be able to log in. Their clinic's vets and staff are not affected.", "Deactivate", danger: true, icon: "user-x");
    }

    private async Task CloseAdminDialogAsync()
    {
        _adminConfirm = null;
        await _adminHost.Dialog.HideAsync();
    }
}
