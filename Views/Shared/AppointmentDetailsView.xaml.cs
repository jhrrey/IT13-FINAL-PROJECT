using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>A footer button of the details panel. <see cref="Style"/> is a button style key.</summary>
public sealed record AppointmentAction(string Key, string Text, string Style);

/// <summary>
/// The appointment details panel: pet and owner, visit info, a status timeline and the action
/// buttons the controller allows for this status and role. Raises events only.
/// </summary>
public partial class AppointmentDetailsView : ContentView
{
    public const double PanelWidth = 460;

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public AppointmentDetailsView()
    {
        InitializeComponent();
        ScrollFocus.DontScrollOnFocusWhileOpening(FormScroll);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);
        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);
    }

    public event EventHandler CloseRequested;

    /// <summary>A footer button was pressed (argument: its <see cref="AppointmentAction.Key"/>).</summary>
    public event EventHandler<string> ActionRequested;

    /// <summary>"View pet →" (argument: PetID).</summary>
    public event EventHandler<int> PetRequested;

    /// <summary>"View medical record →" (argument: RecordID).</summary>
    public event EventHandler<int> MedicalRecordRequested;

    /// <param name="canOpenPet">Show "View pet →" (the role may open the Pets module).</param>
    public void Show(Appointment a, IReadOnlyList<AppointmentAction> actions, bool canOpenPet)
    {
        StatusHost.Content = Badges.AppointmentStatus(a.Status);
        SyncHost.Content = a.SyncStatus == SyncStatuses.Conflict ? ConflictBadge() : Badges.Sync(a.SyncStatus);

        Body.Children.Clear();
        Body.Children.Add(PetCard(a, canOpenPet));
        Body.Children.Add(OwnerRow(a));
        Body.Children.Add(Divider());
        Body.Children.Add(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                InfoRow("calendar", a.AppointmentDate.ToString("dddd, MMM d, yyyy")),
                InfoRow("clock", a.TimeText),
                InfoRow("stethoscope", a.VetName),
                InfoRow("message-square", a.Reason),
                InfoRow("user", "Booked by " + (string.IsNullOrWhiteSpace(a.BookedByName) ? "—" : a.BookedByName))
            }
        });
        if (a.Status == AppointmentStatuses.Cancelled && !string.IsNullOrWhiteSpace(a.CancelReason))
            Body.Children.Add(Note("Reason for cancelling: " + a.CancelReason));

        Body.Children.Add(Divider());
        Body.Children.Add(SectionLabel("STATUS TIMELINE"));
        Body.Children.Add(Timeline(a));

        if (a.MedicalRecordId is int recordId)
        {
            var link = Link("View medical record");
            AddTap(link, () => MedicalRecordRequested?.Invoke(this, recordId));
            Body.Children.Add(link);
        }

        ShowActions(actions);
        ShowMessage(null);
    }

    public void ShowMessage(string message)
    {
        MessageLabel.Text = message ?? string.Empty;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void SetBusy(bool busy)
    {
        foreach (var button in ButtonsGrid.Children.OfType<Button>()) button.IsEnabled = !busy;
    }

    // Sections ------------------------------------------------------------------------------------

    private View PetCard(Appointment a, bool canOpenPet)
    {
        var details = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                AppointmentCards.WithGuestPill(a, new Label { Text = a.PetName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation }),
                new Label
                {
                    Text = string.Join(" · ", new[] { a.Species, a.Breed, a.PetSex }.Where(t => !string.IsNullOrWhiteSpace(t))),
                    FontSize = 14,
                    TextColor = ThemeResources.Color("BodyText")
                }
            }
        };
        if (canOpenPet && a.PetId is int petId)
        {
            var link = Link("View pet");
            link.Margin = new Thickness(0, 4, 0, 0);
            AddTap(link, () => PetRequested?.Invoke(this, petId));
            details.Children.Add(link);
        }

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 14
        };
        grid.Add(AppointmentCards.PetAvatar(a, 48), 0, 0);
        grid.Add(details, 1, 0);
        return grid;
    }

    private static View OwnerRow(Appointment a) => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            SectionLabel("OWNER"),
            new Label { Text = a.OwnerName, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
            InfoRow("phone", a.OwnerContactDisplay),
            new Label
            {
                IsVisible = a.IsGuest,
                Text = "New client (guest): the owner and pet are registered at check-in.",
                FontSize = 13,
                TextColor = ThemeResources.Color("Muted"),
                Margin = new Thickness(0, 4, 0, 0)
            }
        }
    };

    /// <summary>Booked → Checked in → Consultation started → Completed (or Cancelled / No-show). Future steps are hollow gray dots.</summary>
    private static View Timeline(Appointment a)
    {
        var steps = new List<(string Title, string When, bool Done)>
        {
            ("Booked", a.CreatedAt.ToString("MMM d, h:mm tt", Invariant) + (string.IsNullOrWhiteSpace(a.BookedByName) ? string.Empty : " by " + a.BookedByName), true)
        };

        if (a.Status == AppointmentStatuses.Cancelled)
            steps.Add(("Cancelled", a.CancelledAt?.ToString("MMM d, h:mm tt", Invariant) ?? string.Empty, true));
        else if (a.Status == AppointmentStatuses.NoShow)
            steps.Add(("Marked no-show", a.UpdatedAt.ToString("MMM d, h:mm tt", Invariant), true));
        else
        {
            steps.Add(("Checked in", a.CheckedInAt?.ToString("h:mm tt", Invariant) ?? string.Empty, a.CheckedInAt != null));
            steps.Add(("Consultation started", a.StartedAt?.ToString("h:mm tt", Invariant) ?? string.Empty, a.StartedAt != null));
            steps.Add(("Completed", a.CompletedAt?.ToString("h:mm tt", Invariant) ?? string.Empty, a.CompletedAt != null));
        }

        var stack = new VerticalStackLayout();
        for (int i = 0; i < steps.Count; i++)
        {
            var (title, when, done) = steps[i];
            bool last = i == steps.Count - 1;

            var rail = new Grid { WidthRequest = 16 };
            if (!last)
                rail.Children.Add(new BoxView { WidthRequest = 2, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 14, 0, 0) });
            rail.Children.Add(new Ellipse
            {
                WidthRequest = 10,
                HeightRequest = 10,
                Fill = done ? ThemeResources.Color("Orange") : Colors.White,
                Stroke = ThemeResources.Color(done ? "Orange" : "NeutralDot"),
                StrokeThickness = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var text = new VerticalStackLayout
            {
                Padding = new Thickness(0, 0, 0, last ? 0 : 14),
                Children =
                {
                    new Label { Text = title, FontFamily = done ? "DMSansBold" : "DMSans", FontSize = 14, TextColor = ThemeResources.Color(done ? "Navy" : "Placeholder") },
                    new Label { Text = when, FontSize = 13, TextColor = ThemeResources.Color("Muted"), IsVisible = !string.IsNullOrEmpty(when) }
                }
            };

            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
            row.Add(rail, 0, 0);
            row.Add(text, 1, 0);
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>Two buttons per row (one alone stretches); the main action comes last.</summary>
    private void ShowActions(IReadOnlyList<AppointmentAction> actions)
    {
        ButtonsGrid.Children.Clear();
        ButtonsGrid.ColumnDefinitions.Clear();
        ButtonsGrid.RowDefinitions.Clear();
        Footer.IsVisible = actions.Count > 0;
        if (actions.Count == 0) return;

        ButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        int rows = (actions.Count + 1) / 2;
        for (int r = 0; r < rows; r++) ButtonsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var button = new HoverButton { Text = action.Text, Style = ThemeResources.Get<Style>(action.Style) };
            button.Clicked += (s, e) => ActionRequested?.Invoke(this, action.Key);
            int row = i / 2, column = i % 2;
            ButtonsGrid.Add(button, column, row);
            if (i == actions.Count - 1 && column == 0) Grid.SetColumnSpan(button, 2);
        }
    }

    // Building blocks ------------------------------------------------------------------------------

    private static View ConflictBadge()
    {
        var badge = Badges.Sync(SyncStatuses.Conflict);
        ToolTipProperties.SetText(badge, "Double booking — please reschedule");
        return badge;
    }

    private static View InfoRow(string icon, string text) => new Grid
    {
        ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
        ColumnSpacing = 10,
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0) },
            WithColumn(new Label { Text = text ?? "—", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap }, 1)
        }
    };

    private static View WithColumn(View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontFamily = "DMSansBold",
        FontSize = 12,
        CharacterSpacing = 0.67,
        TextColor = ThemeResources.Color("Muted")
    };

    private static View Note(string text) => new Border
    {
        Padding = new Thickness(12, 10),
        BackgroundColor = ThemeResources.Color("LightGray"),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        Content = new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
    };

    private static BoxView Divider() => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") };

    private static HorizontalStackLayout Link(string text) => new()
    {
        Spacing = 6,
        HorizontalOptions = LayoutOptions.Start,
        Children =
        {
            new Label { Text = text, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center },
            new LucideIcon { Icon = "arrow-right", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center }
        }
    };

    private static void AddTap(View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => action();
        view.GestureRecognizers.Add(tap);
        HandCursor.Apply(view);
    }
}
