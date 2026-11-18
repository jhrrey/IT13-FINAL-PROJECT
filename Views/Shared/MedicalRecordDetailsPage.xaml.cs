using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Staff;

namespace VetCare.Views.Shared;

/// <summary>Everything the record details page shows, gathered by the controller.</summary>
public sealed class RecordDetails
{
    public MedicalRecord Record { get; init; }
    public IReadOnlyList<RecordPrescription> Prescriptions { get; init; } = Array.Empty<RecordPrescription>();
    public IReadOnlyList<RecordVaccination> Vaccinations { get; init; } = Array.Empty<RecordVaccination>();
    public IReadOnlyList<RecordNote> Notes { get; init; } = Array.Empty<RecordNote>();

    /// <summary>The signed-in vet wrote it and it's still the same day (rule 11).</summary>
    public bool CanEdit { get; init; }

    /// <summary>The record's vet, after the day it was written: corrections go in as notes.</summary>
    public bool CanAddNote { get; init; }

    public bool CanPrint { get; init; }
    public bool CanBookFollowUp { get; init; }

    /// <summary>No longer editable (shown with a lock).</summary>
    public bool Locked { get; init; }
}

/// <summary>A clean, read-only view of one medical record. Raises events only.</summary>
public partial class MedicalRecordDetailsPage : ContentView
{
    public MedicalRecordDetailsPage()
    {
        InitializeComponent();
        BackText.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        BackLink.GestureRecognizers.Add(backTap);
        HandCursor.Apply(BackLink);
        ScrollFocus.DontScrollOnFocus(PageScroll);
    }

    public event EventHandler BackRequested;
    public event EventHandler PrintRequested;
    public event EventHandler FollowUpRequested;
    public event EventHandler EditRequested;
    public event EventHandler AddNoteRequested;

    public void Show(RecordDetails d)
    {
        var r = d.Record;

        ButtonsRow.Children.Clear();
        if (d.CanPrint) ButtonsRow.Children.Add(Button("Print", "OutlineButton", () => PrintRequested?.Invoke(this, EventArgs.Empty)));
        if (d.CanBookFollowUp) ButtonsRow.Children.Add(Button("Book follow-up", "OutlineButton", () => FollowUpRequested?.Invoke(this, EventArgs.Empty)));
        if (d.CanAddNote) ButtonsRow.Children.Add(Button("Add note", "NavyOutlineButton", () => AddNoteRequested?.Invoke(this, EventArgs.Empty)));
        if (d.CanEdit) ButtonsRow.Children.Add(Button("Edit", "WorkspaceButton", () => EditRequested?.Invoke(this, EventArgs.Empty)));

        AvatarHost.Content = PetsPage.SpeciesAvatar(r.Species, 56);
        PetNameLabel.Text = r.PetName;
        PetInfoLabel.Text = string.Join(" · ", new[] { r.Species, r.Breed, r.PetSex, r.AgeText }.Where(t => !string.IsNullOrWhiteSpace(t)));
        InfoRow.Children.Clear();
        InfoRow.Children.Add(Info("user", r.OwnerName + " · " + r.OwnerContactDisplay));
        InfoRow.Children.Add(Info("calendar", r.VisitText));
        InfoRow.Children.Add(Info("stethoscope", r.VetName));
        SyncHost.Content = Badges.Sync(r.SyncStatus);
        if (r.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(SyncHost.Content, "Two versions found — review needed");
        LockedRow.IsVisible = d.Locked;
        if (r.IsDraft) { LockedRow.IsVisible = true; LockedLabel.Text = "Draft — visit still in consultation"; }

        // Vital signs: temperature amber when outside the species' normal range.
        VitalsRow.Children.Clear();
        VitalsRow.Children.Add(Vital("weight", "Weight", r.WeightText ?? "—", warn: false));
        VitalsRow.Children.Add(Vital("thermometer", "Temperature", r.TemperatureText ?? "—",
            warn: VitalSignsHelper.IsOutsideNormal(r.Species, r.Temperature)));

        SectionsStack.Children.Clear();
        SectionsStack.Children.Add(TextSections(r));
        SectionsStack.Children.Add(PrescriptionsCard(d.Prescriptions));
        SectionsStack.Children.Add(VaccinationsCard(d.Vaccinations));
        if (d.Notes.Count > 0) SectionsStack.Children.Add(NotesCard(d.Notes));
    }

    private static View TextSections(MedicalRecord r)
    {
        var stack = new VerticalStackLayout { Spacing = 18 };
        void Add(string title, string body)
        {
            stack.Children.Add(new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = title, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                    new Label { Text = string.IsNullOrWhiteSpace(body) ? "—" : body, FontSize = 15, TextColor = ThemeResources.Color("BodyText"), LineHeight = 1.3 }
                }
            });
        }
        Add("Diagnosis", r.Diagnosis);
        Add("Treatment", r.Treatment);
        Add("Notes", r.Notes);
        return Card(stack, 24);
    }

    private static View PrescriptionsCard(IReadOnlyList<RecordPrescription> items)
    {
        var columns = new[] { new TableColumn("Medicine", 240), new TableColumn("Qty", 90), new TableColumn("Dosage", 150), new TableColumn("Instructions", 260), new TableColumn("Dispensed", 130) };
        if (items.Count == 0) return Titled("Prescriptions", Empty("No medicines prescribed."));
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var p in items)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(p.MedicineName), 0, 0);
            row.Add(PageKit.Cell(p.Quantity + " " + p.Unit), 1, 0);
            row.Add(PageKit.Cell(p.Dosage ?? "—"), 2, 0);
            row.Add(PageKit.Cell(p.Instructions ?? "—", muted: true), 3, 0);
            row.Add(p.IsDispensed ? Badges.Pill("Yes", "CompletedText", "Mint") : Badges.Pill("Not yet", "Warning", "WarningSoft"), 4, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Titled("Prescriptions", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }, padding: 0);
    }

    private static View VaccinationsCard(IReadOnlyList<RecordVaccination> items)
    {
        var columns = new[] { new TableColumn("Vaccine", 240), new TableColumn("Dose no.", 100), new TableColumn("Date given", 150), new TableColumn("Next due", 150) };
        if (items.Count == 0) return Titled("Vaccinations", Empty("No vaccines given."));
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var v in items)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(v.VaccineName), 0, 0);
            row.Add(PageKit.Cell(v.DoseNo.ToString()), 1, 0);
            row.Add(PageKit.Cell(v.DateGiven.ToString("MMM d, yyyy")), 2, 0);
            row.Add(PageKit.Cell(v.NextDueDate?.ToString("MMM d, yyyy") ?? "—"), 3, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Titled("Vaccinations", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }, padding: 0);
    }

    private static View NotesCard(IReadOnlyList<RecordNote> notes)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        foreach (var n in notes)
            stack.Children.Add(new Border
            {
                Padding = new Thickness(14, 10),
                BackgroundColor = ThemeResources.Color("InputBackground"),
                Stroke = ThemeResources.Color("BorderColor"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = n.CreatedAt.ToString("MMM d, yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " · " + n.VetName, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") },
                        new Label { Text = n.NoteText, FontSize = 14.5, TextColor = ThemeResources.Color("Navy") }
                    }
                }
            });
        return Titled("Added notes", stack);
    }

    // Building blocks --------------------------------------------------------------------------------

    private static View Titled(string title, View content, double padding = 20)
    {
        var heading = new Label { Text = title, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(padding == 0 ? 20 : 0, padding == 0 ? 16 : 0, 0, 10) };
        return Card(new VerticalStackLayout { Children = { heading, content } }, padding);
    }

    private static View Empty(string text) => new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color("Muted") };

    private static Border Card(View content, double padding) => new()
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = new Thickness(padding),
        Content = content
    };

    private static View Info(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 6,
        Margin = new Thickness(0, 0, 18, 6),
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
        }
    };

    /// <summary>"Weight 12.5 kg" chip; amber when outside the normal range.</summary>
    private static View Vital(string icon, string label, string value, bool warn) => new Border
    {
        Padding = new Thickness(14, 8),
        Margin = new Thickness(0, 0, 10, 0),
        BackgroundColor = ThemeResources.Color(warn ? "WarningBox" : "Surface"),
        Stroke = ThemeResources.Color(warn ? "WarningBorder" : "BorderColor"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 18 },
        Content = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(warn ? "Warning" : "NavyMuted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                new Label
                {
                    VerticalOptions = LayoutOptions.Center,
                    FormattedText = new FormattedString
                    {
                        Spans =
                        {
                            new Span { Text = label + " ", FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                            new Span { Text = value, FontSize = 14, FontFamily = "DMSansBold", TextColor = ThemeResources.Color(warn ? "Warning" : "Navy") }
                        }
                    }
                }
            }
        }
    };

    private static View Button(string text, string style, Action onClick)
    {
        var button = new HoverButton { Text = text, Style = ThemeResources.Get<Style>(style), Padding = new Thickness(18, 0) };
        button.Clicked += (s, e) => onClick();
        return button;
    }
}
