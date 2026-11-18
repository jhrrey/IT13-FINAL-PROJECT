using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>Everything the pet profile shows, gathered by the controller.</summary>
public sealed class PetProfile
{
    public Pet Pet { get; init; }
    public PetStats Stats { get; init; }
    public IReadOnlyList<PetVisit> Visits { get; init; } = Array.Empty<PetVisit>();
    public IReadOnlyList<PetVaccination> Vaccinations { get; init; } = Array.Empty<PetVaccination>();
    public IReadOnlyList<PetAppointment> Appointments { get; init; } = Array.Empty<PetAppointment>();
    public IReadOnlyList<PetPrescription> Prescriptions { get; init; } = Array.Empty<PetPrescription>();
    public IReadOnlyList<PetWeight> Weights { get; init; } = Array.Empty<PetWeight>();

    // False until that module's table exists (changes the empty-state wording).
    public bool MedicalRecordsAvailable { get; init; }
    public bool AppointmentsAvailable { get; init; }
    public bool VaccinationsAvailable { get; init; }
    public bool PrescriptionsAvailable { get; init; }

    /// <summary>Cashier/Staff: true. Veterinarian and Administrator (view only): false.</summary>
    public bool CanEdit { get; init; }

    /// <summary>The signed-in role may open the Owners module (the "View owner" link).</summary>
    public bool CanOpenOwner { get; init; }

    /// <summary>Show the "Vaccination card" button on the Vaccinations tab.</summary>
    public bool CanOpenVaccinationCard { get; init; }
}

/// <summary>
/// The pet profile page: header and owner cards, quick stats, and the Medical history /
/// Vaccinations / Appointments / Prescriptions / Weight trend tabs.
/// </summary>
public partial class PetDetailsPage : ContentView
{
    private readonly List<(Label Label, BoxView Line)> _tabs = new();
    private PetProfile _profile;
    private int _activeTab;
    private bool _wide = true;

    public PetDetailsPage()
    {
        InitializeComponent();

        BackText.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        BackLink.GestureRecognizers.Add(backTap);

        ViewOwnerText.Clicked += (s, e) => OwnerRequested?.Invoke(this, _profile?.Pet.OwnerId ?? 0);
        var ownerTap = new TapGestureRecognizer();
        ownerTap.Tapped += (s, e) => OwnerRequested?.Invoke(this, _profile?.Pet.OwnerId ?? 0);
        ViewOwnerLink.GestureRecognizers.Add(ownerTap);
        HandCursor.Apply(ViewOwnerLink);

        EditButton.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
        BookButton.Clicked += (s, e) => BookRequested?.Invoke(this, EventArgs.Empty);

        AddTab("Medical history", 0);
        AddTab("Vaccinations", 1);
        AddTab("Appointments", 2);
        AddTab("Prescriptions", 3);
        AddTab("Weight trend", 4);

        SizeChanged += (s, e) => ApplyWidth(Width);
    }

    public event EventHandler BackRequested;
    public event EventHandler EditRequested;
    public event EventHandler BookRequested;

    /// <summary>"View owner →" was clicked (argument: the OwnerID).</summary>
    public event EventHandler<int> OwnerRequested;

    /// <summary>A consultation on the Medical history timeline was clicked (argument: the RecordID).</summary>
    public event EventHandler<int> RecordRequested;

    /// <summary>"Vaccination card" on the Vaccinations tab (argument: the PetID).</summary>
    public event EventHandler<int> VaccinationCardRequested;

    public void Show(PetProfile profile)
    {
        _profile = profile;
        var pet = profile.Pet;

        AvatarHost.Content = PetsPage.SpeciesAvatar(pet.Species, 72);
        NameLabel.Text = pet.PetName;
        SpeciesLabel.Text = pet.SpeciesAndBreed;
        SyncHost.Content = Badges.Sync(pet.SyncStatus);

        ChipsRow.Children.Clear();
        ChipsRow.Children.Add(InfoChip(pet.Sex == PetSpecies.Female ? "venus" : "mars", pet.Sex));
        if (pet.AgeText != null) ChipsRow.Children.Add(InfoChip("cake", pet.AgeText));
        if (!string.IsNullOrWhiteSpace(pet.Color)) ChipsRow.Children.Add(InfoChip("paw-print", pet.Color));
        if (pet.WeightText != null) ChipsRow.Children.Add(InfoChip("weight", pet.WeightText));

        ButtonsRow.IsVisible = profile.CanEdit;

        OwnerNameLabel.Text = pet.OwnerName;
        OwnerContactLabel.Text = pet.OwnerContactDisplay;
        ViewOwnerLink.IsVisible = profile.CanOpenOwner;

        var stats = profile.Stats;
        StatsRow.Children.Clear();
        StatsRow.Children.Add(StatChip("Last visit", stats.LastVisit?.ToString("MMM d, yyyy") ?? "None"));
        StatsRow.Children.Add(StatChip("Next appointment", stats.NextAppointment?.ToString("MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture) ?? "None"));
        StatsRow.Children.Add(StatChip("Next vaccine", stats.NextVaccineDue?.ToString("MMM dd, yyyy") ?? "None"));
        StatsRow.Children.Add(StatChip("Total visits", stats.TotalVisits.ToString()));

        SelectTab(_activeTab);
    }

    // Header and stats ----------------------------------------------------------------------

    private static View InfoChip(string icon, string text) => new Border
    {
        Padding = new Thickness(12, 6),
        Margin = new Thickness(0, 0, 8, 8),
        BackgroundColor = ThemeResources.Color("InputBackground"),
        Stroke = ThemeResources.Color("BorderColor"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 16 },
        Content = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("NavyMuted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
            }
        }
    };

    private static View StatChip(string label, string value) => new Border
    {
        Padding = new Thickness(14, 8),
        Margin = new Thickness(0, 0, 10, 10),
        BackgroundColor = ThemeResources.Color("Surface"),
        Stroke = ThemeResources.Color("BorderColor"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 18 },
        Content = new Label
        {
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = label + ": ", FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                    new Span { Text = value, FontSize = 14, FontFamily = "DMSansBold", TextColor = ThemeResources.Color("Navy") }
                }
            }
        }
    };

    // Tabs -----------------------------------------------------------------------------------

    private void AddTab(string text, int index)
    {
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var line = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End };
        var tab = new Grid { HeightRequest = 40, Children = { label, line } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectTab(index);
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);

        _tabs.Add((label, line));
        TabsRow.Children.Add(tab);
    }

    private void SelectTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == index;
            _tabs[i].Label.FontFamily = active ? "DMSansBold" : "DMSansMedium";
            _tabs[i].Label.TextColor = ThemeResources.Color(active ? "Orange" : "BodyText");
            _tabs[i].Line.Color = active ? ThemeResources.Color("Orange") : Colors.Transparent;
        }

        if (_profile == null) return;
        TabContent.Content = index switch
        {
            0 => MedicalHistoryTab(),
            1 => VaccinationsTab(),
            2 => AppointmentsTab(),
            3 => PrescriptionsTab(),
            _ => WeightTab()
        };
    }

    /// <summary>The empty state of a tab whose module isn't built yet (or that simply has no rows).</summary>
    private View Empty(string icon, string title, string comingWith)
    {
        var (module, available) = comingWith switch
        {
            "Vaccines" => ("Vaccinations", _profile.VaccinationsAvailable),
            "Visits" => ("Appointments", _profile.AppointmentsAvailable),
            "Medicines" => ("Prescriptions", _profile.PrescriptionsAvailable),
            _ => ("Medical records", _profile.MedicalRecordsAvailable)
        };
        return Card(PageKit.EmptyState(icon, title,
            available ? null : comingWith + " will appear here once the " + module + " module is added."));
    }

    /// <summary>Vertical timeline: a dot on the left line, then a card per consultation.</summary>
    private View MedicalHistoryTab()
    {
        if (_profile.Visits.Count == 0) return Empty("stethoscope", "No consultations yet", "Consultations");

        var timeline = new VerticalStackLayout { Spacing = 0 };
        foreach (var visit in _profile.Visits)
        {
            var chips = new WrapLayout { Margin = new Thickness(0, 6, 0, 0) };
            if (visit.Weight != null) chips.Children.Add(InfoChip("weight", visit.Weight.Value.ToString("0.##") + " kg"));
            if (visit.Temperature != null) chips.Children.Add(InfoChip("heart-pulse", visit.Temperature.Value.ToString("0.0") + " °C"));

            var card = new Border
            {
                Style = ThemeResources.Get<Style>("WorkspaceCard"),
                Padding = new Thickness(18, 14),
                Margin = new Thickness(0, 0, 0, 14),
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = visit.Date.ToString("MMM d, yyyy") + " · " + visit.VetName, FontSize = 13, TextColor = ThemeResources.Color("Muted") },
                        new Label { Text = visit.Diagnosis, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                        new Label { Text = visit.Treatment, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), IsVisible = !string.IsNullOrWhiteSpace(visit.Treatment) },
                        chips
                    }
                }
            };

            // Clicking an entry opens the full record.
            int recordId = visit.RecordId;
            var openRecord = new TapGestureRecognizer();
            openRecord.Tapped += (s, e) => RecordRequested?.Invoke(this, recordId);
            card.GestureRecognizers.Add(openRecord);
            HandCursor.Apply(card);

            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(28)), new ColumnDefinition(GridLength.Star) }
            };
            row.Add(new BoxView { WidthRequest = 2, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(new Ellipse
            {
                WidthRequest = 12,
                HeightRequest = 12,
                Fill = ThemeResources.Color("Orange"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 18, 0, 0)
            }, 0, 0);
            row.Add(card, 1, 0);
            timeline.Children.Add(row);
        }
        return timeline;
    }

    private View VaccinationsTab()
    {
        // "Vaccination card" opens the pet's card (print / book) when the Vaccinations module is available.
        View cardButton = null;
        if (_profile.CanOpenVaccinationCard)
        {
            var button = new IconTextButton { Icon = "shield-check", Text = "Vaccination card", Outline = true, HorizontalOptions = LayoutOptions.End };
            button.Clicked += (s, e) => VaccinationCardRequested?.Invoke(this, _profile.Pet.PetId);
            cardButton = button;
        }
        View WithCard(View content) => cardButton == null ? content : new VerticalStackLayout { Spacing = 12, Children = { cardButton, content } };

        if (_profile.Vaccinations.Count == 0) return WithCard(Empty("syringe", "No vaccinations yet", "Vaccines"));

        var columns = new[] { new TableColumn("Vaccine", 200), new TableColumn("Dose no.", 100), new TableColumn("Date given", 140), new TableColumn("Next due", 140), new TableColumn("Given by", 180), new TableColumn("Status", 160) };
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var v in _profile.Vaccinations)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(v.Vaccine), 0, 0);
            row.Add(PageKit.Cell(v.DoseNo.ToString()), 1, 0);
            row.Add(PageKit.Cell(v.DateGiven.ToString("MMM d, yyyy")), 2, 0);
            row.Add(PageKit.Cell(v.NextDue?.ToString("MMM d, yyyy") ?? "—"), 3, 0);
            row.Add(PageKit.Cell(v.GivenBy), 4, 0);
            row.Add(Badges.VaccineStatus(Helpers.VaccineScheduleHelper.StatusOf(v.NextDue, DateTime.Today)), 5, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return WithCard(Table(stack));
    }

    private View AppointmentsTab()
    {
        if (_profile.Appointments.Count == 0) return Empty("calendar-days", "No appointments yet", "Visits");

        var columns = new[] { new TableColumn("Date", 140), new TableColumn("Time", 100), new TableColumn("Vet", 180), new TableColumn("Reason", 260), new TableColumn("Status", 140) };
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var a in _profile.Appointments)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(a.When.ToString("MMM d, yyyy")), 0, 0);
            row.Add(PageKit.Cell(a.When.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)), 1, 0);
            row.Add(PageKit.Cell(a.VetName), 2, 0);
            row.Add(PageKit.Cell(a.Reason), 3, 0);
            row.Add(AppointmentStatus(a.Status), 4, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Table(stack);
    }

    /// <summary>Scheduled = pale blue, Checked In = cyan, Completed = mint, Cancelled = gray, No-show = light orange.</summary>
    private static View AppointmentStatus(string status) => Badges.AppointmentStatus(status);

    private View PrescriptionsTab()
    {
        if (_profile.Prescriptions.Count == 0) return Empty("pill", "No prescriptions yet", "Medicines");

        var columns = new[] { new TableColumn("Date", 140), new TableColumn("Medicine", 220), new TableColumn("Dosage", 200), new TableColumn("Quantity", 110), new TableColumn("Dispensed", 130) };
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var p in _profile.Prescriptions)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(p.Date.ToString("MMM d, yyyy")), 0, 0);
            row.Add(PageKit.Cell(p.Medicine), 1, 0);
            row.Add(PageKit.Cell(p.Dosage), 2, 0);
            row.Add(PageKit.Cell(p.Quantity.ToString()), 3, 0);
            row.Add(p.Dispensed ? Badges.Pill("Yes", "Navy", "Mint") : Badges.Pill("No", "Warning", "WarningSoft"), 4, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Table(stack);
    }

    /// <summary>A line chart of the weights recorded at visits, with the latest weight above it.</summary>
    private View WeightTab()
    {
        var weights = _profile.Weights;
        var current = _profile.Pet.WeightText;

        if (weights.Count == 0)
        {
            string subtitle = (current != null ? "Current weight: " + current + ". " : string.Empty)
                + (_profile.MedicalRecordsAvailable
                    ? "The trend appears after the pet's first consultation."
                    : "The trend will appear here once the Medical records module is added.");
            return Card(PageKit.EmptyState("trending-up", "No weight history yet", subtitle));
        }

        var latest = weights[^1];
        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(24),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = "LATEST WEIGHT", FontFamily = "DMSansBold", FontSize = 12, CharacterSpacing = 0.67, TextColor = ThemeResources.Color("Muted") },
                    new Label
                    {
                        FormattedText = new FormattedString
                        {
                            Spans =
                            {
                                new Span { Text = latest.Weight.ToString("0.##") + " kg", FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy") },
                                new Span { Text = "   " + latest.Date.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
                            }
                        }
                    },
                    new GraphicsView { Drawable = new WeightChart(weights), HeightRequest = 240, Margin = new Thickness(0, 12, 0, 0) }
                }
            }
        };
    }

    private static View Table(View stack) =>
        Card(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }, padding: 0);

    private static View Card(View content, double padding = 8) => new Border
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = new Thickness(padding),
        Content = content
    };

    /// <summary>The two header cards sit side by side on wide windows and stack on narrow ones.</summary>
    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        bool wide = width >= 980;
        if (wide == _wide) return;
        _wide = wide;

        HeaderGrid.ColumnDefinitions = wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(300)))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
        HeaderGrid.RowDefinitions = wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        Grid.SetColumn(OwnerCard, wide ? 1 : 0);
        Grid.SetRow(OwnerCard, wide ? 0 : 1);

        // Narrow: the Edit / Book buttons move under the info chips.
        if (PetHeaderGrid.RowDefinitions.Count < 3) PetHeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(ButtonsRow, wide ? 2 : 1);
        Grid.SetRow(ButtonsRow, wide ? 0 : 2);
        Grid.SetColumnSpan(ButtonsRow, wide ? 1 : 2);
        ButtonsRow.Margin = wide ? new Thickness(0) : new Thickness(0, 12, 0, 0);
    }
}

/// <summary>Weight over the visit dates: navy line, cyan dots, light grid lines with kg labels.</summary>
internal sealed class WeightChart : IDrawable
{
    private readonly IReadOnlyList<PetWeight> _points;

    public WeightChart(IReadOnlyList<PetWeight> points) => _points = points;

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (_points.Count == 0) return;

        const float left = 48, right = 16, top = 12, bottom = 28;
        var plot = new RectF(rect.X + left, rect.Y + top, rect.Width - left - right, rect.Height - top - bottom);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        float min = (float)_points.Min(p => p.Weight), max = (float)_points.Max(p => p.Weight);
        float pad = Math.Max(0.5f, (max - min) * 0.15f);
        min = Math.Max(0, min - pad);
        max += pad;

        float X(int i) => _points.Count == 1 ? plot.Center.X : plot.Left + plot.Width * i / (_points.Count - 1);
        float Y(decimal w) => plot.Bottom - plot.Height * ((float)w - min) / (max - min);

        // Grid lines and kg labels
        canvas.FontSize = 12;
        canvas.FontColor = Color.FromArgb("#6B7A89");
        canvas.StrokeColor = Color.FromArgb("#CFE3EC");
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 4; i++)
        {
            float value = min + (max - min) * i / 4;
            float y = plot.Bottom - plot.Height * i / 4;
            canvas.DrawLine(plot.Left, y, plot.Right, y);
            canvas.DrawString(value.ToString("0.#"), rect.X, y - 8, left - 8, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // Date labels (first, last, and every other one when there's room)
        int step = Math.Max(1, (int)Math.Ceiling(_points.Count / Math.Max(1f, plot.Width / 90)));
        for (int i = 0; i < _points.Count; i += step)
            canvas.DrawString(_points[i].Date.ToString("MMM d"), X(i) - 40, plot.Bottom + 6, 80, 18, HorizontalAlignment.Center, VerticalAlignment.Top);

        // Line
        canvas.StrokeColor = Color.FromArgb("#0A2E4F");
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        var path = new PathF();
        for (int i = 0; i < _points.Count; i++)
        {
            if (i == 0) path.MoveTo(X(i), Y(_points[i].Weight));
            else path.LineTo(X(i), Y(_points[i].Weight));
        }
        canvas.DrawPath(path);

        // Dots
        for (int i = 0; i < _points.Count; i++)
        {
            canvas.FillColor = Color.FromArgb("#B8F0F2");
            canvas.FillCircle(X(i), Y(_points[i].Weight), 6);
            canvas.StrokeSize = 2;
            canvas.DrawCircle(X(i), Y(_points[i].Weight), 6);
        }
    }
}
