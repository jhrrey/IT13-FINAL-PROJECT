using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// The Add / Edit pet form. Returns what was entered and shows the controller's validation
/// results; the owner search results also come from the controller. Contains no rules itself.
/// </summary>
public partial class PetFormView : ContentView
{
    public const double PanelWidth = 460;

    private readonly PetFormData _initial;
    private readonly InputFieldSet _textFields = new();
    private readonly OwnerPickerField _owner;

    // Species and sex
    private readonly PickerField _species;
    private readonly List<(string Sex, Border Button, Label Text, LucideIcon Icon)> _sexButtons = new();
    private readonly Label _sexError;
    private string _sex;

    // Birth date or estimated age
    private readonly DateBox _birthDate;
    private readonly Grid _estimateRow;
    private readonly Entry _years;
    private readonly Entry _months;
    private readonly CheckBox _unknownDate;
    private readonly Label _birthError;

    // Weight
    private readonly Border _weightBox;
    private readonly Entry _weight;
    private readonly Label _weightError;

    /// <param name="initial">The values to start with (an empty <see cref="PetFormData"/> for a new pet).</param>
    /// <param name="initialOwner">The owner already chosen (opened from an owner profile, or editing), or null.</param>
    public PetFormView(PetFormData initial, Owner initialOwner)
    {
        InitializeComponent();
        ScrollFocus.DontScrollOnFocusWhileOpening(FormScroll);
        _initial = initial;

        TitleLabel.Text = initial.IsNew ? "Add pet" : "Edit pet";

        // Owner ------------------------------------------------------------------------------
        _owner = new OwnerPickerField();
        _owner.SearchRequested += (s, text) => { ShowMessage(null); OwnerSearchRequested?.Invoke(this, text); };
        _owner.OwnerChanged += (s, owner) =>
        {
            ShowMessage(null);
            // Rule 10: in Edit, a different owner means the pet is transferred.
            bool transfer = owner != null && !_initial.IsNew && _initial.OriginalOwnerId != 0 && owner.OwnerId != _initial.OriginalOwnerId;
            _owner.Hint = transfer ? "Saving moves this pet to " + owner.FullName + "." : null;
        };
        FieldsStack.Add(_owner);

        // Pet name --------------------------------------------------------------------------
        var nameSlot = new VerticalStackLayout();
        FieldsStack.Add(nameSlot);

        // Species ---------------------------------------------------------------------------
        _species = new PickerField(PetFormData.SpeciesField, "Species *");
        _species.SetItems(PetSpecies.All.Select(s => (s, (object)s)), initial.Species);
        FieldsStack.Add(_species);

        // Breed -----------------------------------------------------------------------------
        var breedSlot = new VerticalStackLayout();
        FieldsStack.Add(breedSlot);

        // Sex (two toggle buttons) ----------------------------------------------------------
        var sexRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        sexRow.Add(SexButton(PetSpecies.Male, "mars"), 0, 0);
        sexRow.Add(SexButton(PetSpecies.Female, "venus"), 1, 0);
        _sexError = ErrorLabel();
        FieldsStack.Add(new VerticalStackLayout { Children = { FieldLabel("Sex *"), sexRow, _sexError } });

        // Birth date / estimated age ---------------------------------------------------------
        _birthDate = new DateBox
        {
            MinimumDate = DateTime.Today.AddYears(-50),
            MaximumDate = DateTime.Today,
            Date = initial.BirthDate
        };
        _birthDate.DateChanged += (s, e) =>
        {
            SetError(_birthError, null, _birthDate.Box);
            ShowMessage(null);
        };

        _years = NumberEntry("Years");
        _months = NumberEntry("Months");
        _estimateRow = new Grid
        {
            IsVisible = false,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        _estimateRow.Add(SuffixBox(_years, "yrs"), 0, 0);
        _estimateRow.Add(SuffixBox(_months, "mos"), 1, 0);

        _unknownDate = new CheckBox { Color = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center };
        _unknownDate.CheckedChanged += (s, e) => ShowEstimate(e.Value);
        var unknownLabel = new Label
        {
            Text = "I don’t know the exact date",
            FontSize = 14,
            TextColor = ThemeResources.Color("BodyText"),
            VerticalOptions = LayoutOptions.Center
        };
        var unknownTap = new TapGestureRecognizer();
        unknownTap.Tapped += (s, e) => _unknownDate.IsChecked = !_unknownDate.IsChecked;
        unknownLabel.GestureRecognizers.Add(unknownTap);
        HandCursor.Apply(unknownLabel);

        _birthError = ErrorLabel();
        FieldsStack.Add(new VerticalStackLayout
        {
            Children =
            {
                FieldLabel("Birth date"),
                _birthDate,
                _estimateRow,
                _birthError,
                new HorizontalStackLayout { Spacing = 2, Margin = new Thickness(-6, 4, 0, 0), Children = { _unknownDate, unknownLabel } }
            }
        });

        // Color -----------------------------------------------------------------------------
        var colorSlot = new VerticalStackLayout();
        FieldsStack.Add(colorSlot);

        // Weight ----------------------------------------------------------------------------
        _weight = NumberEntry("e.g. 12.5");
        _weight.MaxLength = 6;
        _weightBox = SuffixBox(_weight, "kg");
        _weightError = ErrorLabel();
        FieldsStack.Add(new VerticalStackLayout { Children = { FieldLabel("Weight"), _weightBox, _weightError } });

        // Text inputs (max lengths match the Pet table) ---------------------------------------
        var slots = new Dictionary<string, Layout>
        {
            [PetFormData.PetNameField] = nameSlot,
            [PetFormData.BreedField] = breedSlot,
            [PetFormData.ColorField] = colorSlot
        };
        _textFields.Build(new[]
        {
            new AuthField(PetFormData.PetNameField, "Pet name *", "e.g. Bantay", AuthFieldKind.Text, maxLength: 50),
            new AuthField(PetFormData.BreedField, "Breed", "e.g. Aspin, Puspin, Shih Tzu", AuthFieldKind.Text, maxLength: 50),
            new AuthField(PetFormData.ColorField, "Color", "e.g. Brown and white", AuthFieldKind.Text, maxLength: 30)
        }, f => slots[f.Name]);
        _textFields.ValueChanged += (s, e) => ShowMessage(null);
        _textFields.SubmitRequested += (s, e) => SaveRequested?.Invoke(this, false);

        // Initial values ----------------------------------------------------------------------
        _textFields.SetValue(PetFormData.PetNameField, initial.PetName);
        _textFields.SetValue(PetFormData.BreedField, initial.Breed);
        _textFields.SetValue(PetFormData.ColorField, initial.Color);
        _weight.Text = initial.Weight;
        SelectSex(initial.Sex);
        if (initialOwner != null) _owner.Select(initialOwner);

        // Buttons ----------------------------------------------------------------------------
        SaveButton.Clicked += (s, e) => SaveRequested?.Invoke(this, false);
        SaveAndBookButton.Clicked += (s, e) => SaveRequested?.Invoke(this, true);
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

    /// <summary>Save pressed; the argument is true for "Save and book appointment".</summary>
    public event EventHandler<bool> SaveRequested;

    public event EventHandler CancelRequested;

    /// <summary>The owner search text changed (or the box was focused): the controller answers with <see cref="ShowOwnerResults"/>.</summary>
    public event EventHandler<string> OwnerSearchRequested;

    public PetFormData Read()
    {
        var values = _textFields.ReadValues();
        string Get(string name) => values.TryGetValue(name, out var v) ? v : null;

        return new PetFormData
        {
            PetId = _initial.PetId,
            OwnerId = _owner.SelectedOwner?.OwnerId ?? 0,
            OriginalOwnerId = _initial.OriginalOwnerId,
            PetName = Get(PetFormData.PetNameField),
            Species = _species.SelectedValue as string,
            Breed = Get(PetFormData.BreedField),
            Sex = _sex,
            BirthDate = _birthDate.Date,
            UseEstimatedAge = _unknownDate.IsChecked,
            AgeYears = _years.Text,
            AgeMonths = _months.Text,
            Color = Get(PetFormData.ColorField),
            Weight = _weight.Text
        };
    }

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Error(string field) => errors.TryGetValue(field, out var message) ? message : null;

        _textFields.ShowErrors(errors);
        _owner.SetError(Error(PetFormData.OwnerField));
        _species.SetError(Error(PetFormData.SpeciesField));
        SetError(_sexError, Error(PetFormData.SexField), null);
        foreach (var b in _sexButtons) UpdateSexButton(b.Sex, invalid: Error(PetFormData.SexField) != null);
        SetError(_birthError, Error(PetFormData.BirthDateField) ?? Error(PetFormData.AgeField),
            _unknownDate.IsChecked ? null : _birthDate.Box);
        SetError(_weightError, Error(PetFormData.WeightField), _weightBox);
        ShowMessage(result.Message);
    }

    public void ShowOwnerResults(IReadOnlyList<Owner> owners) => _owner.ShowResults(owners);

    public void ShowOwnerSearchError(string message) => _owner.ShowSearchError(message);

    public void SetBusy(bool busy)
    {
        SaveButton.IsEnabled = !busy;
        SaveAndBookButton.IsEnabled = !busy;
        SaveButton.Text = busy ? "Saving…" : "Save pet";
    }

    // Sex -----------------------------------------------------------------------------------

    private View SexButton(string sex, string icon)
    {
        var iconView = new LucideIcon { Icon = icon, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center };
        var text = new Label { Text = sex, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var button = new Border
        {
            HeightRequest = 48,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { iconView, text }
            }
        };
        SemanticProperties.SetDescription(button, sex);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            SelectSex(sex);
            SetError(_sexError, null, null);
            ShowMessage(null);
        };
        button.GestureRecognizers.Add(tap);
        HandCursor.Apply(button);

        _sexButtons.Add((sex, button, text, iconView));
        UpdateSexButton(sex, invalid: false);
        return button;
    }

    private void SelectSex(string sex)
    {
        _sex = PetSpecies.Sexes.Contains(sex) ? sex : null;
        foreach (var b in _sexButtons) UpdateSexButton(b.Sex, invalid: false);
    }

    /// <summary>Active = cyan background, navy bold text.</summary>
    private void UpdateSexButton(string sex, bool invalid)
    {
        var (_, button, text, icon) = _sexButtons.First(b => b.Sex == sex);
        bool active = _sex == sex;
        button.BackgroundColor = ThemeResources.Color(active ? "Cyan" : "InputBackground");
        button.Stroke = ThemeResources.Color(invalid ? "Orange" : active ? "Cyan" : "BorderColor");
        text.FontFamily = active ? "DMSansBold" : "DMSans";
        text.TextColor = ThemeResources.Color(active ? "Navy" : "BodyText");
        icon.StrokeColor = ThemeResources.Color(active ? "Navy" : "Muted");
    }

    // Birth date ----------------------------------------------------------------------------

    /// <summary>Swaps the date box for the Years / Months inputs (and back).</summary>
    private void ShowEstimate(bool estimate)
    {
        _birthDate.IsVisible = !estimate;
        _estimateRow.IsVisible = estimate;
        SetError(_birthError, null, _birthDate.Box);
        ShowMessage(null);

        // Start the estimate from the picked date, if any, so switching doesn't lose it.
        if (estimate && _birthDate.Date is DateTime picked && string.IsNullOrEmpty(_years.Text) && string.IsNullOrEmpty(_months.Text))
        {
            var (years, months) = AgeCalculator.YearsAndMonths(picked, DateTime.Today);
            _years.Text = years.ToString();
            _months.Text = months.ToString();
        }
        if (estimate) _years.Focus();
    }

    // Building blocks -----------------------------------------------------------------------

    private Entry NumberEntry(string placeholder)
    {
        var entry = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Placeholder = placeholder, Keyboard = Keyboard.Numeric, MaxLength = 2 };
        entry.TextChanged += (s, e) =>
        {
            SetError(entry == _weight ? _weightError : _birthError, null, entry == _weight ? _weightBox : null);
            ShowMessage(null);
        };
        entry.Completed += (s, e) => SaveRequested?.Invoke(this, false);
        return entry;
    }

    /// <summary>An input box with a gray unit on the right ("kg", "yrs").</summary>
    private static Border SuffixBox(Entry entry, string suffix)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        grid.Add(entry, 0, 0);
        grid.Add(new Label { Text = suffix, FontSize = 15, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, 1, 0);

        var box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => entry.Focus();
        box.GestureRecognizers.Add(tap);
        return box;
    }

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };
        return label;
    }

    private static Label ErrorLabel()
    {
        var label = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        label.StyleClass = new[] { "field-error" };
        return label;
    }

    /// <summary>Shows or clears an inline error; an invalid box gets the orange border.</summary>
    private static void SetError(Label error, string message, Border box)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        error.Text = message ?? string.Empty;
        error.IsVisible = invalid;
        if (box != null) box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }

    private void ShowMessage(string message)
    {
        MessageLabel.Text = message ?? string.Empty;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(message);
    }
}
