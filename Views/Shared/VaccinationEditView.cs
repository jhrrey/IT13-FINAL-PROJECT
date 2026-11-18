using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Same-day correction of a vaccination by the vet who gave it (rule 10): vaccine, dose no. and
/// next due date. Shown in the side panel; the controller validates and saves.
/// </summary>
public sealed class VaccinationEditView : ContentView
{
    public const string VaccineField = "vaccine";
    public const string DoseField = "dose";
    public const string NextDueField = "nextDue";
    public const double PanelWidth = 440;

    private readonly RecordVaccination _vaccination;
    private readonly IReadOnlyList<Medicine> _vaccines;
    private readonly PickerField _vaccine;
    private readonly Entry _dose;
    private readonly Border _doseBox;
    private readonly DateBox _nextDue;
    private readonly Label _doseError, _nextError, _message;
    private readonly HoverButton _save;

    public VaccinationEditView(RecordVaccination vaccination, IReadOnlyList<Medicine> vaccines)
    {
        _vaccination = vaccination;
        _vaccines = vaccines;

        var title = new Label { Text = "Edit vaccination", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
        var close = PageKit.IconButton("x", "Close", () => CancelRequested?.Invoke(this, EventArgs.Empty));
        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(title, 0, 0);
        header.Add(close, 1, 0);

        _vaccine = new PickerField(VaccineField, "Vaccine *");
        _vaccine.SetItems(vaccines.Select(m => (m.MedicineName + " · " + (m.MedicineId == vaccination.MedicineId ? "given" : m.StockText), (object)m.MedicineId)), vaccination.MedicineId);

        _dose = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Keyboard = Keyboard.Numeric, MaxLength = 2, Text = vaccination.DoseNo.ToString() };
        _doseBox = new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = _dose };
        _doseError = Error();
        _nextDue = new DateBox { MinimumDate = vaccination.DateGiven.AddDays(1), MaximumDate = DateTime.Today.AddYears(5), Date = vaccination.NextDueDate };
        _nextError = Error();
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        _dose.TextChanged += (s, e) => SetError(_doseError, null, _doseBox);
        _nextDue.DateChanged += (s, e) => SetError(_nextError, null, _nextDue.Box);

        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children =
            {
                new Label { Text = vaccination.PetName + " · given " + vaccination.DateGiven.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                _vaccine,
                new VerticalStackLayout { Children = { FieldLabel("Dose no. *"), _doseBox, _doseError } },
                new VerticalStackLayout { Children = { FieldLabel("Next due date"), _nextDue, Hint("Must be after the date given."), _nextError } },
                _message
            }
        };

        var cancel = new HoverButton { Text = "Cancel", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
        cancel.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _save = new HoverButton { Text = "Save changes", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        var footer = new Grid { Padding = new Thickness(24, 16), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        footer.Add(cancel, 0, 0);
        footer.Add(_save, 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End } } }, 0, 0);
        layout.Add(new ScrollView { Content = form }, 0, 1);
        layout.Add(new Grid { Children = { footer, new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.Start } } }, 0, 2);
        Content = layout;
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    /// <summary>The edited values (raw dose text is returned separately for validation).</summary>
    public (int MedicineId, string Dose, DateTime? NextDue) Read() =>
        (_vaccine.SelectedValue is int id ? id : 0, _dose.Text, _nextDue.Date);

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        _vaccine.SetError(Get(VaccineField));
        SetError(_doseError, Get(DoseField), _doseBox);
        SetError(_nextError, Get(NextDueField), _nextDue.Box);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Save changes";
    }

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };
        return label;
    }

    private static Label Hint(string text) => new() { Text = text, FontSize = 13, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(0, 6, 0, 0) };

    private static Label Error()
    {
        var label = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        label.StyleClass = new[] { "field-error" };
        return label;
    }

    private static void SetError(Label error, string message, Border box)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        error.Text = message ?? string.Empty;
        error.IsVisible = invalid;
        if (box != null) box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }
}
