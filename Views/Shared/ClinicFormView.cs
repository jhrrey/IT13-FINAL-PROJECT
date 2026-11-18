using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Add / Edit clinic panel (section 4.C, 480px): clinic details, and when adding the first Administrator account.
/// Inline errors per field; an amber note when another clinic looks the same (it doesn't block saving).
/// </summary>
public sealed class ClinicFormView : ContentView
{
    public const double PanelWidth = 480;
    public const string NameField = "clinicName";
    public const string AddressField = "address";
    public const string ContactField = "contactNo";
    public const string EmailField = "clinicEmail";
    public const string AdminFirstField = UserFormData.FirstNameField;
    public const string AdminLastField = UserFormData.LastNameField;
    public const string AdminUsernameField = UserFormData.UsernameField;
    public const string AdminEmailField = UserFormData.EmailField;
    public const string PasswordField = UserFormData.PasswordField;
    public const string ConfirmField = UserFormData.ConfirmPasswordField;

    private readonly bool _adding;
    private readonly int? _clinicId;
    private readonly Dictionary<string, (Border Box, Label Error, Func<string> Read)> _fields = new();
    private readonly Border _duplicateBox;
    private readonly Label _duplicateText, _message;
    private readonly HoverButton _save;

    public ClinicFormView(Clinic existing)
    {
        _adding = existing == null;
        _clinicId = existing?.ClinicId;

        var form = new VerticalStackLayout { Padding = new Thickness(24, 20), Spacing = 16 };
        if (_adding)
        {
            var (note, noteText) = PharmacyKit.InfoBox("CyanSoft", "Navy");
            noteText.Text = "Clinics added here are active right away.";
            form.Children.Add(note);
        }

        form.Children.Add(SectionTitle("Clinic details"));
        form.Children.Add(Field(NameField, "Clinic name *", "e.g. Happy Paws Veterinary Clinic", 100, existing?.ClinicName));
        form.Children.Add(AreaField(AddressField, "Address *", "e.g. Lanang, Davao City", 255, existing?.Address));
        form.Children.Add(Field(ContactField, "Contact no. *", "09XX XXX XXXX or landline", 20, existing?.ContactNo, Keyboard.Telephone));
        form.Children.Add(Field(EmailField, "Email *", "clinic@example.com", 100, existing?.Email, Keyboard.Email));

        (_duplicateBox, _duplicateText) = PharmacyKit.InfoBox("WarningSoft", "Warning");
        _duplicateBox.IsVisible = false;
        form.Children.Add(_duplicateBox);

        if (_adding)
        {
            form.Children.Add(SectionTitle("First administrator", top: 8));
            var names = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            names.Add(Field(AdminFirstField, "First name *", "e.g. Rafael", 50, null), 0, 0);
            names.Add(Field(AdminLastField, "Last name *", "e.g. Cruz", 50, null), 1, 0);
            form.Children.Add(names);
            form.Children.Add(Field(AdminUsernameField, "Username *", "e.g. rcruz", 50, null));
            form.Children.Add(Field(AdminEmailField, "Email *", "admin@example.com", 100, null, Keyboard.Email));
            form.Children.Add(Field(PasswordField, "Temporary password *", VetCare.Helpers.Validator.PasswordHint, 100, null, password: true));
            form.Children.Add(Field(ConfirmField, "Confirm password *", "Re-enter the password", 100, null, password: true));
        }

        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };
        form.Children.Add(_message);

        _save = new HoverButton { Text = "Save clinic", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame(_adding ? "Add clinic" : "Edit clinic", _adding ? null : existing.ClinicName, form,
            () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public ClinicFormData Read()
    {
        string Get(string key) => _fields.TryGetValue(key, out var f) ? f.Read()?.Trim() : null;
        return new ClinicFormData
        {
            ClinicId = _clinicId,
            ClinicName = Get(NameField),
            Address = Get(AddressField),
            ContactNo = Get(ContactField),
            Email = Get(EmailField),
            AdminFirstName = Get(AdminFirstField),
            AdminLastName = Get(AdminLastField),
            AdminUsername = Get(AdminUsernameField),
            AdminEmail = Get(AdminEmailField),
            Password = _fields.TryGetValue(PasswordField, out var p) ? p.Read() : null,
            ConfirmPassword = _fields.TryGetValue(ConfirmField, out var c) ? c.Read() : null
        };
    }

    public void ShowResult(OperationResult result)
    {
        foreach (var (key, field) in _fields)
            PharmacyKit.SetError(field.Error, result.FieldErrors.TryGetValue(key, out var m) ? m : null, field.Box);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    /// <summary>Rule 3: a warning (not an error) when another clinic matches.</summary>
    public void ShowDuplicates(IReadOnlyList<ClinicDuplicate> duplicates)
    {
        _duplicateBox.IsVisible = duplicates.Count > 0;
        if (duplicates.Count == 0) return;
        var d = duplicates[0];
        _duplicateText.Text = "Possible duplicate: " + d.ClinicName + " (" + d.Status + ") has the same " + d.MatchedOn + ". Save again to keep it.";
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Save clinic";
    }

    private View Field(string key, string label, string placeholder, int max, string value, Keyboard keyboard = null, bool password = false)
    {
        var (box, entry) = PharmacyKit.TextInput(placeholder, keyboard, max);
        entry.IsPassword = password;
        entry.Text = value;
        var error = PharmacyKit.ErrorLabel();
        entry.TextChanged += (s, e) => { PharmacyKit.SetError(error, null, box); _message.IsVisible = false; };
        _fields[key] = (box, error, () => entry.Text);
        return new VerticalStackLayout { Children = { PharmacyKit.FieldLabel(label), box, error } };
    }

    private View AreaField(string key, string label, string placeholder, int max, string value)
    {
        var editor = new Editor
        {
            Placeholder = placeholder,
            MaxLength = max,
            Text = value,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 72,
            FontFamily = "DMSans",
            FontSize = 16,
            TextColor = ThemeResources.Color("Navy"),
            PlaceholderColor = ThemeResources.Color("Placeholder"),
            BackgroundColor = Colors.Transparent
        };
        var box = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = -1, MinimumHeightRequest = 80, Padding = new Thickness(8, 4), Content = editor };
        var error = PharmacyKit.ErrorLabel();
        editor.TextChanged += (s, e) => { PharmacyKit.SetError(error, null, box); _message.IsVisible = false; };
        _fields[key] = (box, error, () => editor.Text);
        return new VerticalStackLayout { Children = { PharmacyKit.FieldLabel(label), box, error } };
    }

    private static Label SectionTitle(string text, double top = 0) => new()
    {
        Text = text,
        FontFamily = "NunitoExtraBold",
        FontSize = 17,
        TextColor = ThemeResources.Color("Navy"),
        Margin = new Thickness(0, top, 0, 0)
    };
}
