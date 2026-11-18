using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Views.Veterinarian;

/// <summary>Everything the consultation page shows, gathered by the controller.</summary>
public sealed class ConsultationData
{
    public Appointment Appointment { get; init; }
    public Pet Pet { get; init; }

    /// <summary>The saved draft (or, when editing, the record); null for a new consultation.</summary>
    public MedicalRecord Record { get; init; }

    public IReadOnlyList<RecordPrescription> Prescriptions { get; init; } = Array.Empty<RecordPrescription>();
    public IReadOnlyList<RecordVaccination> Vaccinations { get; init; } = Array.Empty<RecordVaccination>();

    /// <summary>Every vaccine the pet received (for the status card and dose numbers).</summary>
    public IReadOnlyList<RecordVaccination> PetVaccinations { get; init; } = Array.Empty<RecordVaccination>();

    /// <summary>The last 5 records of the pet (not this one).</summary>
    public IReadOnlyList<MedicalRecord> PreviousVisits { get; init; } = Array.Empty<MedicalRecord>();

    public IReadOnlyList<Medicine> Medicines { get; init; } = Array.Empty<Medicine>();

    /// <summary>Editing a saved record (same day) instead of running the consultation.</summary>
    public bool EditMode { get; init; }
}

/// <summary>
/// The consultation page: the pet summary (read only) on the left and the consultation form on the
/// right — vital signs, diagnosis, treatment, notes, prescriptions, vaccinations and a follow-up.
/// Reports what was entered; the rules live in Controllers/MedicalRecordController.
/// </summary>
public partial class ConsultationPage : ContentView
{
    private sealed class RxRow
    {
        public int PrescriptionId;
        public bool IsDispensed;
        public View View;
        public Picker Medicine;
        public Entry Quantity, Dosage, Instructions;
        public Label Warning, Error;
        public List<Medicine> Options;
        public int FixedMedicineId;
    }

    private sealed class VxRow
    {
        public View View;
        public Picker Vaccine;
        public Entry Dose;
        public DateBox NextDue;
        public Label Error;
        public List<Medicine> Options;
    }

    private readonly ConsultationData _data;
    private readonly List<RxRow> _rx = new();
    private readonly List<VxRow> _vx = new();
    private readonly VerticalStackLayout _rxStack = new() { Spacing = 10 };
    private readonly VerticalStackLayout _vxStack = new() { Spacing = 10 };

    private Entry _weight, _temperature;
    private Border _weightBox, _temperatureBox;
    private Label _weightError, _temperatureError, _temperatureWarning;
    private Editor _diagnosis, _treatment, _notes;
    private Border _diagnosisBox, _treatmentBox;
    private Label _diagnosisError, _treatmentError;
    private CheckBox _followUp;
    private DateBox _followUpDate;
    private Switch _sameVet;
    private View _followUpOptions;
    private Label _followUpError;
    private IDispatcherTimer _timer;
    private bool _wide = true;

    public ConsultationPage(ConsultationData data)
    {
        InitializeComponent();
        _data = data;

        BackText.Text = data.EditMode ? "Medical records" : "Appointments";
        BackText.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        BackLink.GestureRecognizers.Add(backTap);
        HandCursor.Apply(BackLink);

        TitleLabel.Text = (data.EditMode ? "Edit record · " : "Consultation · ") + data.Pet.PetName;
        StatusHost.Content = Badges.AppointmentStatus(data.Appointment.Status);

        SecondaryButton.Text = data.EditMode ? "Cancel" : "Save draft";
        PrimaryButton.Text = data.EditMode ? "Save changes" : "Save & complete visit";
        SecondaryButton.Clicked += (s, e) =>
        {
            if (_data.EditMode) BackRequested?.Invoke(this, EventArgs.Empty);
            else SaveRequested?.Invoke(this, false);
        };
        PrimaryButton.Clicked += (s, e) => SaveRequested?.Invoke(this, !_data.EditMode);

        BuildLeft();
        BuildRight();
        Fill();

        if (!data.EditMode && data.Appointment.StartedAt != null)
        {
            UpdateTimer();
            Loaded += (s, e) =>
            {
                _timer = Dispatcher.CreateTimer();
                _timer.Interval = TimeSpan.FromSeconds(30);
                _timer.Tick += (t, a) => UpdateTimer();
                _timer.Start();
            };
            Unloaded += (s, e) => _timer?.Stop();
        }

        ScrollFocus.DontScrollOnFocusWhileOpening(PageScroll);
        SizeChanged += (s, e) => ApplyWidth(Width);
    }

    public event EventHandler BackRequested;

    /// <summary>Save pressed; true for "Save &amp; complete visit".</summary>
    public event EventHandler<bool> SaveRequested;

    /// <summary>"View full history →".</summary>
    public event EventHandler<int> PetHistoryRequested;

    public ConsultationFormData Read() => new()
    {
        Weight = _weight.Text,
        Temperature = _temperature.Text,
        Diagnosis = _diagnosis.Text,
        Treatment = _treatment.Text,
        Notes = _notes.Text,
        Prescriptions = _rx.Select(r => new PrescriptionRow(
            r.PrescriptionId,
            r.IsDispensed ? r.FixedMedicineId : r.Medicine.SelectedIndex >= 0 ? r.Options[r.Medicine.SelectedIndex].MedicineId : 0,
            r.Quantity.Text, r.Dosage.Text, r.Instructions.Text, r.IsDispensed)).ToList(),
        Vaccinations = _vx.Select(v => new VaccinationRow(
            v.Vaccine.SelectedIndex >= 0 ? v.Options[v.Vaccine.SelectedIndex].MedicineId : 0,
            v.Dose.Text, v.NextDue.Date)).ToList(),
        FollowUp = _followUp?.IsChecked == true,
        FollowUpDate = _followUpDate?.Date,
        SameVet = _sameVet?.IsToggled != false
    };

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Error(string key) => errors.TryGetValue(key, out var m) ? m : null;

        SetError(_weightError, Error(ConsultationFormData.WeightField), _weightBox);
        SetError(_temperatureError, Error(ConsultationFormData.TemperatureField), _temperatureBox);
        SetError(_diagnosisError, Error(ConsultationFormData.DiagnosisField), _diagnosisBox);
        SetError(_treatmentError, Error(ConsultationFormData.TreatmentField), _treatmentBox);
        for (int i = 0; i < _rx.Count; i++) SetError(_rx[i].Error, Error(ConsultationFormData.PrescriptionField(i)), null);
        for (int i = 0; i < _vx.Count; i++) SetError(_vx[i].Error, Error(ConsultationFormData.VaccinationField(i)), null);
        if (_followUpError != null) SetError(_followUpError, Error(ConsultationFormData.FollowUpField), null);
        SavedLabel.Text = result.Message ?? string.Empty;
        SavedLabel.TextColor = ThemeResources.Color(result.Ok ? "Muted" : "Danger");
    }

    /// <summary>"Draft saved 9:41 AM".</summary>
    public void ShowSaved(DateTime when)
    {
        ShowResult(OperationResult.Success("Draft saved " + when.ToString("h:mm tt", CultureInfo.InvariantCulture)));
    }

    public void SetBusy(bool busy)
    {
        PrimaryButton.IsEnabled = !busy;
        SecondaryButton.IsEnabled = !busy;
    }

    // Left column: pet summary ----------------------------------------------------------------------

    private void BuildLeft()
    {
        var pet = _data.Pet;
        var a = _data.Appointment;

        // Pet card
        var chips = new WrapLayout { Margin = new Thickness(0, 10, 0, 0) };
        if (pet.AgeText != null) chips.Children.Add(Chip("cake", pet.AgeText));
        if (pet.WeightText != null) chips.Children.Add(Chip("weight", "Last weight " + pet.WeightText));
        var petGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14 };
        petGrid.Add(PetsPage.SpeciesAvatar(pet.Species, 64), 0, 0);
        petGrid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = pet.PetName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = string.Join(" · ", new[] { pet.Species, pet.Breed, pet.Sex }.Where(t => !string.IsNullOrWhiteSpace(t))), FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
            }
        }, 1, 0);
        LeftColumn.Children.Add(Card(new VerticalStackLayout { Children = { petGrid, chips } }));

        // Owner card
        LeftColumn.Children.Add(Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Small("OWNER"),
                new Label { Text = pet.OwnerName, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                IconLine("phone", pet.OwnerContactDisplay)
            }
        }));

        // Visit card
        LeftColumn.Children.Add(Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Small("VISIT"),
                IconLine("calendar", a.AppointmentDate.ToString("MMM d, yyyy") + " · " + a.TimeText),
                AppointmentCards.ReasonPill(a.Reason),
                IconLine("stethoscope", a.VetName)
            }
        }));

        // Vaccination status: the latest dose of each vaccine
        var status = new VerticalStackLayout { Spacing = 10 };
        var latest = _data.PetVaccinations.GroupBy(v => v.MedicineId).Select(g => g.First()).ToList();
        if (latest.Count == 0)
            status.Children.Add(new Label { Text = "No vaccines recorded yet.", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        foreach (var v in latest)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
            row.Add(new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = v.VaccineName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = "Dose " + v.DoseNo + (v.NextDueDate != null ? " · Due " + v.NextDueDate.Value.ToString("MMM dd, yyyy") : string.Empty), FontSize = 13, TextColor = ThemeResources.Color("Muted") }
                }
            }, 0, 0);
            row.Add(VaccineStatusPill(v.NextDueDate), 1, 0);
            status.Children.Add(row);
        }
        LeftColumn.Children.Add(Card(new VerticalStackLayout { Spacing = 10, Children = { Small("VACCINATION STATUS"), status } }));

        // Previous visits (last 5), each expandable
        var visits = new VerticalStackLayout { Spacing = 0 };
        if (_data.PreviousVisits.Count == 0)
            visits.Children.Add(new Label { Text = "This is the first recorded visit.", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        for (int i = 0; i < _data.PreviousVisits.Count; i++)
            visits.Children.Add(VisitItem(_data.PreviousVisits[i], last: i == _data.PreviousVisits.Count - 1));

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(Small("PREVIOUS VISITS"), 0, 0);
        header.Add(Badges.Pill(_data.PreviousVisits.Count.ToString(), "Navy", "CyanSoft"), 1, 0);
        var history = new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new Label { Text = "View full history", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center },
                new LucideIcon { Icon = "arrow-right", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center }
            }
        };
        AddTap(history, () => PetHistoryRequested?.Invoke(this, pet.PetId));
        LeftColumn.Children.Add(Card(new VerticalStackLayout { Spacing = 10, Children = { header, visits, history } }));
    }

    /// <summary>Date, diagnosis (bold) and vet; tapping shows the treatment and vital signs.</summary>
    private static View VisitItem(MedicalRecord r, bool last)
    {
        var details = new VerticalStackLayout
        {
            IsVisible = false,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { new Label { Text = r.Treatment, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") } }
        };
        string vitals = string.Join(" · ", new[] { r.WeightText, r.TemperatureText }.Where(t => t != null));
        if (vitals.Length > 0) details.Children.Add(new Label { Text = vitals, FontSize = 13, TextColor = ThemeResources.Color("Muted") });

        var text = new VerticalStackLayout
        {
            Padding = new Thickness(0, 0, 0, last ? 0 : 14),
            Children =
            {
                new Label { Text = r.RecordDate.ToString("MMM d, yyyy"), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") },
                new Label { Text = r.Diagnosis, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = r.VetName, FontSize = 13, TextColor = ThemeResources.Color("Muted") },
                details
            }
        };

        var rail = new Grid { WidthRequest = 14 };
        if (!last) rail.Children.Add(new BoxView { WidthRequest = 2, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 12, 0, 0) });
        rail.Children.Add(new Ellipse { WidthRequest = 9, HeightRequest = 9, Fill = ThemeResources.Color("Orange"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4, 0, 0) });

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        row.Add(rail, 0, 0);
        row.Add(text, 1, 0);
        AddTap(row, () => details.IsVisible = !details.IsVisible);
        return row;
    }

    // Right column: consultation form --------------------------------------------------------------

    private void BuildRight()
    {
        // 1. Vital signs
        (_weightBox, _weight) = SuffixInput("e.g. 12.5", "kg", 6);
        (_temperatureBox, _temperature) = SuffixInput("e.g. 38.6", "°C", 4);
        _weightError = ErrorLabel();
        _temperatureError = ErrorLabel();
        _temperatureWarning = new Label { Text = "Outside normal range", FontFamily = "DMSansMedium", FontSize = 13, TextColor = ThemeResources.Color("Warning"), IsVisible = false };
        _weight.TextChanged += (s, e) => SetError(_weightError, null, _weightBox);
        _temperature.TextChanged += (s, e) =>
        {
            SetError(_temperatureError, null, _temperatureBox);
            UpdateTemperatureWarning();
        };

        string lastWeight = _data.Pet.WeightText;
        string normal = VitalSignsHelper.NormalTemperatureText(_data.Pet.Species);
        var vitals = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14 };
        vitals.Add(new VerticalStackLayout { Children = { FieldLabel("Weight"), _weightBox, Hint(lastWeight != null ? "Last: " + lastWeight : "No weight recorded yet"), _weightError } }, 0, 0);
        vitals.Add(new VerticalStackLayout { Children = { FieldLabel("Temperature"), _temperatureBox, Hint(normal ?? "No reference range for this species"), _temperatureWarning, _temperatureError } }, 1, 0);
        RightColumn.Children.Add(Section("activity", "Vital signs", vitals));

        // 2–4. Diagnosis, treatment, notes
        (_diagnosisBox, _diagnosis) = TextArea("What did you find?", 110);
        (_treatmentBox, _treatment) = TextArea("What was done or given?", 110);
        var (notesBox, notes) = TextArea("Advice for the owner, observations, follow-up instructions…", 86);
        _notes = notes;
        _diagnosisError = ErrorLabel();
        _treatmentError = ErrorLabel();
        _diagnosis.TextChanged += (s, e) => SetError(_diagnosisError, null, _diagnosisBox);
        _treatment.TextChanged += (s, e) => SetError(_treatmentError, null, _treatmentBox);
        RightColumn.Children.Add(Section("search-check", "Diagnosis *", new VerticalStackLayout { Children = { _diagnosisBox, _diagnosisError } }));
        RightColumn.Children.Add(Section("bandage", "Treatment *", new VerticalStackLayout { Children = { _treatmentBox, _treatmentError } }));
        RightColumn.Children.Add(Section("notebook-pen", "Notes", notesBox));

        // 5. Prescriptions
        var rxHeader = ColumnHeader(new[] { ("Medicine", 0), ("Qty", 1), ("Dosage", 2), ("Instructions", 3) }, RxColumns());
        RightColumn.Children.Add(Section("pill", "Prescriptions", new VerticalStackLayout
        {
            Spacing = 10,
            Children = { rxHeader, _rxStack, DashedButton("Add medicine", () => AddRxRow(null)) }
        }));

        // 6. Vaccinations
        var vxHeader = ColumnHeader(new[] { ("Vaccine", 0), ("Dose no.", 1), ("Next due", 2) }, VxColumns());
        RightColumn.Children.Add(Section("syringe", "Vaccinations", new VerticalStackLayout
        {
            Spacing = 10,
            Children = { vxHeader, _vxStack, DashedButton("Add vaccine", () => AddVxRow(null)) }
        }));

        // 7. Follow-up (not when editing a finished record)
        if (!_data.EditMode)
        {
            _followUp = new CheckBox { Color = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center };
            var followLabel = new Label { Text = "Book a follow-up visit", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
            AddTap(followLabel, () => _followUp.IsChecked = !_followUp.IsChecked);
            _followUpDate = new DateBox { MinimumDate = DateTime.Today.AddDays(1), MaximumDate = DateTime.Today.AddYears(1), WidthRequest = 220, HorizontalOptions = LayoutOptions.Start };
            _sameVet = new Switch { IsToggled = true, OnColor = ThemeResources.Color("Cyan"), ThumbColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
            _followUpError = ErrorLabel();
            _followUpOptions = new HorizontalStackLayout
            {
                IsVisible = false,
                Spacing = 16,
                Children =
                {
                    _followUpDate,
                    new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { _sameVet, new Label { Text = "Same vet", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center } } }
                }
            };
            _followUp.CheckedChanged += (s, e) =>
            {
                _followUpOptions.IsVisible = e.Value;
                SetError(_followUpError, null, null);
                if (e.Value && _followUpDate.Date == null) _followUpDate.Date = DateTime.Today.AddDays(7);
            };
            _followUpDate.DateChanged += (s, e) => SetError(_followUpError, null, null);
            RightColumn.Children.Add(Section("calendar-plus", "Follow-up", new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new HorizontalStackLayout { Spacing = 2, Margin = new Thickness(-6, 0, 0, 0), Children = { _followUp, followLabel } },
                    _followUpOptions,
                    _followUpError,
                    Hint("Booked in the first free slot of that day; the time shows after saving.")
                }
            }));
        }
    }

    /// <summary>Puts the saved draft (or the record being edited) into the form.</summary>
    private void Fill()
    {
        var r = _data.Record;
        if (r != null)
        {
            _weight.Text = r.Weight?.ToString("0.##", CultureInfo.InvariantCulture);
            _temperature.Text = r.Temperature?.ToString("0.0", CultureInfo.InvariantCulture);
            _diagnosis.Text = r.Diagnosis;
            _treatment.Text = r.Treatment;
            _notes.Text = r.Notes;
        }
        foreach (var p in _data.Prescriptions) AddRxRow(p);
        foreach (var v in _data.Vaccinations) AddVxRow(v);
        UpdateTemperatureWarning();
    }

    // Prescription rows ------------------------------------------------------------------------------

    private static ColumnDefinitionCollection RxColumns() => new(
        new ColumnDefinition(new GridLength(2.2, GridUnitType.Star)), new ColumnDefinition(new GridLength(70)),
        new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)), new ColumnDefinition(new GridLength(40)));

    private static ColumnDefinitionCollection VxColumns() => new(
        new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(90)),
        new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)), new ColumnDefinition(new GridLength(40)));

    private void AddRxRow(RecordPrescription existing)
    {
        var row = new RxRow
        {
            PrescriptionId = existing?.PrescriptionId ?? 0,
            IsDispensed = existing?.IsDispensed == true,
            FixedMedicineId = existing?.MedicineId ?? 0,
            Options = _data.Medicines.Where(m => !m.IsVaccine).ToList(),
            Warning = new Label { FontFamily = "DMSansMedium", FontSize = 13, TextColor = ThemeResources.Color("Warning"), IsVisible = false },
            Error = ErrorLabel()
        };

        var grid = new Grid { ColumnDefinitions = RxColumns(), ColumnSpacing = 8 };
        if (row.IsDispensed)
        {
            // Already dispensed: shown, but no longer editable.
            grid.Add(Cell(existing.MedicineName), 0, 0);
            grid.Add(Cell(existing.Quantity.ToString()), 1, 0);
            grid.Add(Cell(existing.Dosage ?? "—"), 2, 0);
            grid.Add(Cell(existing.Instructions ?? "—"), 3, 0);
            var dispensed = new LucideIcon { Icon = "circle-check", StrokeColor = ThemeResources.Color("Success"), WidthRequest = 20, HeightRequest = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            ToolTipProperties.SetText(dispensed, "Already dispensed — can’t be changed");
            grid.Add(dispensed, 4, 0);
            row.Medicine = new Picker();
            row.Quantity = new Entry { Text = existing.Quantity.ToString() };
            row.Dosage = new Entry { Text = existing.Dosage };
            row.Instructions = new Entry { Text = existing.Instructions };
        }
        else
        {
            row.Medicine = SmallPicker(row.Options.Select(m => m.MedicineName + " · " + m.StockText));
            row.Quantity = SmallEntry("0", Keyboard.Numeric, 5);
            row.Dosage = SmallEntry("e.g. 1 tab", Keyboard.Default, 100);
            row.Instructions = SmallEntry("e.g. Twice a day for 7 days", Keyboard.Default, 255);
            if (existing != null)
            {
                row.Medicine.SelectedIndex = row.Options.FindIndex(m => m.MedicineId == existing.MedicineId);
                row.Quantity.Text = existing.Quantity.ToString();
                row.Dosage.Text = existing.Dosage;
                row.Instructions.Text = existing.Instructions;
            }

            row.Medicine.SelectedIndexChanged += (s, e) => { SetError(row.Error, null, null); UpdateStockWarning(row); };
            row.Quantity.TextChanged += (s, e) => { SetError(row.Error, null, null); UpdateStockWarning(row); };

            grid.Add(Boxed(row.Medicine), 0, 0);
            grid.Add(Boxed(row.Quantity), 1, 0);
            grid.Add(Boxed(row.Dosage), 2, 0);
            grid.Add(Boxed(row.Instructions), 3, 0);
            var remove = PageKit.IconButton("trash-2", "Remove medicine", () => RemoveRxRow(row));
            grid.Add(remove, 4, 0);
        }

        row.View = new VerticalStackLayout { Spacing = 4, Children = { grid, row.Warning, row.Error } };
        _rx.Add(row);
        _rxStack.Children.Add(row.View);
        UpdateStockWarning(row);
    }

    private void RemoveRxRow(RxRow row)
    {
        _rx.Remove(row);
        _rxStack.Children.Remove(row.View);
    }

    /// <summary>Rule 8: "Only 5 in stock — can't be dispensed yet."</summary>
    private static void UpdateStockWarning(RxRow row)
    {
        if (row.IsDispensed || row.Medicine.SelectedIndex < 0) { row.Warning.IsVisible = false; return; }
        var medicine = row.Options[row.Medicine.SelectedIndex];
        bool over = int.TryParse(row.Quantity.Text, out int qty) && qty > medicine.StockQty;
        row.Warning.Text = "Only " + medicine.StockQty + " in stock — can’t be dispensed yet.";
        row.Warning.IsVisible = over;
    }

    // Vaccination rows -------------------------------------------------------------------------------

    private void AddVxRow(RecordVaccination existing)
    {
        var row = new VxRow
        {
            Options = _data.Medicines.Where(m => m.IsVaccine).ToList(),
            Error = ErrorLabel()
        };
        row.Vaccine = SmallPicker(row.Options.Select(m => m.MedicineName + " · " + m.StockText));
        row.Dose = SmallEntry("1", Keyboard.Numeric, 2);
        row.NextDue = new DateBox { MinimumDate = DateTime.Today.AddDays(1), MaximumDate = DateTime.Today.AddYears(5) };
        row.NextDue.Box.HeightRequest = 44;

        if (existing != null)
        {
            row.Vaccine.SelectedIndex = row.Options.FindIndex(m => m.MedicineId == existing.MedicineId);
            row.Dose.Text = existing.DoseNo.ToString();
            row.NextDue.Date = existing.NextDueDate;
        }

        // Vaccination module (screen B): picking a vaccine fills in the next dose number (last dose + 1)
        // and suggests the next due date from the vaccine type; the vet can change both.
        var hint = new Label { FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), IsVisible = false };
        bool dateChangedByVet = existing != null;
        bool settingDate = false;

        void Suggest()
        {
            if (row.Vaccine.SelectedIndex < 0) { hint.IsVisible = false; return; }
            var vaccine = row.Options[row.Vaccine.SelectedIndex];
            var last = _data.PetVaccinations
                .Where(v => v.MedicineId == vaccine.MedicineId && v.RecordId != _data.Record?.RecordId)
                .OrderByDescending(v => v.DateGiven).FirstOrDefault();
            int dose = int.TryParse(row.Dose.Text, out int typed) && typed > 0 ? typed : 1;
            var (date, suggested) = VaccineScheduleHelper.SuggestNextDue(vaccine.MedicineName, dose, DateTime.Today);
            if (!dateChangedByVet)
            {
                settingDate = true;
                row.NextDue.Date = date;
                settingDate = false;
            }
            string lastText = last == null ? "First dose" : "Last: Dose " + last.DoseNo + " on " + last.DateGiven.ToString("MMM d");
            hint.Text = lastText + " · " + suggested + (vaccine.InStock ? string.Empty : " · Out of stock");
            hint.TextColor = ThemeResources.Color(vaccine.InStock ? "Muted" : "Danger");
            hint.IsVisible = true;
        }

        row.Vaccine.SelectedIndexChanged += (s, e) =>
        {
            SetError(row.Error, null, null);
            if (row.Vaccine.SelectedIndex < 0) return;
            int medicineId = row.Options[row.Vaccine.SelectedIndex].MedicineId;
            int given = _data.PetVaccinations.Count(v => v.MedicineId == medicineId && v.RecordId != _data.Record?.RecordId);
            row.Dose.Text = (given + 1).ToString();
            Suggest();
        };
        row.Dose.TextChanged += (s, e) => { SetError(row.Error, null, null); Suggest(); };
        row.NextDue.DateChanged += (s, e) =>
        {
            SetError(row.Error, null, null);
            if (!settingDate) dateChangedByVet = true;
        };

        var grid = new Grid { ColumnDefinitions = VxColumns(), ColumnSpacing = 8 };
        grid.Add(Boxed(row.Vaccine), 0, 0);
        grid.Add(Boxed(row.Dose), 1, 0);
        grid.Add(row.NextDue, 2, 0);
        grid.Add(PageKit.IconButton("trash-2", "Remove vaccine", () =>
        {
            _vx.Remove(row);
            _vxStack.Children.Remove(row.View);
        }), 3, 0);

        row.View = new VerticalStackLayout { Spacing = 4, Children = { grid, hint, row.Error } };
        _vx.Add(row);
        _vxStack.Children.Add(row.View);
        if (existing != null) Suggest();
    }

    // Helpers ----------------------------------------------------------------------------------------

    private void UpdateTemperatureWarning()
    {
        bool parsed = decimal.TryParse(_temperature.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var t);
        _temperatureWarning.IsVisible = parsed && VitalSignsHelper.IsOutsideNormal(_data.Pet.Species, t);
    }

    private void UpdateTimer()
    {
        var started = _data.Appointment.StartedAt;
        if (started == null) return;
        int minutes = Math.Max(0, (int)(DateTime.Now - started.Value).TotalMinutes);
        TimerLabel.Text = "Started " + started.Value.ToString("h:mm tt", CultureInfo.InvariantCulture) + " · " +
                          (minutes >= 60 ? minutes / 60 + " h " + minutes % 60 + " min" : minutes + " min");
    }

    private static View VaccineStatusPill(DateTime? nextDue)
    {
        if (nextDue == null) return Badges.Pill("Up to date", "CompletedText", "Mint");
        var today = DateTime.Today;
        if (nextDue.Value.Date < today) return Badges.Pill("Overdue", "Orange", "OrangeSoft");
        if (nextDue.Value.Date <= today.AddDays(30)) return Badges.Pill("Due soon", "Warning", "WarningSoft");
        return Badges.Pill("Up to date", "CompletedText", "Mint");
    }

    /// <summary>A white card with a title row (icon + bold navy name) above <paramref name="content"/>.</summary>
    private static View Section(string icon, string title, View content) => Card(new VerticalStackLayout
    {
        Spacing = 14,
        Children =
        {
            new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = title, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
                }
            },
            content
        }
    });

    private static Border Card(View content) => new()
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = new Thickness(20),
        Content = content
    };

    private static View ColumnHeader(IEnumerable<(string Text, int Column)> titles, ColumnDefinitionCollection columns)
    {
        var grid = new Grid { ColumnDefinitions = columns, ColumnSpacing = 8 };
        foreach (var (text, column) in titles)
            grid.Add(new Label { Text = text.ToUpperInvariant(), FontFamily = "DMSansBold", FontSize = 11.5, CharacterSpacing = 0.6, TextColor = ThemeResources.Color("SectionLabel") }, column, 0);
        return grid;
    }

    private static View DashedButton(string text, Action onTap)
    {
        var button = new Border
        {
            HeightRequest = 42,
            BackgroundColor = Colors.Transparent,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = "plus", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        AddTap(button, onTap);
        return button;
    }

    private static (Border Box, Entry Entry) SuffixInput(string placeholder, string suffix, int maxLength)
    {
        var entry = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Placeholder = placeholder, Keyboard = Keyboard.Numeric, MaxLength = maxLength };
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        grid.Add(entry, 0, 0);
        grid.Add(new Label { Text = suffix, FontSize = 15, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        var box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = grid };
        AddTap(box, () => entry.Focus(), hand: false);
        return (box, entry);
    }

    private static (Border Box, Editor Editor) TextArea(string placeholder, double height)
    {
        var editor = new Editor
        {
            FontFamily = "DMSans",
            FontSize = 15,
            TextColor = ThemeResources.Color("Navy"),
            PlaceholderColor = ThemeResources.Color("Placeholder"),
            BackgroundColor = Colors.Transparent,
            Placeholder = placeholder,
            AutoSize = EditorAutoSizeOption.Disabled
        };
        var box = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = height, Padding = new Thickness(12, 6), Content = editor };
        return (box, editor);
    }

    private static Picker SmallPicker(IEnumerable<string> items)
    {
        var picker = new Picker { FontFamily = "DMSans", FontSize = 14, TextColor = ThemeResources.Color("Navy"), BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Center };
        foreach (var item in items) picker.Items.Add(item);
        return picker;
    }

    private static Entry SmallEntry(string placeholder, Keyboard keyboard, int maxLength) => new()
    {
        Style = ThemeResources.Get<Style>("InputEntry"),
        FontSize = 14,
        Placeholder = placeholder,
        Keyboard = keyboard,
        MaxLength = maxLength
    };

    private static Border Boxed(View input) => new()
    {
        Style = ThemeResources.Get<Style>("InputBox"),
        HeightRequest = 44,
        Padding = new Thickness(input is Picker ? 4 : 10, 0),
        Content = input
    };

    private static Label Cell(string text) => new() { Text = text, FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };

    private static View Chip(string icon, string text) => new Border
    {
        Padding = new Thickness(10, 5),
        Margin = new Thickness(0, 0, 8, 8),
        BackgroundColor = ThemeResources.Color("InputBackground"),
        Stroke = ThemeResources.Color("BorderColor"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        Content = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("NavyMuted"), WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontFamily = "DMSansMedium", FontSize = 13, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
            }
        }
    };

    private static View IconLine(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 8,
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
        }
    };

    private static Label Small(string text) => new()
    {
        Text = text,
        FontFamily = "DMSansBold",
        FontSize = 12,
        CharacterSpacing = 0.67,
        TextColor = ThemeResources.Color("Muted"),
        VerticalOptions = LayoutOptions.Center
    };

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };
        return label;
    }

    private static Label Hint(string text) => new() { Text = text, FontSize = 13, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(0, 6, 0, 0) };

    private static Label ErrorLabel()
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

    private static void AddTap(View view, Action action, bool hand = true)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => action();
        view.GestureRecognizers.Add(tap);
        if (hand) HandCursor.Apply(view);
    }

    /// <summary>On narrow windows the pet summary moves above the form.</summary>
    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        bool wide = width >= 880;
        if (wide == _wide) return;
        _wide = wide;
        ColumnsGrid.ColumnDefinitions = wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(new GridLength(340)), new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
        ColumnsGrid.RowDefinitions = wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        Grid.SetColumn(RightColumn, wide ? 1 : 0);
        Grid.SetRow(RightColumn, wide ? 0 : 1);
    }
}
