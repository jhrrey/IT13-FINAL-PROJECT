using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>A pet on the list, with what other modules know about it (empty until they exist).</summary>
public sealed record PetListItem(Pet Pet, DateTime? NextVaccineDue, DateTime? LastVisit);

/// <summary>
/// The pets list: card view (default) or table view. Displays what the controller gives it and
/// raises events; the rules (who may add, edit or archive) live in Controllers/PetController.
/// </summary>
public partial class PetsPage : ContentView
{
    private static readonly TableColumn[] Columns =
    {
        new("Pet", 220), new("Species", 110), new("Breed", 150), new("Sex", 100), new("Age", 130),
        new("Weight", 100), new("Owner", 190), new("Last visit", 130), new("Sync", 130), new("", 60)
    };

    private IDispatcherTimer _searchTimer;
    private PagedResult<PetListItem> _page;
    private bool _canEdit;
    private bool _tableView;
    private double _width;

    public PetsPage()
    {
        InitializeComponent();

        SpeciesPicker.Items.Add("All species");
        foreach (var species in PetSpecies.All) SpeciesPicker.Items.Add(species);
        SpeciesPicker.SelectedIndex = 0;
        SpeciesPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);

        SexPicker.Items.Add("All sex");
        foreach (var sex in PetSpecies.Sexes) SexPicker.Items.Add(sex);
        SexPicker.SelectedIndex = 0;
        SexPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);

        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        AddButton.Clicked += (s, e) => AddRequested?.Invoke(this, EventArgs.Empty);
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        WireToggle(CardViewButton, table: false);
        WireToggle(TableViewButton, table: true);
        UpdateToggle();

        SizeChanged += (s, e) => ApplyWidth(Width);

        ScrollFocus.DontScrollOnFocus(PageScroll, TableScroll);
    }

    public event EventHandler AddRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<int> PageRequested;

    /// <summary>A card or row was clicked: open that pet's profile.</summary>
    public event EventHandler<Pet> PetSelected;

    /// <summary>The three-dot button of a card or row was clicked (anchor view, pet).</summary>
    public event EventHandler<(View Anchor, Pet Pet)> MenuRequested;

    public string CurrentSearch => SearchEntry.Text?.Trim();

    /// <summary>Null = all species.</summary>
    public string CurrentSpecies => SpeciesPicker.SelectedIndex > 0 ? PetSpecies.All[SpeciesPicker.SelectedIndex - 1] : null;

    /// <summary>Null = both sexes.</summary>
    public string CurrentSex => SexPicker.SelectedIndex > 0 ? PetSpecies.Sexes[SexPicker.SelectedIndex - 1] : null;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentSpecies != null || CurrentSex != null;

    /// <summary>False for the Veterinarian and Administrator (view only): no Add button.</summary>
    public void SetCanEdit(bool canEdit)
    {
        _canEdit = canEdit;
        AddButton.IsVisible = canEdit;
    }

    public void ShowSummary(PetListSummary summary)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("paw-print", "Cyan", summary.Total.ToString(), "Total pets"),
            PageKit.SummaryCard("dog", "Mint", summary.Dogs.ToString(), "Dogs"),
            PageKit.SummaryCard("cat", "PaleBlue", summary.Cats.ToString(), "Cats"),
            PageKit.SummaryCard("syringe", "OrangeSoft", summary.VaccinesDueThisMonth.ToString(), "Vaccines due this month", "Orange")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading pets…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    public void ShowPets(PagedResult<PetListItem> page)
    {
        _page = page;

        if (page.TotalCount == 0)
        {
            ShowState(IsFiltering
                ? PageKit.StateMessage("No pets match your search.")
                : PageKit.EmptyState("paw-print", "No pets yet", "Add a pet to start their health record.",
                    _canEdit ? "Add pet" : null, () => AddRequested?.Invoke(this, EventArgs.Empty)));
            return;
        }

        StateCard.IsVisible = false;
        PagerRow.IsVisible = true;
        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "pet" : "pets")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        RenderItems();
    }

    // Shared with the pet profile ---------------------------------------------------------

    /// <summary>Round avatar with the species icon (dog = mint, cat = pale blue, bird = light orange, others = cyan).</summary>
    public static View SpeciesAvatar(string species, double size) => new IconBadge
    {
        Icon = PetSpecies.IconFor(species),
        Size = size,
        IconSize = Math.Round(size * 0.46),
        CircleColor = ThemeResources.Color(PetSpecies.CircleFor(species)),
        IconColor = ThemeResources.Color("Navy"),
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>Mint if more than 30 days away, amber "Due soon" within 30 days, light orange "Overdue" if past.</summary>
    public static View VaccinePill(DateTime due)
    {
        var today = DateTime.Today;
        if (due.Date < today) return Badges.Pill("Overdue: " + due.ToString("MMM dd, yyyy"), "Orange", "OrangeSoft");
        if (due.Date <= today.AddDays(30)) return Badges.Pill("Due soon: " + due.ToString("MMM dd, yyyy"), "Warning", "WarningSoft");
        return Badges.Pill("Next vaccine: " + due.ToString("MMM dd, yyyy"), "Navy", "Mint");
    }

    // Rendering ----------------------------------------------------------------------------

    private void RenderItems()
    {
        if (_page == null || _page.TotalCount == 0) return;

        CardsGrid.IsVisible = !_tableView;
        TableCard.IsVisible = _tableView;
        CardsGrid.Children.Clear();
        TableStack.Children.Clear();

        if (_tableView)
        {
            TableStack.Children.Add(PageKit.HeaderRow(Columns));
            foreach (var item in _page.Items) TableStack.Children.Add(PetRow(item));
            // A new table always starts at its first column (the Pet names).
            Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
        }
        else
        {
            PageKit.PlaceInGrid(CardsGrid, _page.Items.Select(PetCard).ToList(), CardColumns());
        }
    }

    private View PetCard(PetListItem item)
    {
        var pet = item.Pet;

        // Top row: avatar, (sync badge when not synced), actions menu.
        var top = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        top.Add(SpeciesAvatar(pet.Species, 52), 0, 0);
        if (pet.SyncStatus is SyncStatuses.Pending or SyncStatuses.Conflict)
        {
            var badge = Badges.Sync(pet.SyncStatus);
            badge.HorizontalOptions = LayoutOptions.End;
            badge.VerticalOptions = LayoutOptions.Start;
            top.Add(badge, 1, 0);
        }
        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + pet.PetName,
            () => MenuRequested?.Invoke(this, (menuButton, pet)));
        menuButton.VerticalOptions = LayoutOptions.Start;
        top.Add(menuButton, 2, 0);

        // Sex, age and weight with icons.
        var info = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        if (!string.IsNullOrEmpty(pet.Sex)) info.Children.Add(InfoItem(pet.Sex == PetSpecies.Female ? "venus" : "mars", pet.Sex));
        if (pet.AgeText != null) info.Children.Add(InfoItem("cake", pet.AgeText));
        if (pet.WeightText != null) info.Children.Add(InfoItem("weight", pet.WeightText));

        var owner = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = "user", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                new Label { Text = pet.OwnerName, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }
            }
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                top,
                new Label { Text = pet.PetName, FontFamily = "NunitoExtraBold", FontSize = 20, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(0, 14, 0, 0), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = pet.SpeciesAndBreed, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(0, 2, 0, 10), LineBreakMode = LineBreakMode.TailTruncation },
                info,
                new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), Margin = new Thickness(0, 6, 0, 12) },
                owner
            }
        };
        if (item.NextVaccineDue != null)
        {
            var pill = VaccinePill(item.NextVaccineDue.Value);
            pill.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(pill);
        }

        var card = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(20),
            Content = stack
        };

        // Clicking the card (but not the actions button) opens the profile.
        var open = new TapGestureRecognizer();
        open.Tapped += (s, e) =>
        {
            if (e.GetPosition(card) is Point p && p.X > card.Width - 64 && p.Y < 64) return; // actions button
            PetSelected?.Invoke(this, pet);
        };
        card.GestureRecognizers.Add(open);

        // Hover: stronger border and a slight lift.
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) =>
        {
            card.Stroke = ThemeResources.Color("CardHoverBorder");
            card.TranslationY = -2;
        };
        hover.PointerExited += (s, e) =>
        {
            if (e.GetPosition(card) is Point p && p.X > 0 && p.Y > 0 && p.X < card.Width && p.Y < card.Height) return;
            card.Stroke = ThemeResources.Color("BorderColor");
            card.TranslationY = 0;
        };
        card.GestureRecognizers.Add(hover);
        HandCursor.Apply(card);
        return card;
    }

    private static View InfoItem(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 5,
        Margin = new Thickness(0, 0, 14, 4),
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
        }
    };

    private View PetRow(PetListItem item)
    {
        var pet = item.Pet;
        var grid = PageKit.RowGrid(Columns, 64);

        grid.Add(new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                SpeciesAvatar(pet.Species, 40),
                new Label { Text = pet.PetName, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150 }
            }
        }, 0, 0);
        grid.Add(PageKit.Cell(pet.Species), 1, 0);
        grid.Add(PageKit.Cell(Dash(pet.Breed)), 2, 0);
        grid.Add(PageKit.Cell(pet.Sex), 3, 0);
        grid.Add(PageKit.Cell(pet.AgeText ?? "—"), 4, 0);
        grid.Add(PageKit.Cell(pet.WeightText ?? "—"), 5, 0);
        grid.Add(PageKit.Cell(pet.OwnerName), 6, 0);
        grid.Add(PageKit.Cell(item.LastVisit?.ToString("MMM d, yyyy") ?? "—", muted: true), 7, 0);
        grid.Add(Badges.Sync(pet.SyncStatus), 8, 0);

        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + pet.PetName,
            () => MenuRequested?.Invoke(this, (menuButton, pet)));
        grid.Add(menuButton, 9, 0);

        var open = new TapGestureRecognizer();
        open.Tapped += (s, e) =>
        {
            if (e.GetPosition(grid) is Point p && p.X > grid.Width - Columns[^1].Width - 20) return; // actions column
            PetSelected?.Invoke(this, pet);
        };
        grid.GestureRecognizers.Add(open);
        HandCursor.Apply(grid);

        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void ShowState(View content)
    {
        CardsGrid.Children.Clear();
        TableStack.Children.Clear();
        CardsGrid.IsVisible = false;
        TableCard.IsVisible = false;
        PagerRow.IsVisible = false;
        StateHost.Content = content;
        StateCard.IsVisible = true;
    }

    // View toggle ---------------------------------------------------------------------------

    private void WireToggle(Border button, bool table)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (_tableView == table) return;
            _tableView = table;
            UpdateToggle();
            RenderItems();
        };
        button.GestureRecognizers.Add(tap);
        HandCursor.Apply(button);
    }

    /// <summary>The active view's button has a cyan background.</summary>
    private void UpdateToggle()
    {
        CardViewButton.BackgroundColor = _tableView ? Colors.Transparent : ThemeResources.Color("Cyan");
        TableViewButton.BackgroundColor = _tableView ? ThemeResources.Color("Cyan") : Colors.Transparent;
    }

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        return timer;
    }

    // Responsive ----------------------------------------------------------------------------

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 4 : 2;

    /// <summary>3 pet cards per row on wide windows, then 2, then 1.</summary>
    private int CardColumns() => _width <= 0 || _width >= 1000 ? 3 : _width >= 680 ? 2 : 1;

    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        int oldSummary = SummaryColumns(), oldCards = CardColumns();
        bool oldWide = _width <= 0 || _width >= 900;
        _width = width;

        if (SummaryColumns() != oldSummary) PageKit.Reflow(SummaryGrid, SummaryColumns());
        if (CardColumns() != oldCards && CardsGrid.Children.Count > 0) PageKit.Reflow(CardsGrid, CardColumns());

        bool wide = width >= 900;
        if (wide == oldWide) return;

        // Wide: one row. Narrow: search on its own row, the filters and toggle below it.
        ToolbarGrid.ColumnDefinitions = wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(180)),
                new ColumnDefinition(new GridLength(160)), new ColumnDefinition(GridLength.Auto))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto));
        ToolbarGrid.RowDefinitions = wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));

        Grid.SetColumnSpan(SearchBox, wide ? 1 : 3);
        Grid.SetRow(SpeciesBox, wide ? 0 : 1);
        Grid.SetColumn(SpeciesBox, wide ? 1 : 0);
        Grid.SetRow(SexBox, wide ? 0 : 1);
        Grid.SetColumn(SexBox, wide ? 2 : 1);
        Grid.SetRow(ViewToggle, wide ? 0 : 1);
        Grid.SetColumn(ViewToggle, wide ? 3 : 2);
    }
}
