using Microsoft.Data.SqlClient;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Helpers;
using VetCare.Views;
using VetCare.Views.Sections;

namespace VetCare.Controllers;

/// <summary>
/// Sign-in and clinic registration: connects the sign-in card and the "Create account" modal
/// to the <see cref="AuthModel"/> rules and the database repositories.
/// </summary>
public sealed class AuthController
{
    /// <summary>
    /// System settings → Registration.RequireApproval: new clinics wait for the Super Admin's review
    /// (Clinic management) before they can sign in.
    /// </summary>
    public static bool RequireClinicApproval => SettingsCache.GetBool(SettingKeys.RequireApproval);

    /// <summary>System settings → Registration.AllowSignup: show "Create account".</summary>
    public static bool AllowSignup => SettingsCache.GetBool(SettingKeys.AllowSignup);

    /// <summary>A warning to show on the sign-in card after a forced sign-out (set by the workspace).</summary>
    public static (string Title, string Message) PendingWarning { get; set; }

    private const string DatabaseError =
        "Can’t reach the VetCare database. Make sure SQL Server (SQLEXPRESS) is running, then try again.";

    private readonly AuthModel _model;
    private readonly AuthCardView _card;
    private readonly AccountModalView _accountModal;
    private readonly UserRepository _users;
    private readonly ClinicRepository _clinics;
    private User _mustChangeUser;

    public AuthController(AuthModel model, AuthCardView card, AccountModalView accountModal,
        UserRepository users, ClinicRepository clinics)
    {
        _model = model;
        _card = card;
        _accountModal = accountModal;
        _users = users;
        _clinics = clinics;
    }

    /// <summary>Raised after a successful sign-in.</summary>
    public event EventHandler<UserSession> SignedIn;

    public async void Start()
    {
        _card.ShowMode(_model.Get(AuthMode.SignIn), _model.CardTabs);

        // The "Create account" tab opens the registration modal (growing out of the tab)
        // instead of switching the card. The tab stays highlighted while the modal is open.
        _card.ModeRequested += async (s, e) =>
        {
            if (e.Mode != AuthMode.Register) return;
            _card.SetActiveTab(AuthMode.Register);
            await OpenModalAsync(AuthMode.Register, e.Origin);
        };

        // First-time setup: create the Super Admin (only offered while none exists).
        _card.SetupRequested += async (s, e) => await OpenModalAsync(AuthMode.SetupSuperAdmin, e.Origin);

        _accountModal.CloseRequested += async (s, e) => await CloseModalAsync();

        _card.SubmitRequested += async (s, e) => await SignInAsync(e.Values);
        _accountModal.SubmitRequested += async (s, e) =>
        {
            if (_accountModal.Mode == AuthMode.SetupSuperAdmin) await SetupSuperAdminAsync(e.Values);
            else if (_accountModal.Mode == AuthMode.ChangePassword) await ChangePasswordAsync(e.Values);
            else await RegisterAsync(e.Values);
        };

        await RefreshSetupLinkAsync();

        // The sign-up rules come from System settings (the last loaded values apply until they load).
        await SettingsCache.LoadGlobalAsync();
        _card.SetTabVisible(AuthMode.Register, AllowSignup);

        // Signed out because this computer or the clinic was turned off: say so on the card.
        if (PendingWarning is var (title, message) && message != null)
        {
            _card.ShowWarning(title, message, "ban");
            PendingWarning = (null, null);
        }
    }

    /// <summary>Close the modal (if open) so the sign-in card is usable.</summary>
    public Task ShowSignInAsync() => CloseModalAsync();

    public void FocusSignIn() => _card.FocusFirstInput();

    private Task OpenModalAsync(AuthMode mode, VisualElement origin)
    {
        _accountModal.ShowForm(_model.Get(mode));
        return _accountModal.OpenAsync(origin);
    }

    private async Task CloseModalAsync()
    {
        _mustChangeUser = null;   // closing the "Set your password" form cancels that sign-in
        await _accountModal.CloseAsync();
        _card.SetActiveTab(AuthMode.SignIn);
    }

    /// <summary>Shows the setup link only while the database has no Super Admin.</summary>
    private async Task RefreshSetupLinkAsync()
    {
        try
        {
            _card.ShowSetupLink(!await _users.SuperAdminExistsAsync());
        }
        catch (Exception)
        {
            _card.ShowSetupLink(false); // can't tell; sign-in will report the database problem
        }
    }

    private async Task SetupSuperAdminAsync(IDictionary<string, string> values)
    {
        const AuthMode mode = AuthMode.SetupSuperAdmin;

        var errors = _model.Validate(mode, values);
        _accountModal.ShowErrors(errors);
        _accountModal.ShowStatus(null);
        if (errors.Count > 0) return;

        string email = _model.Value(mode, values, AuthField.Email);

        _accountModal.SetBusy(true, "Creating Super Admin…");
        try
        {
            if (await _users.EmailExistsAsync(email))
            {
                _accountModal.ShowErrors(new Dictionary<string, string>
                {
                    [AuthField.Email] = "An account with this email already exists."
                });
                return;
            }

            var superAdmin = new User
            {
                RoleId = RoleIds.SuperAdmin,
                FirstName = PersonName.Split(_model.Value(mode, values, AuthField.FullName)).First,
                LastName = PersonName.Split(_model.Value(mode, values, AuthField.FullName)).Last,
                Username = await _users.MakeUniqueUsernameAsync(email.Split('@')[0]),
                PasswordHash = PasswordHasher.Hash(_model.Value(mode, values, AuthField.Password)),
                Email = email,
                Status = AccountStatus.Active
            };

            bool created = await _users.CreateFirstSuperAdminAsync(superAdmin);

            _accountModal.ClearInputs();
            await CloseModalAsync();
            await RefreshSetupLinkAsync();

            if (created)
            {
                _card.SetFieldValue(AuthField.Email, email);
                _card.ShowStatus(_model.Get(mode).SuccessMessage);
            }
            else
            {
                _card.ShowStatus("A Super Admin account already exists. Sign in with that account.");
            }
        }
        catch (SqlException)
        {
            _accountModal.ShowStatus(DatabaseError);
        }
        finally
        {
            _accountModal.SetBusy(false);
        }
    }

    private async Task SignInAsync(IDictionary<string, string> values)
    {
        var errors = _model.Validate(AuthMode.SignIn, values);
        _card.ShowErrors(errors);
        _card.ShowStatus(null);
        _card.ShowWarning(null, null);
        if (errors.Count > 0) return;

        string email = _model.Value(AuthMode.SignIn, values, AuthField.Email);
        string password = _model.Value(AuthMode.SignIn, values, AuthField.Password);

        _card.SetBusy(true, "Signing in…");
        try
        {
            var user = await _users.FindByEmailAsync(email);
            if (user == null || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                _card.ShowStatus("Incorrect email or password.");
                return;
            }

            // Blocked sign-ins get a clear warning (not the small status line used for typing mistakes).
            string problem = SignInProblem(user);
            string title = problem == null ? null : BlockTitle(user);
            string icon = "triangle-alert";
            if (problem == null && await DeviceProblemAsync(user) is string deviceProblem)
            {
                problem = deviceProblem;
                bool turnedOff = DeviceIdentity.CurrentDeviceId == null && deviceProblem.StartsWith("This computer was turned off", StringComparison.Ordinal);
                title = turnedOff ? "This computer is blocked by your administrator"
                    : deviceProblem.Contains("isn't registered") ? "This computer isn't registered yet"
                    : "This computer can't be added";
                icon = turnedOff ? "ban" : "monitor";
            }
            if (problem != null)
            {
                _card.ShowWarning(title, problem, icon);
                return;
            }

            await _users.RecordLoginAsync(user.UserId);
            await SettingsCache.LoadAsync(user.ClinicId);   // this clinic's settings (System settings)
            _card.ClearInputs();

            // A temporary password (set by an administrator) must be replaced before continuing.
            if (user.MustChangePassword)
            {
                await OpenModalAsync(AuthMode.ChangePassword, null);
                _mustChangeUser = user;
                return;
            }

            SignedIn?.Invoke(this, UserSession.SignedIn(user));
        }
        catch (SqlException)
        {
            _card.ShowStatus(DatabaseError);
        }
        finally
        {
            _card.SetBusy(false);
        }
    }

    /// <summary>
    /// Devices & Sync section 3 and rule 3: a clinic computer must be registered and active. An Administrator's first
    /// sign-in on a computer registers it (up to Sync.MaxDevicesPerClinic); Vets and Staff need that done first.
    /// Returns why the user can't sign in here, or null (and remembers the device for the sync check).
    /// </summary>
    private static async Task<string> DeviceProblemAsync(User user)
    {
        DeviceIdentity.CurrentDeviceId = null;
        if (user.ClinicId is not int clinicId) return null;   // the Super Admin isn't tied to a clinic computer

        var devices = new DeviceRepository();
        var device = await devices.FindAsync(clinicId, DeviceIdentity.MachineCode);
        if (device != null)
        {
            if (!device.IsActive)
                return "This computer was turned off for " + (user.ClinicName ?? "your clinic") + ". Ask your administrator to turn it back on.";
            DeviceIdentity.CurrentDeviceId = device.DeviceId;
            return null;
        }

        if (user.RoleId != RoleIds.Administrator)
            return "This computer isn't registered for " + (user.ClinicName ?? "your clinic") + " yet. An Administrator must sign in on it first.";

        await SettingsCache.LoadAsync(clinicId);
        int max = SettingsCache.GetInt(SettingKeys.MaxDevicesPerClinic);
        if (await devices.CountActiveAsync(clinicId) >= max)
            return (user.ClinicName ?? "Your clinic") + " already has " + max + " computers. Turn one off in Devices & sync before adding this one.";

        DeviceIdentity.CurrentDeviceId = await devices.RegisterAsync(clinicId, DeviceIdentity.ComputerName, DeviceIdentity.MachineCode, DeviceIdentity.AppVersion);
        return null;
    }

    /// <summary>The warning title for a <see cref="SignInProblem"/> (clinic first, then the account).</summary>
    private static string BlockTitle(User user)
    {
        if (user.ClinicId != null)
        {
            switch (user.ClinicStatus)
            {
                case AccountStatus.Pending: return "Your clinic is waiting for approval";
                case AccountStatus.Rejected: return "Your clinic's registration wasn't approved";
                case AccountStatus.Inactive:
                case AccountStatus.Suspended: return "Your clinic is blocked";
            }
        }
        return "Your account is blocked by your administrator";
    }

    /// <summary>Why this (correct-password) user may not sign in, or null if they may.</summary>
    private static string SignInProblem(User user)
    {
        // The clinic comes first: a pending sign-up's admin is inactive until approval and should see why.
        if (user.ClinicId != null)
        {
            string clinicProblem = user.ClinicStatus switch
            {
                AccountStatus.Rejected => "This clinic’s registration was not approved.",
                AccountStatus.Inactive => "This clinic has been deactivated. Please contact the VetCare team.",
                AccountStatus.Suspended => "This clinic is suspended. Please contact the VetCare team.",
                AccountStatus.Pending when RequireClinicApproval =>
                    "Your clinic is waiting for approval by the VetCare team.",
                _ => null
            };
            if (clinicProblem != null) return clinicProblem;
        }

        if (user.Status is AccountStatus.Inactive or AccountStatus.Suspended)
            return "This account is not active. Please contact your clinic administrator.";

        return null;
    }

    /// <summary>"Set your password": replaces a temporary password, then finishes the sign-in.</summary>
    private async Task ChangePasswordAsync(IDictionary<string, string> values)
    {
        const AuthMode mode = AuthMode.ChangePassword;
        var user = _mustChangeUser;
        if (user == null) { await CloseModalAsync(); return; }

        var errors = _model.Validate(mode, values);
        string password = _model.Value(mode, values, AuthField.Password);
        if (errors.Count == 0 && PasswordHasher.Verify(password, user.PasswordHash))
            errors[AuthField.Password] = "Choose a password different from the temporary one.";
        _accountModal.ShowErrors(errors);
        _accountModal.ShowStatus(null);
        if (errors.Count > 0) return;

        _accountModal.SetBusy(true, "Saving…");
        try
        {
            await _users.UpdatePasswordAsync(user.UserId, PasswordHasher.Hash(password), mustChange: false);
            _accountModal.ClearInputs();
            await CloseModalAsync();
            SignedIn?.Invoke(this, UserSession.SignedIn(user));
        }
        catch (SqlException)
        {
            _accountModal.ShowStatus(DatabaseError);
        }
        finally
        {
            _accountModal.SetBusy(false);
        }
    }

    private async Task RegisterAsync(IDictionary<string, string> values)
    {
        const AuthMode mode = AuthMode.Register;

        var errors = _model.Validate(mode, values);
        _accountModal.ShowErrors(errors);
        _accountModal.ShowStatus(null);
        if (errors.Count > 0) return;
        if (!AllowSignup)
        {
            _accountModal.ShowStatus("Clinic sign-up is closed right now. Please contact the VetCare team.");
            return;
        }
        bool approval = RequireClinicApproval;

        string email = _model.Value(mode, values, AuthField.Email);

        _accountModal.SetBusy(true, "Creating account…");
        try
        {
            if (await _users.EmailExistsAsync(email))
            {
                _accountModal.ShowErrors(new Dictionary<string, string>
                {
                    [AuthField.Email] = "An account with this email already exists."
                });
                return;
            }

            // A new clinic waits for Super Admin review; its first user is that clinic's Administrator.
            var clinic = new Clinic
            {
                ClinicName = _model.Value(mode, values, AuthField.ClinicName),
                Address = _model.Value(mode, values, AuthField.ClinicAddress),
                ContactNo = _model.Value(mode, values, AuthField.ClinicContact),
                Email = email,
                Status = approval ? AccountStatus.Pending : AccountStatus.Active
            };

            var administrator = new User
            {
                RoleId = RoleIds.Administrator,
                FirstName = PersonName.Split(_model.Value(mode, values, AuthField.AdminName)).First,
                LastName = PersonName.Split(_model.Value(mode, values, AuthField.AdminName)).Last,
                Username = await _users.MakeUniqueUsernameAsync(email.Split('@')[0]),
                PasswordHash = PasswordHasher.Hash(_model.Value(mode, values, AuthField.Password)),
                Email = email,
                Status = approval ? AccountStatus.Inactive : AccountStatus.Active // turned on when the Super Admin approves the clinic
            };

            await _clinics.RegisterWithAdministratorAsync(clinic, administrator);

            // Back to the sign-in card with the new email filled in.
            _accountModal.ClearInputs();
            await CloseModalAsync();
            _card.SetFieldValue(AuthField.Email, email);
            _card.ShowStatus(approval ? _model.Get(mode).SuccessMessage : "Your clinic is registered. Sign in with your new account.");
        }
        catch (SqlException)
        {
            _accountModal.ShowStatus(DatabaseError);
        }
        finally
        {
            _accountModal.SetBusy(false);
        }
    }
}
