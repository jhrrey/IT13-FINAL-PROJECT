using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// The Add owner fields as a block (first and last name, contact no., email, address) for the walk-in panel
/// and the guest check-in panel. Same field keys as <see cref="OwnerFormData"/>, so OwnerController's
/// validation messages show under the right field.
/// </summary>
public sealed class OwnerFieldsView : ContentView
{
    private readonly Entry _first, _last, _contact, _email;
    private readonly Editor _address;
    private readonly Border _firstBox, _lastBox, _contactBox, _emailBox, _addressBox;
    private readonly Label _firstError, _lastError, _contactError, _emailError, _addressError;
    private readonly VerticalStackLayout _afterContact;

    public OwnerFieldsView(string firstName = null, string lastName = null, string contactNo = null)
    {
        (_firstBox, _first) = PharmacyKit.TextInput(maxLength: 50);
        (_lastBox, _last) = PharmacyKit.TextInput(maxLength: 50);
        (_contactBox, _contact) = PharmacyKit.TextInput("09XX XXX XXXX", Keyboard.Telephone, 13);
        (_emailBox, _email) = PharmacyKit.TextInput("name@example.com", Keyboard.Email, 100);
        _address = new Editor { FontFamily = "DMSans", FontSize = 15, TextColor = ThemeResources.Color("Navy"), PlaceholderColor = ThemeResources.Color("Placeholder"), BackgroundColor = Colors.Transparent, MaxLength = 255, HeightRequest = 64, AutoSize = EditorAutoSizeOption.Disabled };
        _addressBox = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = 80, Padding = new Thickness(12, 6), Content = _address };
        _firstError = PharmacyKit.ErrorLabel();
        _lastError = PharmacyKit.ErrorLabel();
        _contactError = PharmacyKit.ErrorLabel();
        _emailError = PharmacyKit.ErrorLabel();
        _addressError = PharmacyKit.ErrorLabel();
        _afterContact = new VerticalStackLayout();

        _first.Text = firstName;
        _last.Text = lastName;
        _contact.Text = contactNo;

        _first.TextChanged += (s, e) => PharmacyKit.SetError(_firstError, null, _firstBox);
        _last.TextChanged += (s, e) => PharmacyKit.SetError(_lastError, null, _lastBox);
        _contact.TextChanged += (s, e) => { PharmacyKit.SetError(_contactError, null, _contactBox); ContactChanged?.Invoke(this, _contact.Text); };
        _email.TextChanged += (s, e) => PharmacyKit.SetError(_emailError, null, _emailBox);
        _address.TextChanged += (s, e) => PharmacyKit.SetError(_addressError, null, _addressBox);

        var names = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        names.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("First name *"), _firstBox, _firstError } }, 0, 0);
        names.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Last name *"), _lastBox, _lastError } }, 1, 0);

        Content = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                names,
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Contact no. *"), _contactBox, _contactError, _afterContact } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Email"), _emailBox, _emailError } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Address"), _addressBox, _addressError } }
            }
        };
    }

    /// <summary>The contact no. changed (for the "already registered" check).</summary>
    public event EventHandler<string> ContactChanged;

    public string ContactNo => _contact.Text;

    /// <summary>A place under the contact no. for the "already registered" box.</summary>
    public VerticalStackLayout AfterContact => _afterContact;

    public OwnerFormData Read() => new()
    {
        FirstName = _first.Text,
        LastName = _last.Text,
        ContactNo = _contact.Text,
        Email = _email.Text,
        Address = _address.Text
    };

    public void ShowErrors(IDictionary<string, string> errors)
    {
        string Get(string key) => errors != null && errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_firstError, Get(OwnerFormData.FirstNameField), _firstBox);
        PharmacyKit.SetError(_lastError, Get(OwnerFormData.LastNameField), _lastBox);
        PharmacyKit.SetError(_contactError, Get(OwnerFormData.ContactNoField), _contactBox);
        PharmacyKit.SetError(_emailError, Get(OwnerFormData.EmailField), _emailBox);
        PharmacyKit.SetError(_addressError, Get(OwnerFormData.AddressField), _addressBox);
    }
}

/// <summary>
/// The Add pet fields as a block (name, species, breed, sex, birth date or estimated age, color, weight) for the
/// walk-in and guest check-in panels. Same field keys as <see cref="PetFormData"/> (PetController.Validate).
/// </summary>
public sealed class PetFieldsView : ContentView
{
    private readonly Entry _name, _breed, _years, _months, _color, _weight;
    private readonly Border _nameBox, _breedBox, _ageBox, _colorBox, _weightBox;
    private readonly PickerField _species, _sex;
    private readonly DateBox _birth;
    private readonly CheckBoxRow _unknown;
    private readonly View _ageRow;
    private readonly Label _nameError, _breedError, _birthError, _ageError, _colorError, _weightError;

    public PetFieldsView(string petName = null, string species = null)
    {
        (_nameBox, _name) = PharmacyKit.TextInput(maxLength: 50);
        _species = new PickerField(PetFormData.SpeciesField, "Species *");
        _species.SetItems(PetSpecies.All.Select(s => (s, (object)s)), PetSpecies.All.Contains(species) ? species : null);
        (_breedBox, _breed) = PharmacyKit.TextInput("e.g. Aspin", maxLength: 50);
        _sex = new PickerField(PetFormData.SexField, "Sex *");
        _sex.SetItems(PetSpecies.Sexes.Select(s => (s, (object)s)));
        _birth = new DateBox { Placeholder = "Pick a date", MinimumDate = DateTime.Today.AddYears(-40), MaximumDate = DateTime.Today };
        _unknown = new CheckBoxRow("I don’t know the exact date");
        (_ageBox, _years) = PharmacyKit.TextInput("Years", Keyboard.Numeric, 2);
        var (monthsBox, months) = PharmacyKit.TextInput("Months", Keyboard.Numeric, 2);
        _months = months;
        (_colorBox, _color) = PharmacyKit.TextInput("e.g. Brown", maxLength: 30);
        (_weightBox, _weight) = PharmacyKit.TextInput("kg", Keyboard.Numeric, 7);
        _nameError = PharmacyKit.ErrorLabel();
        _breedError = PharmacyKit.ErrorLabel();
        _birthError = PharmacyKit.ErrorLabel();
        _ageError = PharmacyKit.ErrorLabel();
        _colorError = PharmacyKit.ErrorLabel();
        _weightError = PharmacyKit.ErrorLabel();
        _name.Text = petName;

        var age = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        age.Add(_ageBox, 0, 0);
        age.Add(monthsBox, 1, 0);
        _ageRow = new VerticalStackLayout { IsVisible = false, Children = { age, _ageError } };
        _unknown.Changed += (s, on) => { _birth.IsVisible = !on; _ageRow.IsVisible = on; };

        _name.TextChanged += (s, e) => PharmacyKit.SetError(_nameError, null, _nameBox);
        _breed.TextChanged += (s, e) => PharmacyKit.SetError(_breedError, null, _breedBox);
        _color.TextChanged += (s, e) => PharmacyKit.SetError(_colorError, null, _colorBox);
        _weight.TextChanged += (s, e) => PharmacyKit.SetError(_weightError, null, _weightBox);
        _years.TextChanged += (s, e) => PharmacyKit.SetError(_ageError, null, _ageBox);
        _birth.DateChanged += (s, e) => PharmacyKit.SetError(_birthError, null, _birth.Box);
        _species.SelectionChanged += (s, e) => _species.SetError(null);
        _sex.SelectionChanged += (s, e) => _sex.SetError(null);

        var nameRow = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        nameRow.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Pet name *"), _nameBox, _nameError } }, 0, 0);
        nameRow.Add(_species, 1, 0);
        var breedRow = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        breedRow.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Breed"), _breedBox, _breedError } }, 0, 0);
        breedRow.Add(_sex, 1, 0);
        var colorRow = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        colorRow.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Color"), _colorBox, _colorError } }, 0, 0);
        colorRow.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Weight (kg)"), _weightBox, _weightError } }, 1, 0);

        Content = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                nameRow,
                breedRow,
                new VerticalStackLayout { Spacing = 8, Children = { PharmacyKit.FieldLabel("Birth date"), _birth, _birthError, _ageRow, _unknown } },
                colorRow
            }
        };
    }

    public PetFormData Read() => new()
    {
        PetName = _name.Text,
        Species = _species.SelectedValue as string,
        Breed = _breed.Text,
        Sex = _sex.SelectedValue as string,
        BirthDate = _unknown.IsChecked ? null : _birth.Date,
        UseEstimatedAge = _unknown.IsChecked,
        AgeYears = _years.Text,
        AgeMonths = _months.Text,
        Color = _color.Text,
        Weight = _weight.Text
    };

    public void ShowErrors(IDictionary<string, string> errors)
    {
        string Get(string key) => errors != null && errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_nameError, Get(PetFormData.PetNameField), _nameBox);
        _species.SetError(Get(PetFormData.SpeciesField));
        PharmacyKit.SetError(_breedError, Get(PetFormData.BreedField), _breedBox);
        _sex.SetError(Get(PetFormData.SexField));
        PharmacyKit.SetError(_birthError, Get(PetFormData.BirthDateField), _birth.Box);
        PharmacyKit.SetError(_ageError, Get(PetFormData.AgeField), _ageBox);
        PharmacyKit.SetError(_colorError, Get(PetFormData.ColorField), _colorBox);
        PharmacyKit.SetError(_weightError, Get(PetFormData.WeightField), _weightBox);
    }
}

/// <summary>A drawn 20px checkbox with its text (WinUI's native CheckBox keeps a wide frame that pushes the text away).</summary>
public sealed class CheckBoxRow : ContentView
{
    private readonly Border _box;
    private readonly LucideIcon _check;

    public CheckBoxRow(string text, bool isChecked = false)
    {
        _check = new LucideIcon { Icon = "check", StrokeColor = Colors.White, StrokeWidth = 3, WidthRequest = 14, HeightRequest = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _box = new Border { WidthRequest = 20, HeightRequest = 20, StrokeThickness = 1.5, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 }, VerticalOptions = LayoutOptions.Center, Content = _check };
        var row = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Start,
            Children = { _box, new Label { Text = text, FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center } }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => { IsChecked = !IsChecked; Changed?.Invoke(this, IsChecked); };
        row.GestureRecognizers.Add(tap);
        HandCursor.Apply(row);
        SemanticProperties.SetDescription(row, text);
        Content = row;
        IsChecked = isChecked;
    }

    public event EventHandler<bool> Changed;

    private bool _checked;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            _checked = value;
            _box.BackgroundColor = ThemeResources.Color(value ? "Orange" : "Surface");
            _box.Stroke = ThemeResources.Color(value ? "Orange" : "BorderColor");
            _check.IsVisible = value;
        }
    }
}
