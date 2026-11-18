using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>What the controller tells the Add / Edit form to show.</summary>
public sealed class UserFormSetup
{
    public bool IsNew { get; init; }

    /// <summary>The roles the signed-in user may assign (Super Admin: Administrator; Administrator: Vet, Cashier/Staff).</summary>
    public IReadOnlyList<(int Id, string Name)> Roles { get; init; } = Array.Empty<(int, string)>();

    /// <summary>Clinics to choose from; null hides the Clinic dropdown (Administrators: set automatically).</summary>
    public IReadOnlyList<(int Id, string Name)> Clinics { get; init; }

    public UserFormData Values { get; init; } = new();

    /// <summary>True when editing yourself: your own role can't be changed (rule 7).</summary>
    public bool RoleLocked { get; init; }

    /// <summary>True when editing yourself: you can't deactivate yourself (rule 7).</summary>
    public bool StatusLocked { get; init; }
}

/// <summary>
/// The Add / Edit user form. Shows the controller's setup, returns what was typed, and
/// displays the controller's validation results. Contains no rules itself.
/// </summary>
public partial class UserFormView : ContentView
{
    private readonly UserFormSetup _setup;
    private readonly InputFieldSet _fields = new();
    private readonly PickerField _role;
    private readonly PickerField _clinic;

    public UserFormView(UserFormSetup setup)
    {
        InitializeComponent();
        _setup = setup;

        TitleLabel.Text = setup.IsNew ? "Add user" : "Edit user";

        // Text fields (max lengths match the User table).
        var top = new List<AuthField>
        {
            new(UserFormData.FirstNameField, "First name", "e.g. Maria", AuthFieldKind.Text, maxLength: 50),
            new(UserFormData.LastNameField, "Last name", "e.g. Santos", AuthFieldKind.Text, maxLength: 50),
            new(UserFormData.UsernameField, "Username", "e.g. msantos", AuthFieldKind.Text, maxLength: 50),
            new(UserFormData.EmailField, "Email", "name@example.com", AuthFieldKind.Email, maxLength: 100),
            new(UserFormData.ContactNoField, "Contact no. (optional)", "e.g. 0912 345 6789", AuthFieldKind.Phone, maxLength: 20)
        };
        var passwords = setup.IsNew
            ? new List<AuthField>
            {
                new(UserFormData.PasswordField, "Temporary password", VetCare.Helpers.Validator.PasswordHint, AuthFieldKind.Password),
                new(UserFormData.ConfirmPasswordField, "Confirm password", "Re-enter the password", AuthFieldKind.Password)
            }
            : new List<AuthField>();

        _fields.Build(top.Concat(passwords),
            field => field.Kind == AuthFieldKind.Password ? PasswordFields : TopFields);
        _fields.SubmitRequested += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        _fields.ValueChanged += (s, e) => ShowMessage(null);

        // Role (and Clinic for the Super Admin).
        _role = new PickerField(UserFormData.RoleField, "Role");
        _role.SetItems(setup.Roles.Select(r => (r.Name, (object)r.Id)), setup.Values.RoleId);
        _role.IsLocked = setup.RoleLocked;
        RoleHost.Content = _role;

        if (setup.Clinics != null)
        {
            _clinic = new PickerField(UserFormData.ClinicField, "Clinic");
            _clinic.SetItems(setup.Clinics.Select(c => (c.Name, (object)c.Id)), setup.Values.ClinicId);
            ClinicHost.Content = _clinic;
        }
        ClinicHost.IsVisible = _clinic != null;

        // Values when editing.
        var v = setup.Values;
        _fields.SetValue(UserFormData.FirstNameField, v.FirstName);
        _fields.SetValue(UserFormData.LastNameField, v.LastName);
        _fields.SetValue(UserFormData.UsernameField, v.Username);
        _fields.SetValue(UserFormData.EmailField, v.Email);
        _fields.SetValue(UserFormData.ContactNoField, v.ContactNo);

        StatusSwitch.IsToggled = v.IsActive;
        StatusSwitch.IsEnabled = !setup.StatusLocked;
        StatusSwitch.Toggled += (s, e) => UpdateStatusText();
        UpdateStatusText();

        // Buttons.
        SaveButton.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        CancelButton.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);
        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    /// <summary>What is currently in the form.</summary>
    public UserFormData Read()
    {
        var values = _fields.ReadValues();
        string Get(string name) => values.TryGetValue(name, out var v) ? v : null;

        return new UserFormData
        {
            UserId = _setup.Values.UserId,
            FirstName = Get(UserFormData.FirstNameField),
            LastName = Get(UserFormData.LastNameField),
            Username = Get(UserFormData.UsernameField),
            Email = Get(UserFormData.EmailField),
            ContactNo = Get(UserFormData.ContactNoField),
            RoleId = _role.SelectedValue as int?,
            ClinicId = _clinic != null ? _clinic.SelectedValue as int? : _setup.Values.ClinicId,
            IsActive = StatusSwitch.IsToggled,
            Password = Get(UserFormData.PasswordField),
            ConfirmPassword = Get(UserFormData.ConfirmPasswordField)
        };
    }

    /// <summary>Shows per-field errors under each field, and/or a general message.</summary>
    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors;
        _role.SetError(errors.TryGetValue(UserFormData.RoleField, out var roleError) ? roleError : null);
        _clinic?.SetError(errors.TryGetValue(UserFormData.ClinicField, out var clinicError) ? clinicError : null);
        _fields.ShowErrors(errors);
        ShowMessage(result.Message);
    }

    public void SetBusy(bool busy)
    {
        SaveButton.IsEnabled = !busy && !OfflineNote.IsVisible;
        SaveButton.Text = busy ? "Saving…" : "Save user";
    }

    /// <summary>Offline: Save is disabled and the amber note is shown (rule 10).</summary>
    public void SetOffline(bool offline)
    {
        OfflineNote.IsVisible = offline;
        SaveButton.IsEnabled = !offline;
    }

    private void ShowMessage(string message)
    {
        MessageLabel.Text = message ?? string.Empty;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(message);
    }

    private void UpdateStatusText()
    {
        StatusText.Text = StatusSwitch.IsToggled ? "Active" : "Inactive";
        StatusHint.Text = _setup.StatusLocked
            ? "You can't deactivate your own account."
            : StatusSwitch.IsToggled ? "This user can sign in." : "This user can't sign in.";
    }
}
