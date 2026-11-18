using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>Everything the owner profile shows, gathered by the controller.</summary>
public sealed class OwnerProfile
{
    public Owner Owner { get; init; }
    public OwnerStats Stats { get; init; }
    public IReadOnlyList<OwnerPet> Pets { get; init; } = Array.Empty<OwnerPet>();
    public IReadOnlyList<OwnerAppointment> Appointments { get; init; } = Array.Empty<OwnerAppointment>();
    public IReadOnlyList<OwnerBill> Bills { get; init; } = Array.Empty<OwnerBill>();

    /// <summary>False until the Billing module exists (changes the empty-state wording).</summary>
    public bool LinksAvailable { get; init; }

    /// <summary>True: the Appointments module exists.</summary>
    public bool AppointmentsAvailable { get; init; }

    /// <summary>True: the Pets module exists, so the Pets tab shows real pets.</summary>
    public bool PetsAvailable { get; init; }

    /// <summary>Cashier/Staff: true. Administrator (view only): false.</summary>
    public bool CanEdit { get; init; }
}

/// <summary>The owner profile page: header, quick stats, and Pets / Appointments / Billing history tabs.</summary>
public partial class OwnerDetailsPage : ContentView
{

    private readonly List<(Label Label, BoxView Line)> _tabs = new();
    private OwnerProfile _profile;
    private int _activeTab;
    private bool _wide = true;

    public OwnerDetailsPage()
    {
        InitializeComponent();

        BackText.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        BackLink.GestureRecognizers.Add(backTap);

        EditButton.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
        AddPetButton.Clicked += (s, e) => AddPetRequested?.Invoke(this, EventArgs.Empty);
        BookButton.Clicked += (s, e) => BookRequested?.Invoke(this, EventArgs.Empty);

        AddTab("Pets", 0);
        AddTab("Appointments", 1);
        AddTab("Billing history", 2);

        SizeChanged += (s, e) => ApplyWidth(Width);
    }

    public event EventHandler BackRequested;
    public event EventHandler EditRequested;
    public event EventHandler AddPetRequested;
    public event EventHandler BookRequested;

    /// <summary>A pet card was clicked (argument: the PetID).</summary>
    public event EventHandler<int> PetSelected;

    /// <summary>"View receipt" on a bill (argument: BillID); only shown when someone handles it.</summary>
    public event EventHandler<int> BillRequested;

    public void Show(OwnerProfile profile)
    {
        _profile = profile;
        var owner = profile.Owner;

        AvatarLabel.Text = owner.Initials;
        NameLabel.Text = owner.FullName;
        SyncHost.Content = OwnersPage.SyncBadge(owner);

        InfoRows.Children.Clear();
        InfoRows.Children.Add(InfoRow("phone", owner.ContactDisplay));
        if (!string.IsNullOrWhiteSpace(owner.Email)) InfoRows.Children.Add(InfoRow("mail", owner.Email));
        if (!string.IsNullOrWhiteSpace(owner.Address)) InfoRows.Children.Add(InfoRow("map-pin", owner.Address));
        InfoRows.Children.Add(InfoRow("calendar", "Registered " + owner.DateRegistered.ToString("MMM d, yyyy")));

        ButtonsRow.IsVisible = profile.CanEdit;

        var stats = profile.Stats;
        StatsRow.Children.Clear();
        StatsRow.Children.Add(StatChip("Pets", stats.PetCount.ToString(), highlight: false));
        StatsRow.Children.Add(StatChip("Next appointment",
            stats.NextAppointment?.ToString("MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture) ?? "None", highlight: false));
        StatsRow.Children.Add(StatChip("Unpaid balance", Money(stats.UnpaidBalance), highlight: stats.HasUnpaidBalance));

        SelectTab(_activeTab);
    }

    // Header and stats ----------------------------------------------------------------------

    private static View InfoRow(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 8,
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 520 }
        }
    };

    private static View StatChip(string label, string value, bool highlight) => new Border
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
                    new Span
                    {
                        Text = value,
                        FontSize = 14,
                        FontFamily = "DMSansBold",
                        TextColor = ThemeResources.Color(highlight ? "Orange" : "Navy")
                    }
                }
            }
        }
    };

    private static string Money(decimal amount) => Helpers.CurrencyHelper.Peso(amount);

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
            0 => PetsTab(),
            1 => AppointmentsTab(),
            _ => BillingTab()
        };
    }

    private View PetsTab()
    {
        var cards = _profile.Pets.Select(PetCard).ToList();
        if (_profile.CanEdit) cards.Add(AddPetCard());

        if (cards.Count == 0)
            return Card(PageKit.EmptyState("paw-print", "No pets yet",
                _profile.PetsAvailable ? null : "Pets will appear here once the Pets module is added."));

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        PageKit.PlaceInGrid(grid, cards, _wide ? 3 : 1);

        if (_profile.Pets.Count == 0)
        {
            var stack = new VerticalStackLayout { Spacing = 12 };
            stack.Children.Add(new Label
            {
                Text = _profile.PetsAvailable ? "No pets yet." : "No pets yet. Pets will appear here once the Pets module is added.",
                FontSize = 14,
                TextColor = ThemeResources.Color("BodyText")
            });
            stack.Children.Add(grid);
            return stack;
        }

        return grid;
    }

    private View PetCard(OwnerPet pet)
    {
        var details = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = pet.Name, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = string.Join(" · ", new[] { pet.Species, pet.Breed }.Where(s => !string.IsNullOrWhiteSpace(s))), FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                new Label { Text = pet.Age ?? string.Empty, FontSize = 13, TextColor = ThemeResources.Color("Muted"), IsVisible = !string.IsNullOrEmpty(pet.Age) }
            }
        };
        if (pet.NextVaccineDue != null)
            details.Children.Add(Badges.Pill("Next vaccine: " + pet.NextVaccineDue.Value.ToString("MMM dd, yyyy"), "Navy", "Mint"));

        var card = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(18),
            Content = new HorizontalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new IconBadge
                    {
                        Icon = PetSpecies.IconFor(pet.Species),
                        Size = 48,
                        IconSize = 22,
                        CircleColor = ThemeResources.Color(PetSpecies.CircleFor(pet.Species)),
                        IconColor = ThemeResources.Color("Navy"),
                        VerticalOptions = LayoutOptions.Start
                    },
                    details
                }
            }
        };

        // Clicking a pet opens its profile in the Pets module.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => PetSelected?.Invoke(this, pet.PetId);
        card.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => card.Stroke = ThemeResources.Color("CardHoverBorder");
        hover.PointerExited += (s, e) => card.Stroke = ThemeResources.Color("BorderColor");
        card.GestureRecognizers.Add(hover);
        HandCursor.Apply(card);
        return card;
    }

    private View AddPetCard()
    {
        var card = new Border
        {
            Padding = new Thickness(18),
            MinimumHeightRequest = 90,
            BackgroundColor = Colors.Transparent,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = "plus", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Add pet", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => AddPetRequested?.Invoke(this, EventArgs.Empty);
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        return card;
    }

    private View AppointmentsTab()
    {
        if (_profile.Appointments.Count == 0)
            return Card(PageKit.EmptyState("calendar-days", "No appointments yet",
                _profile.AppointmentsAvailable ? null : "Visits will appear here once the Appointments module is added."));

        var columns = new[] { new TableColumn("Date", 140), new TableColumn("Time", 100), new TableColumn("Pet", 150), new TableColumn("Vet", 180), new TableColumn("Reason", 240), new TableColumn("Status", 140) };
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var a in _profile.Appointments)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(PageKit.Cell(a.When.ToString("MMM d, yyyy")), 0, 0);
            row.Add(PageKit.Cell(a.When.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)), 1, 0);
            row.Add(PageKit.Cell(a.PetName), 2, 0);
            row.Add(PageKit.Cell(a.VetName), 3, 0);
            row.Add(PageKit.Cell(a.Reason), 4, 0);
            row.Add(AppointmentStatus(a.Status), 5, 0);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Card(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }, padding: 0);
    }

    private View BillingTab()
    {
        if (_profile.Bills.Count == 0)
            return Card(PageKit.EmptyState("circle-dollar-sign", "No bills yet",
                _profile.LinksAvailable ? null : "Bills will appear here once the Billing module is added."));

        var columns = new[] { new TableColumn("Bill no.", 140), new TableColumn("Bill date", 130), new TableColumn("Pet", 140), new TableColumn("Amount", 120), new TableColumn("Balance", 120), new TableColumn("Status", 110), new TableColumn("", 120) };
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(columns) } };
        foreach (var b in _profile.Bills)
        {
            var row = PageKit.RowGrid(columns, 56);
            row.Add(new Label { Text = b.BillNo, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(PageKit.Cell(b.BillDate.ToString("MMM d, yyyy")), 1, 0);
            row.Add(PageKit.Cell(b.PetName), 2, 0);
            row.Add(PageKit.Cell(Money(b.Amount)), 3, 0);
            row.Add(new Label { Text = b.Balance > 0 ? Money(b.Balance) : "—", FontFamily = b.Balance > 0 ? "DMSansBold" : "DMSans", FontSize = 14, TextColor = ThemeResources.Color(b.Balance > 0 ? "Orange" : "Muted"), VerticalOptions = LayoutOptions.Center }, 4, 0);
            row.Add(Badges.BillStatus(b.Status), 5, 0);
            if (BillRequested != null)
            {
                var receipt = new TextLink { Text = "View receipt", VerticalOptions = LayoutOptions.Center };
                receipt.StyleClass = new[] { "link-accent" };
                int billId = b.BillId;
                receipt.Clicked += (s, e) => BillRequested?.Invoke(this, billId);
                row.Add(receipt, 6, 0);
            }
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return Card(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }, padding: 0);
    }

    /// <summary>Scheduled = pale blue, Checked In = cyan, Completed = mint, Cancelled = gray, No-show = light orange.</summary>
    private static View AppointmentStatus(string status) => Badges.AppointmentStatus(status);

    private static View Card(View content, double padding = 8) => new Border
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = new Thickness(padding),
        Content = content
    };

    /// <summary>Pet cards go 3 per row on wide windows, 1 on narrow; header buttons move under the details.</summary>
    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        bool wide = width >= 980;
        if (wide == _wide) return;
        _wide = wide;

        Grid.SetColumn(ButtonsRow, wide ? 2 : 1);
        Grid.SetRow(ButtonsRow, wide ? 0 : 1);
        if (_profile != null) SelectTab(_activeTab);
    }
}
