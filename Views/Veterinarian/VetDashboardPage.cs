using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Views.Veterinarian;

/// <summary>The Veterinarian dashboard (section 4): who's next, today's schedule and unfinished records. No money (rule 5).</summary>
public sealed class VetDashboardPage : DashboardPageBase
{
    public void Show(VetDashboardData d, string displayName, string clinic, SyncState sync)
    {
        var a = Actions;
        var today = DateTime.Today;

        var cards = new View[]
        {
            DashboardKit.StatCard("calendar-days", "Cyan", "Navy", d.MyToday.ToString(), "My appointments today", onTap: () => a.OpenModule(MenuCatalog.Appointments, null, null)),
            DashboardKit.StatCard("clock", "PaleBlue", "Navy", d.Waiting.ToString(), "Waiting for me", onTap: () => a.OpenModule(MenuCatalog.Appointments, null, null)),
            DashboardKit.StatCard("circle-check", "Mint", "CompletedText", d.CompletedToday.ToString(), "Completed today", onTap: () => a.OpenModule(MenuCatalog.MedicalRecords, null, null)),
            DashboardKit.StatCard("file-pen", "WarningBox", "ConsultText", d.Unfinished.ToString(), "Unfinished consultations",
                onTap: d.UnfinishedVisits.Count > 0 ? () => a.StartConsultation(d.UnfinishedVisits[0].Appointment) : null)
        };

        // Today's schedule (timeline) ------------------------------------------------------------------------
        var current = d.Schedule.FirstOrDefault(x => x.Status is AppointmentStatuses.InConsultation or AppointmentStatuses.CheckedIn)
                      ?? d.Schedule.FirstOrDefault(x => x.Status == AppointmentStatuses.Scheduled);
        var schedule = DashboardKit.ListCard("calendar-days", "Today's schedule",
            d.Schedule.Select((x, i) => TimelineRow(x, x == current, i == d.Schedule.Count - 1, () => a.OpenAppointment(x))).ToList(),
            "No appointments today.", () => a.OpenModule(MenuCatalog.Appointments, null, null), "calendar-days");

        var unfinished = DashboardKit.ListCard("file-pen", "Unfinished consultations",
            d.UnfinishedVisits.Select(u => (View)DashboardKit.Row(
                DashboardKit.TwoLines(u.Appointment.PetName,
                    "Started " + (u.Appointment.StartedAt ?? u.Appointment.StartsAt).ToString("MMM d") +
                    (u.DraftSavedAt is DateTime saved ? " · Draft saved " + saved.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : " · No draft yet"),
                    leading: AppointmentCards.PetAvatar(u.Appointment, 34)),
                DashboardKit.SmallButton("Continue", "arrow-right", true, () => a.StartConsultation(u.Appointment)))).ToList(),
            "Every record is finished. Nice!", () => a.OpenModule(MenuCatalog.MedicalRecords, null, null));

        var records = DashboardKit.ListCard("clipboard-plus", "My recent records",
            d.RecentRecords.Select(r => (View)DashboardKit.Row(
                DashboardKit.TwoLines(r.PetName + " · " + r.Date.ToString("MMM d"), r.Diagnosis, leading: PetsPage.SpeciesAvatar(r.Species, 34)),
                Link("View", () => a.OpenRecord(r.RecordId)),
                () => a.OpenRecord(r.RecordId))).ToList(),
            "No records yet.", () => a.OpenModule(MenuCatalog.MedicalRecords, null, null), "clipboard-plus");

        var vaccines = DashboardKit.ListCard("syringe", "Vaccine stock check",
            d.Vaccines.Select(m => DashboardKit.StockRow(m, today, () => a.OpenModule(MenuCatalog.Medicines, null, null))).ToList(),
            "No vaccines in the stock list.", () => a.OpenModule(MenuCatalog.Medicines, null, null), "syringe");

        Render(
            DashboardKit.Header(displayName, clinic, sync, a.SyncNow),
            DashboardKit.QuickActions(new[]
            {
                new QuickAction("My schedule", "calendar", () => a.OpenModule(MenuCatalog.Appointments, null, null)),
                new QuickAction("Medical records", "clipboard-plus", () => a.OpenModule(MenuCatalog.MedicalRecords, null, null)),
                new QuickAction("Vaccinations", "syringe", () => a.OpenModule(MenuCatalog.Vaccinations, null, null))
            }),
            DashboardKit.StatGrid(cards, 4),
            NextPatientCard(d),
            DashboardKit.Columns(820, new[] { 11.0, 9.0 }, schedule, unfinished),
            DashboardKit.Columns(820, new[] { 1.0, 1.0 }, records, vaccines));
    }

    /// <summary>Light cyan, large: the first Checked In pet (with a big Start consultation), or the next scheduled one ("Not here yet").</summary>
    private View NextPatientCard(VetDashboardData d)
    {
        var a = d.NextPatient;
        if (a == null)
        {
            return Panel(new HorizontalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new IconBadge { Icon = "circle-check", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText") },
                    new VerticalStackLayout
                    {
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "No one is waiting", FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy") },
                            new Label { Text = "There are no more appointments for you today.", FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
                        }
                    }
                }
            });
        }

        string label = d.NextIsWaiting
            ? "NEXT PATIENT · WAITING " + GreetingHelper.Duration(DateTime.Now - (a.CheckedInAt ?? a.StartsAt)).ToUpperInvariant()
            : "NEXT APPOINTMENT · NOT HERE YET";
        string details = string.Join(" · ", new[] { a.Species, a.Breed, a.PetSex }.Where(t => !string.IsNullOrWhiteSpace(t)));

        var info = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                AppointmentCards.WithGuestPill(a, new Label { Text = a.PetName, FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy") }),
                new Label { Text = details, FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new LucideIcon { Icon = "user", StrokeColor = ThemeResources.Color("BodyText"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = a.OwnerName, FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
                    }
                },
                new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { Chip(a.TimeText), Chip(a.Reason) } }
            }
        };
        var left = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        left.Add(new IconBadge { Icon = a.IsGuest ? "paw-print" : PetSpecies.IconFor(a.Species), Size = 72, IconSize = 34, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        left.Add(info, 1, 0);

        var last = new Border
        {
            Padding = new Thickness(14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color("Surface"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = d.LastVisit == null ? "First visit — no earlier records." : "Last visit: " + d.LastVisit,
                FontSize = 13.5,
                TextColor = ThemeResources.Color("BodyText"),
                LineBreakMode = LineBreakMode.WordWrap
            }
        };

        var body = new Grid { ColumnSpacing = 20, RowSpacing = 14 };
        View action = null;
        if (d.NextIsWaiting)
        {
            var start = new HoverButton { Text = "Start consultation →", Style = ThemeResources.Get<Style>("WorkspaceButton"), HeightRequest = 52, Padding = new Thickness(24, 0), VerticalOptions = LayoutOptions.Center };
            start.Clicked += (s, e) => Actions.StartConsultation(a);
            action = start;
        }
        // Side by side when there's room, stacked otherwise.
        bool? wide = null;
        void Arrange()
        {
            bool w = body.Width <= 0 || body.Width >= 760;
            if (wide == w) return;
            wide = w;
            body.Children.Clear();
            body.ColumnDefinitions.Clear();
            body.RowDefinitions.Clear();
            if (w)
            {
                body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(250)));
                body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                body.Add(left, 0, 0);
                body.Add(last, 1, 0);
                if (action != null) body.Add(action, 2, 0);
            }
            else
            {
                body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                body.Add(left, 0, 0);
                body.Add(last, 0, 1);
                if (action != null) body.Add(action, 0, 2);
            }
        }
        body.SizeChanged += (s, e) => Arrange();
        Arrange();

        return Panel(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Label { Text = label, FontFamily = "DMSansBold", FontSize = 13, CharacterSpacing = 0.8, TextColor = ThemeResources.Color("Orange") },
                body
            }
        });
    }

    private static Border Panel(View content, Action onTap = null)
    {
        var border = new Border
        {
            Padding = new Thickness(28),
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            BackgroundColor = ThemeResources.Color("CyanSoft"),
            Content = content
        };
        if (onTap != null) DashboardKit.Clickable(border, onTap, "Open the appointment");
        return border;
    }

    /// <summary>Time | dot on the line (status colour) | pet + reason | status pill. The current or next item is highlighted.</summary>
    private static View TimelineRow(Appointment a, bool highlight, bool last, Action open)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(10, 8),
            BackgroundColor = highlight ? ThemeResources.Color("CyanSoft") : Colors.Transparent,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(70)), new ColumnDefinition(new GridLength(14)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(new Label { Text = a.TimeText, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        var line = new Grid
        {
            Children =
            {
                new BoxView { WidthRequest = 2, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, -10, 0, last ? 20 : -10) },
                new Ellipse { WidthRequest = 10, HeightRequest = 10, Fill = new SolidColorBrush(ThemeResources.Color(AppointmentStatuses.AccentFor(a.Status))), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            }
        };
        grid.Add(line, 1, 0);
        grid.Add(DashboardKit.TwoLines(a.PetName + (a.IsGuest ? " (Guest)" : string.Empty), a.Reason, leading: AppointmentCards.PetAvatar(a, 30)), 2, 0);
        grid.Add(Badges.AppointmentStatus(a.Status), 3, 0);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => open();
        grid.GestureRecognizers.Add(tap);
        HandCursor.Apply(grid);
        return new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = grid };
    }

    private static View Chip(string text) => new Border
    {
        Padding = new Thickness(10, 4),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        BackgroundColor = ThemeResources.Color("Surface"),
        Content = new Label { Text = text, FontFamily = "DMSansBold", FontSize = 12.5, TextColor = ThemeResources.Color("Navy") }
    };

    private static View Link(string text, Action onTap)
    {
        var link = new TextLink { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        link.StyleClass = new[] { "link-accent" };
        link.Clicked += (s, e) => onTap();
        return link;
    }
}
