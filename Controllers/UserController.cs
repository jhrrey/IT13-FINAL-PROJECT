using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Data;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Admin;
using VetCare.Views.Shared;
using VetCare.Views.SuperAdmin;
using VetCare.Views.Controls;

namespace VetCare.Controllers;

/// <summary>
/// User Management ("Users & roles" / "Clinic admin accounts").
/// Enforces the rules in Tasks/VetCare_User_Management_Module.md, section 4:
///  1. only Super Admin and Administrator can open it;
///  2. Administrators only see and manage users of their own clinic;
///  3. Super Admin creates Administrators; Administrators create Veterinarians and Cashier/Staff;
///  4. username and email are unique;
///  5. passwords: 8+ characters with a letter and a number, stored hashed;
///  6. users are deactivated, never deleted;
///  7. you can't deactivate yourself or change your own role;
///  8. a clinic keeps at least one active Administrator;
///  9. inactive users can't sign in (AuthController);
/// 10. creating users and resetting passwords need a connection to the server.
/// </summary>
public sealed partial class UserController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly ClinicRepository _clinics;

    private UsersManagementView _view;
    private IReadOnlyList<Clinic> _clinicList = Array.Empty<Clinic>();
    private int _page = 1;
    private (UserAction Action, User User)? _pendingDialog;

    public UserController(UserSession session, UserRepository users, RoleRepository roles, ClinicRepository clinics)
    {
        _session = session;
        _users = users;
        _roles = roles;
        _clinics = clinics;
    }

    // Rules -------------------------------------------------------------------------------------

    /// <summary>Rule 1.</summary>
    public bool CanOpen => _session.IsSuperAdmin || _session.IsAdministrator;

    /// <summary>Rule 3: the roles the signed-in user may create or edit.</summary>
    private IReadOnlyList<int> ManageableRoleIds =>
        _session.IsSuperAdmin ? new[] { RoleIds.Administrator }
        : _session.IsAdministrator ? new[] { RoleIds.Veterinarian, RoleIds.CashierStaff }
        : Array.Empty<int>();

    /// <summary>The roles shown in the list (Administrators also see their fellow Administrators, read-only).</summary>
    private IReadOnlyList<int> VisibleRoleIds =>
        _session.IsSuperAdmin ? new[] { RoleIds.Administrator }
        : _session.IsAdministrator ? new[] { RoleIds.Administrator, RoleIds.Veterinarian, RoleIds.CashierStaff }
        : Array.Empty<int>();

    private static readonly int[] AllRoleIds =
        { RoleIds.SuperAdmin, RoleIds.Administrator, RoleIds.Veterinarian, RoleIds.CashierStaff };

    /// <summary>Rule 2: Administrators are always limited to their own clinic.</summary>
    private int? ScopeClinic(int? requested) => _session.IsAdministrator ? _session.ClinicId : requested;

    private bool InScope(User user) => _session.IsSuperAdmin || (user.ClinicId != null && user.ClinicId == _session.ClinicId);

    private bool CanSee(User user) => VisibleRoleIds.Contains(user.RoleId) && InScope(user);

    private bool CanManage(User user) => ManageableRoleIds.Contains(user.RoleId) && InScope(user);

    private bool IsSelf(User user) => user.UserId == _session.UserId;

    private IReadOnlyList<UserAction> ActionsFor(User user)
    {
        var actions = new List<UserAction> { UserAction.View };
        if (!CanManage(user)) return actions;

        actions.Add(UserAction.Edit);
        actions.Add(UserAction.ResetPassword);
        if (!IsSelf(user)) actions.Add(user.IsActive ? UserAction.Deactivate : UserAction.Activate);
        return actions;
    }

    // Data methods (section 6 of the module document) ------------------------------------------

    public Task<PagedResult<User>> GetUsersAsync(string search, int? roleId, string status, int? clinicId, int page = 1)
    {
        if (!CanOpen) return Task.FromResult(new PagedResult<User>(Array.Empty<User>(), 0, 1, 10));

        return _users.SearchAsync(new UserQuery
        {
            Search = search,
            RoleId = roleId != null && VisibleRoleIds.Contains(roleId.Value) ? roleId : null,
            Status = status,
            ClinicId = ScopeClinic(clinicId),
            VisibleRoleIds = VisibleRoleIds,
            Page = page,
            PageSize = 10
        });
    }

    /// <summary>The user, or null if they don't exist or the signed-in user may not see them.</summary>
    public async Task<User> GetUserByIdAsync(int userId)
    {
        if (!CanOpen) return null;
        var user = await _users.GetByIdAsync(userId);
        return user != null && CanSee(user) ? user : null;
    }

    /// <summary>The roles the signed-in user may assign (rule 3).</summary>
    public async Task<IReadOnlyList<Role>> GetRolesAsync() =>
        (await _roles.GetAllAsync()).Where(r => ManageableRoleIds.Contains(r.RoleId)).ToList();

    public async Task<OperationResult> AddUserAsync(User user, string password)
    {
        if (!CanOpen) return OperationResult.Fail("You don’t have access to user management.");
        if (!ManageableRoleIds.Contains(user.RoleId))
            return Field(UserFormData.RoleField, "You can’t create accounts with this role.");

        if (_session.IsAdministrator) user.ClinicId = _session.ClinicId;              // rule 2
        else if (user.ClinicId == null || _clinicList.All(c => c.ClinicId != user.ClinicId))
            return Field(UserFormData.ClinicField, "Choose the clinic for this Administrator.");

        if (Validator.PasswordProblem(password) is string problem) return Field(UserFormData.PasswordField, problem);

        var duplicate = await DuplicateErrorsAsync(user.Username, user.Email, excludeUserId: null);
        if (duplicate.Count > 0) return OperationResult.Invalid(duplicate);

        user.PasswordHash = PasswordHasher.Hash(password);
        user.MustChangePassword = SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset);   // temporary password (System settings)
        user.SyncStatus = SyncStatuses.Synced;
        user.Status ??= AccountStatus.Active;

        try
        {
            await _users.InsertAsync(user);
            return OperationResult.Success(user.FullName + " was added.");
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            return OperationResult.Invalid(DuplicateFromIndex(ex));
        }
    }

    public async Task<OperationResult> UpdateUserAsync(User user)
    {
        var existing = await GetUserByIdAsync(user.UserId);
        if (existing == null || !CanManage(existing)) return OperationResult.Fail("You can’t edit this user.");

        var errors = new Dictionary<string, string>();

        // Rule 7: not your own role or status.
        if (IsSelf(existing))
        {
            if (user.RoleId != existing.RoleId) errors[UserFormData.RoleField] = "You can’t change your own role.";
            if (user.Status != AccountStatus.Active) return OperationResult.Fail("You can’t deactivate your own account.");
        }

        if (!ManageableRoleIds.Contains(user.RoleId)) errors[UserFormData.RoleField] = "You can’t assign this role.";

        if (_session.IsAdministrator) user.ClinicId = _session.ClinicId;              // rule 2
        else if (user.ClinicId == null || _clinicList.All(c => c.ClinicId != user.ClinicId))
            errors[UserFormData.ClinicField] = "Choose the clinic for this Administrator.";

        if (errors.Count > 0) return OperationResult.Invalid(errors);

        // Rule 8: keep one active Administrator per clinic.
        bool leavesAdminRole = existing.RoleId == RoleIds.Administrator && existing.IsActive &&
            (user.Status != AccountStatus.Active || user.RoleId != RoleIds.Administrator || user.ClinicId != existing.ClinicId);
        if (leavesAdminRole && await IsLastActiveAdministratorAsync(existing))
            return OperationResult.Fail(LastAdminMessage(existing));

        var duplicate = await DuplicateErrorsAsync(user.Username, user.Email, user.UserId);
        if (duplicate.Count > 0) return OperationResult.Invalid(duplicate);

        try
        {
            await _users.UpdateAsync(user);
            return OperationResult.Success(user.FullName + " was updated.");
        }
        catch (SqlException ex) when (IsDuplicateKey(ex))
        {
            return OperationResult.Invalid(DuplicateFromIndex(ex));
        }
    }

    public async Task<OperationResult> ResetPasswordAsync(int userId, string newPassword)
    {
        var existing = await GetUserByIdAsync(userId);
        if (existing == null || !CanManage(existing)) return OperationResult.Fail("You can’t reset this user’s password.");
        if (Validator.PasswordProblem(newPassword) is string problem) return Field(UserFormData.PasswordField, problem);

        await _users.UpdatePasswordAsync(userId, PasswordHasher.Hash(newPassword), mustChange: userId != _session.UserId && SettingsCache.GetBool(SettingKeys.ForceChangeAfterReset));
        return OperationResult.Success("Password reset for " + existing.FullName + ".");
    }

    public async Task<OperationResult> SetStatusAsync(int userId, bool isActive)
    {
        var existing = await GetUserByIdAsync(userId);
        if (existing == null || !CanManage(existing)) return OperationResult.Fail("You can’t change this user’s status.");
        if (!isActive && IsSelf(existing)) return OperationResult.Fail("You can’t deactivate your own account.");   // rule 7

        if (!isActive && existing.RoleId == RoleIds.Administrator && existing.IsActive &&
            await IsLastActiveAdministratorAsync(existing))                                                          // rule 8
            return OperationResult.Fail(LastAdminMessage(existing));

        await _users.SetStatusAsync(userId, isActive ? AccountStatus.Active : AccountStatus.Inactive);              // rule 6
        return OperationResult.Success(existing.FullName + (isActive ? " was activated." : " was deactivated."));
    }

    /// <summary>Rule 10: true when the central database can be reached.</summary>
    public async Task<bool> IsOnlineAsync()
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

    // View ----------------------------------------------------------------------------------------

    /// <summary>The page for the signed-in role: Clinic admin accounts (Super Admin) or Users & roles (Administrator).</summary>
    /// <summary>Dashboard shortcut: "add" opens the Add user panel once the page is shown.</summary>
    public string StartIntent { get; set; }

    public View CreateView()
    {
        if (!CanOpen)
            return new Label
            {
                Text = "Only the Super Admin and clinic Administrators can manage user accounts.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        // Super Admin: the Clinic admins page (UserController.ClinicAdmins.cs).
        if (_session.IsSuperAdmin) return CreateClinicAdminsView();

        var staffPage = new StaffAccountsPage();
        _view = staffPage.Management;
        View page = staffPage;

        _view.Configure(PageSetup());
        _view.AddRequested += async (s, e) => await OpenFormAsync(null);
        _view.FiltersChanged += async (s, e) => { _page = 1; await RefreshAsync(); };
        _view.PageRequested += async (s, page) => { _page = page; await RefreshAsync(); };
        _view.ActionRequested += async (s, e) => await HandleActionAsync(e.User, e.Action);
        _view.Panel.DismissRequested += async (s, e) => await _view.Panel.HideAsync();
        _view.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _view.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync(e.Values);

        _ = InitializeAsync();
        if (StartIntent == "add") page.Dispatcher.Dispatch(async () => { await Task.Delay(250); await OpenFormAsync(null); });
        return page;
    }

    private UsersPageSetup PageSetup() => _session.IsSuperAdmin
        ? new UsersPageSetup
        {
            Icon = "user-cog",
            Title = "Clinic admin accounts",
            Subtitle = "Create and manage the Administrator account of each clinic.",
            ShowClinic = true,
            RoleOptions = new[] { (RoleIds.Administrator, Roles.Administrator) },
            ClinicOptions = _clinicList.Select(c => (c.ClinicId, c.ClinicName)).ToList(),
            InitialClinicId = _session.ClinicId,
            FourthCardRoleId = RoleIds.Administrator,
            FourthCardLabel = "Administrators",
            FourthCardIcon = "user-cog"
        }
        : new UsersPageSetup
        {
            Icon = "users",
            Title = "Users & roles",
            Subtitle = "Manage who can access VetCare and what they can do.",
            ShowClinic = false,
            RoleOptions = VisibleRoleIds.Select(id => (id, RoleIds.NameOf(id))).ToList()
        };

    private async Task InitializeAsync()
    {
        _view.ShowLoading();
        try
        {
            if (_session.IsSuperAdmin)
            {
                _clinicList = await _clinics.GetAllAsync();
                _view.Configure(PageSetup());
            }
        }
        catch (SqlException)
        {
            _view.ShowError(DatabaseError);
            return;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var filter = _view.CurrentFilter;
        try
        {
            int? clinic = ScopeClinic(filter.ClinicId);
            var summary = await _users.GetSummaryAsync(clinic, VisibleRoleIds);
            var page = await GetUsersAsync(filter.Search, filter.RoleId, filter.Status, filter.ClinicId, _page);

            if (page.Items.Count == 0 && _page > 1)
            {
                _page--;
                page = await GetUsersAsync(filter.Search, filter.RoleId, filter.Status, filter.ClinicId, _page);
            }

            _view.ShowSummary(summary);
            _view.ShowUsers(page, ActionsFor, filter.IsActive);

            var roleCounts = await _users.GetSummaryAsync(clinic, AllRoleIds);
            _view.ShowRoleCounts(roleCounts.ByRole);
        }
        catch (SqlException)
        {
            _view.ShowError(DatabaseError);
        }
    }

    private async Task HandleActionAsync(User user, UserAction action)
    {
        switch (action)
        {
            case UserAction.View:
                await ShowDetailsAsync(user);
                break;
            case UserAction.Edit:
                await OpenFormAsync(user);
                break;
            case UserAction.ResetPassword:
                _pendingDialog = (action, user);
                await _view.Dialog.ShowResetPasswordAsync(user.FullName);
                _view.Dialog.SetBusy(false, offline: !await IsOnlineAsync());
                break;
            case UserAction.Deactivate:
                _pendingDialog = (action, user);
                await _view.Dialog.ShowConfirmAsync(
                    "Deactivate " + user.FullName + "?",
                    "They won’t be able to log in, but their records will stay.",
                    "Deactivate", danger: true);
                break;
            case UserAction.Activate:
                await ApplyAsync(() => SetStatusAsync(user.UserId, true));
                break;
        }
    }

    private async Task ShowDetailsAsync(User user)
    {
        try
        {
            var fresh = await GetUserByIdAsync(user.UserId) ?? user;
            bool canManage = CanManage(fresh);
            string reason = canManage ? null
                : fresh.RoleId == RoleIds.Administrator ? "Administrator accounts are managed by the Super Admin." : null;

            var details = new UserDetailsView(fresh, _session.IsSuperAdmin, canManage, canChangeStatus: !IsSelf(fresh), reason);
            details.CloseRequested += async (s, e) => await _view.Panel.HideAsync();
            details.EditRequested += async (s, e) => await OpenFormAsync(fresh);
            details.ResetPasswordRequested += async (s, e) => await HandleActionAsync(fresh, UserAction.ResetPassword);
            details.StatusChangeRequested += async (s, e) =>
                await HandleActionAsync(fresh, fresh.IsActive ? UserAction.Deactivate : UserAction.Activate);

            await _view.Panel.ShowAsync(details);
        }
        catch (SqlException)
        {
            _view.ShowError(DatabaseError);
        }
    }

    private async Task OpenFormAsync(User user)
    {
        try
        {
            if (user != null)
            {
                user = await GetUserByIdAsync(user.UserId);
                if (user == null || !CanManage(user)) return;
            }

            var roles = await GetRolesAsync();
            if (_session.IsSuperAdmin && _clinicList.Count == 0) _clinicList = await _clinics.GetAllAsync();

            var setup = new UserFormSetup
            {
                IsNew = user == null,
                Roles = roles.Select(r => (r.RoleId, r.RoleName)).ToList(),
                Clinics = _session.IsSuperAdmin ? _clinicList.Select(c => (c.ClinicId, c.ClinicName)).ToList() : null,
                Values = user == null
                    ? new UserFormData { ClinicId = _session.IsSuperAdmin ? _view.CurrentFilter.ClinicId ?? _session.ClinicId : _session.ClinicId }
                    : new UserFormData
                    {
                        UserId = user.UserId,
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Username = user.Username,
                        Email = user.Email,
                        ContactNo = user.ContactNo,
                        RoleId = user.RoleId,
                        ClinicId = user.ClinicId,
                        IsActive = user.IsActive
                    },
                RoleLocked = user != null && IsSelf(user),
                StatusLocked = user != null && IsSelf(user)
            };

            var form = new UserFormView(setup);
            form.CancelRequested += async (s, e) => await _view.Panel.HideAsync();
            form.SaveRequested += async (s, e) => await SaveFormAsync(form);

            await _view.Panel.ShowAsync(form);
            if (setup.IsNew) form.SetOffline(!await IsOnlineAsync());   // rule 10
        }
        catch (SqlException)
        {
            _view.ShowError(DatabaseError);
        }
    }

    private async Task SaveFormAsync(UserFormView form)
    {
        var data = form.Read();
        var errors = Validate(data);
        if (errors.Count > 0)
        {
            form.ShowResult(OperationResult.Invalid(errors));
            return;
        }

        var (first, last) = (data.FirstName.Trim(), data.LastName.Trim());
        var user = new User
        {
            UserId = data.UserId,
            FirstName = first,
            LastName = last,
            Username = data.Username.Trim(),
            Email = data.Email.Trim().ToLowerInvariant(),
            ContactNo = string.IsNullOrWhiteSpace(data.ContactNo) ? null : data.ContactNo.Trim(),
            RoleId = data.RoleId ?? 0,
            ClinicId = data.ClinicId,
            Status = data.IsActive ? AccountStatus.Active : AccountStatus.Inactive
        };

        form.SetBusy(true);
        try
        {
            var result = data.IsNew ? await AddUserAsync(user, data.Password) : await UpdateUserAsync(user);
            if (!result.Ok)
            {
                form.ShowResult(result);
                return;
            }

            await _view.Panel.HideAsync();
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
    }

    /// <summary>Field checks for the Add / Edit form (required, formats, lengths, password rule).</summary>
    private static Dictionary<string, string> Validate(UserFormData data)
    {
        var errors = new Dictionary<string, string>();

        void Required(string field, string value, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = label + " is required.";
            else if (value.Trim().Length > max) errors[field] = "Use " + max + " characters or fewer.";
        }

        Required(UserFormData.FirstNameField, data.FirstName, "First name", 50);
        Required(UserFormData.LastNameField, data.LastName, "Last name", 50);
        Required(UserFormData.UsernameField, data.Username, "Username", 50);
        Required(UserFormData.EmailField, data.Email, "Email", 100);

        if (!errors.ContainsKey(UserFormData.UsernameField) && !Validator.IsUsername(data.Username))
            errors[UserFormData.UsernameField] = "Use letters, numbers, dots, dashes or underscores (no spaces).";
        if (!errors.ContainsKey(UserFormData.EmailField) && !Validator.IsEmail(data.Email))
            errors[UserFormData.EmailField] = "Please enter a valid email address.";
        if (!string.IsNullOrWhiteSpace(data.ContactNo) && (data.ContactNo.Trim().Length > 20 || !Validator.IsPhone(data.ContactNo)))
            errors[UserFormData.ContactNoField] = "Please enter a valid contact number.";
        if (data.RoleId == null) errors[UserFormData.RoleField] = "Choose a role.";

        if (data.IsNew)
        {
            if (Validator.PasswordProblem(data.Password) is string problem) errors[UserFormData.PasswordField] = problem;
            if (string.IsNullOrEmpty(data.ConfirmPassword)) errors[UserFormData.ConfirmPasswordField] = "Please confirm the password.";
            else if (data.ConfirmPassword != data.Password) errors[UserFormData.ConfirmPasswordField] = "Passwords don’t match.";
        }

        return errors;
    }

    private async Task ConfirmDialogAsync(IDictionary<string, string> values)
    {
        // No pending action: the dialog was only showing a message, so OK just closes it.
        if (_pendingDialog is not var (action, user))
        {
            await CloseDialogAsync();
            return;
        }

        if (action == UserAction.ResetPassword)
        {
            values.TryGetValue(UserFormData.PasswordField, out var password);
            values.TryGetValue(UserFormData.ConfirmPasswordField, out var confirm);

            var errors = new Dictionary<string, string>();
            if (Validator.PasswordProblem(password) is string problem) errors[UserFormData.PasswordField] = problem;
            if (string.IsNullOrEmpty(confirm)) errors[UserFormData.ConfirmPasswordField] = "Please confirm the password.";
            else if (confirm != password) errors[UserFormData.ConfirmPasswordField] = "Passwords don’t match.";
            if (errors.Count > 0)
            {
                _view.Dialog.ShowResult(OperationResult.Invalid(errors));
                return;
            }

            await RunDialogAsync(() => ResetPasswordAsync(user.UserId, password));
        }
        else if (action == UserAction.Deactivate)
        {
            await RunDialogAsync(() => SetStatusAsync(user.UserId, false));
        }
    }

    private async Task RunDialogAsync(Func<Task<OperationResult>> operation)
    {
        _view.Dialog.SetBusy(true);
        try
        {
            var result = await operation();
            if (!result.Ok)
            {
                _view.Dialog.ShowResult(result);
                return;
            }

            await CloseDialogAsync();
            await _view.Panel.HideAsync();
            await RefreshAsync();
        }
        catch (SqlException)
        {
            _view.Dialog.ShowResult(OperationResult.Fail(DatabaseError));
        }
        finally
        {
            _view.Dialog.SetBusy(false);
        }
    }

    private async Task CloseDialogAsync()
    {
        _pendingDialog = null;
        await _view.Dialog.HideAsync();
    }

    private async Task ApplyAsync(Func<Task<OperationResult>> operation)
    {
        try
        {
            var result = await operation();
            if (!result.Ok)
            {
                // Show the reason in the dialog so it is visible wherever the action came from.
                await _view.Dialog.ShowConfirmAsync("Can’t change this account", result.Message, "OK", danger: false, icon: "circle-alert");
                _pendingDialog = null;
                return;
            }

            await _view.Panel.HideAsync();
            await RefreshAsync();
        }
        catch (SqlException)
        {
            _view.ShowError(DatabaseError);
        }
    }

    // Helpers ---------------------------------------------------------------------------------

    private async Task<Dictionary<string, string>> DuplicateErrorsAsync(string username, string email, int? excludeUserId)
    {
        var errors = new Dictionary<string, string>();
        if (await _users.UsernameExistsAsync(username, excludeUserId)) errors[UserFormData.UsernameField] = "This username is already taken.";
        if (await _users.EmailExistsAsync(email, excludeUserId)) errors[UserFormData.EmailField] = "An account with this email already exists.";
        return errors;
    }

    private async Task<bool> IsLastActiveAdministratorAsync(User admin) =>
        admin.ClinicId != null && await _users.CountActiveAdministratorsAsync(admin.ClinicId.Value, admin.UserId) == 0;

    private static string LastAdminMessage(User admin) =>
        (admin.ClinicName ?? "This clinic") + " must keep at least one active Administrator. Add or activate another Administrator first.";

    private static OperationResult Field(string field, string message) =>
        OperationResult.Invalid(new Dictionary<string, string> { [field] = message });

    /// <summary>2601/2627: a unique index rejected the row (someone took the name in the meantime).</summary>
    private static bool IsDuplicateKey(SqlException ex) => ex.Number is 2601 or 2627;

    private static Dictionary<string, string> DuplicateFromIndex(SqlException ex) =>
        ex.Message.Contains("UX_User_Email")
            ? new Dictionary<string, string> { [UserFormData.EmailField] = "An account with this email already exists." }
            : new Dictionary<string, string> { [UserFormData.UsernameField] = "This username is already taken." };
}
