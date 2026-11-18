using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>What the Add / Edit admin panel shows.</summary>
public sealed class AdminFormSetup
{
    public bool IsNew { get; init; } = true;

    /// <summary>The clinics an admin can be added to (Active and Pending, rule 4).</summary>
    public IReadOnlyList<Clinic> Clinics { get; init; } = Array.Empty<Clinic>();

    /// <summary>Editing: rule 3, an admin can't move to another clinic. Also locked when opened for one clinic.</summary>
    public bool ClinicLocked { get; init; }

    public UserFormData Values { get; init; } = new();
}

/// <summary>
/// Add / Edit admin panel (section 3.B, 460px): "Role: Administrator", the clinic, name, username, email,
/// contact no., a temporary password with Generate and show/hide (Add only), and Active / Inactive.
/// </summary>
public sealed class AdminFormView : ContentView
{
    public const double PanelWidth = 460;

    private readonly AdminFormSetup _setup;
    private readonly Dictionary<string, (Border Box, Label Error, Entry Entry)> _fields = new();
    private readonly PickerField _clinic;
    private readonly ToggleSwitch _active;
    private readonly Border _pendingNote, _offlineNote;
    private readonly Label _message;
    private readonly HoverButton _save;
    private Entry _password, _confirm;
    private LucideIcon _eye;

    public AdminFormView(AdminFormSetup setup)
    {
        _setup = setup;
        var v = setup.Values;
        var form = new VerticalStackLayout { Padding = new Thickness(24, 16, 24, 20), Spacing = 16 };
        form.Children.Add(Badges.Pill("Role: Administrator", "Navy", "PaleBlue"));

        _clinic = new PickerField(UserFormData.ClinicField, "Clinic *");
        _clinic.SetItems(setup.Clinics.Select(c => (c.ClinicName + (c.Status == AccountStatus.Active ? string.Empty : " · " + c.Status), (object)c.ClinicId)), v.ClinicId);
        _clinic.IsLocked = setup.ClinicLocked;
        _clinic.SelectionChanged += (s, e) => UpdatePendingNote();
        form.Children.Add(_clinic);

        (_pendingNote, var pendingText) = PharmacyKit.InfoBox("WarningSoft", "Warning");
        pendingText.Text = "This admin can log in once the clinic is approved.";
        form.Children.Add(_pendingNote);

        var names = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        names.Add(Field(UserFormData.FirstNameField, "First name *", "e.g. Ana", 50, v.FirstName), 0, 0);
        names.Add(Field(UserFormData.LastNameField, "Last name *", "e.g. Reyes", 50, v.LastName), 1, 0);
        form.Children.Add(names);
        form.Children.Add(Field(UserFormData.UsernameField, "Username *", "e.g. areyes", 50, v.Username));
        form.Children.Add(Field(UserFormData.EmailField, "Email *", "admin@example.com", 100, v.Email, Keyboard.Email));
        form.Children.Add(Field(UserFormData.ContactNoField, "Contact no.", "09XX XXX XXXX", 20, v.ContactNo, Keyboard.Telephone));

        if (setup.IsNew)
        {
            form.Children.Add(PasswordField());
            form.Children.Add(Field(UserFormData.ConfirmPasswordField, "Confirm password *", "Re-enter the password", 100, null, password: true));
            _confirm = _fields[UserFormData.ConfirmPasswordField].Entry;
        }

        _active = new ToggleSwitch(v.IsActive ? "Active" : "Inactive", v.IsActive, "SuccessDot");
        _active.Toggled += (s, on) => _active.Text = on ? "Active" : "Inactive";
        form.Children.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Status"), _active } });

        (_offlineNote, var offlineText) = PharmacyKit.InfoBox("WarningSoft", "Warning");
        offlineText.Text = "Connect to the internet to create or change admin accounts.";
        _offlineNote.IsVisible = false;
        form.Children.Add(_offlineNote);

        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };
        form.Children.Add(_message);

        _save = new HoverButton { Text = "Save admin", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame(setup.IsNew ? "Add admin" : "Edit admin", setup.IsNew ? null : v.FirstName + " " + v.LastName, form,
            () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);
        UpdatePendingNote();

        // The clinic picker and password box can pull the panel down while it opens; start at the top.
        Loaded += async (s, e) =>
        {
            foreach (int delay in new[] { 400, 1200 })
            {
                await Task.Delay(delay);
                if (this.GetVisualTreeDescendants().OfType<ScrollView>().FirstOrDefault() is ScrollView scroll)
                    await scroll.ScrollToAsync(0, 0, false);
            }
        };
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public UserFormData Read()
    {
        string Get(string key) => _fields.TryGetValue(key, out var f) ? f.Entry.Text : null;
        return new UserFormData
        {
            UserId = _setup.Values.UserId,
            ClinicId = _clinic.SelectedValue as int?,
            RoleId = RoleIds.Administrator,
            FirstName = Get(UserFormData.FirstNameField)?.Trim(),
            LastName = Get(UserFormData.LastNameField)?.Trim(),
            Username = Get(UserFormData.UsernameField)?.Trim(),
            Email = Get(UserFormData.EmailField)?.Trim(),
            ContactNo = Get(UserFormData.ContactNoField)?.Trim(),
            Password = _password?.Text,
            ConfirmPassword = _confirm?.Text,
            IsActive = _active.IsOn
        };
    }

    public void ShowResult(OperationResult result)
    {
        foreach (var (key, field) in _fields)
            PharmacyKit.SetError(field.Error, result.FieldErrors.TryGetValue(key, out var m) ? m : null, field.Box);
        _clinic.SetError(result.FieldErrors.TryGetValue(UserFormData.ClinicField, out var c) ? c : null);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy && !_offlineNote.IsVisible;
        _save.Text = busy ? "Saving…" : "Save admin";
    }

    /// <summary>Rule 12: adding needs a connection.</summary>
    public void SetOffline(bool offline)
    {
        _offlineNote.IsVisible = offline;
        _save.IsEnabled = !offline;
    }

    private void UpdatePendingNote()
    {
        var clinic = _setup.Clinics.FirstOrDefault(c => Equals(c.ClinicId, _clinic.SelectedValue));
        _pendingNote.IsVisible = clinic?.Status == AccountStatus.Pending;
    }

    /// <summary>Temporary password with a navy "Generate" link and an eye button to show / hide it.</summary>
    private View PasswordField()
    {
        var (box, entry) = PharmacyKit.TextInput(VetCare.Helpers.Validator.PasswordHint, maxLength: 100);
        entry.IsPassword = true;
        _password = entry;
        var error = PharmacyKit.ErrorLabel();
        entry.TextChanged += (s, e) => { PharmacyKit.SetError(error, null, box); _message.IsVisible = false; };
        _fields[UserFormData.PasswordField] = (box, error, entry);

        _eye = new LucideIcon { Icon = "eye", StrokeColor = ThemeResources.Color("NavyMuted"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center };
        var eyeButton = new Border { Padding = new Thickness(6), StrokeThickness = 0, BackgroundColor = Colors.Transparent, Content = _eye, VerticalOptions = LayoutOptions.Center };
        var eyeTap = new TapGestureRecognizer();
        eyeTap.Tapped += (s, e) => ShowPassword(entry.IsPassword);
        eyeButton.GestureRecognizers.Add(eyeTap);
        HandCursor.Apply(eyeButton);
        SemanticProperties.SetDescription(eyeButton, "Show or hide the password");
        var inner = new Grid { ColumnSpacing = 4, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        box.Content = null;
        inner.Add(entry, 0, 0);
        inner.Add(eyeButton, 1, 0);
        box.Content = inner;

        var generate = new TextLink { Text = "Generate", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
        generate.Clicked += (s, e) =>
        {
            string password = PasswordGenerator.Generate();
            entry.Text = password;
            if (_confirm != null) _confirm.Text = password;
            ShowPassword(true);
        };
        var label = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        label.Add(PharmacyKit.FieldLabel("Temporary password *"), 0, 0);
        label.Add(generate, 1, 0);
        return new VerticalStackLayout
        {
            Children = { label, box, PharmacyKit.Hint("They'll be asked to set their own password at the first sign-in."), error }
        };
    }

    private void ShowPassword(bool show)
    {
        _password.IsPassword = !show;
        if (_confirm != null) _confirm.IsPassword = !show;
        _eye.Icon = show ? "eye-off" : "eye";
    }

    private View Field(string key, string label, string placeholder, int max, string value, Keyboard keyboard = null, bool password = false)
    {
        var (box, entry) = PharmacyKit.TextInput(placeholder, keyboard, max);
        entry.IsPassword = password;
        entry.Text = value;
        var error = PharmacyKit.ErrorLabel();
        entry.TextChanged += (s, e) => { PharmacyKit.SetError(error, null, box); _message.IsVisible = false; };
        _fields[key] = (box, error, entry);
        return new VerticalStackLayout { Children = { PharmacyKit.FieldLabel(label), box, error } };
    }
}
