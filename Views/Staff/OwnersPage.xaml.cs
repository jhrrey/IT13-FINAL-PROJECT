using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>Pet count and up to two names for the Pets column.</summary>
public readonly record struct PetSummary(int Count, IReadOnlyList<string> Names);

/// <summary>
/// The owners list. Displays what the controller gives it and raises events; the rules
/// (who may add, edit or archive) live in Controllers/OwnerController.
/// </summary>
public partial class OwnersPage : ContentView
{
    private static readonly TableColumn[] Columns =
    {
        new("Owner", 290), new("Contact no.", 150), new("Address", 220), new("Pets", 200),
        new("Date registered", 150), new("Sync", 180), new("", 60)
    };

    private IDispatcherTimer _searchTimer;
    private PagedResult<Owner> _page;
    private bool _canEdit;
    private bool _wide = true;

    public OwnersPage()
    {
        InitializeComponent();

        SortPicker.Items.Add("Newest first");
        SortPicker.Items.Add("Name A–Z");
        SortPicker.SelectedIndex = 0;
        SortPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);

        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        AddButton.Clicked += (s, e) => AddRequested?.Invoke(this, EventArgs.Empty);
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        SizeChanged += (s, e) => ApplyWidth(Width);
    }

    public event EventHandler AddRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<int> PageRequested;

    /// <summary>A row was clicked: open that owner's profile.</summary>
    public event EventHandler<Owner> OwnerSelected;

    /// <summary>The three-dot button of a row was clicked (anchor view, owner).</summary>
    public event EventHandler<(View Anchor, Owner Owner)> MenuRequested;

    public string CurrentSearch => SearchEntry.Text?.Trim();

    public OwnerSort CurrentSort => SortPicker.SelectedIndex == 1 ? OwnerSort.NameAToZ : OwnerSort.Newest;

    /// <summary>False for the Administrator (view only): no Add button.</summary>
    public void SetCanEdit(bool canEdit)
    {
        _canEdit = canEdit;
        AddButton.IsVisible = canEdit;
    }

    public void ShowSummary(OwnerListSummary summary)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("users", "Cyan", summary.Total.ToString(), "Total owners"),
            PageKit.SummaryCard("user-plus", "Mint", summary.NewThisMonth.ToString(), "New this month"),
            PageKit.SummaryCard("calendar-days", "PaleBlue", summary.WithUpcomingAppointments.ToString(), "With upcoming appointments"),
            PageKit.SummaryCard("circle-dollar-sign", "OrangeSoft", summary.WithUnpaidBalance.ToString(), "With unpaid balance", "Orange")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, _wide ? 4 : 2);
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading owners…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    public void ShowOwners(PagedResult<Owner> page, IDictionary<int, PetSummary> pets, bool searching)
    {
        _page = page;
        TableStack.Children.Clear();

        if (page.TotalCount == 0)
        {
            ShowState(searching
                ? PageKit.StateMessage("No owners match your search.")
                : PageKit.EmptyState("paw-print", "No owners yet", "Add your first pet parent to get started.",
                    _canEdit ? "Add owner" : null, () => AddRequested?.Invoke(this, EventArgs.Empty), "user-plus"));
            return;
        }

        StateHost.IsVisible = false;
        TableStack.IsVisible = true;
        PagerRow.IsVisible = true;

        TableStack.Children.Add(PageKit.HeaderRow(Columns));
        foreach (var owner in page.Items)
        {
            pets.TryGetValue(owner.OwnerId, out var petSummary);
            TableStack.Children.Add(OwnerRow(owner, petSummary));
        }

        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "owner" : "owners")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
    }

    /// <summary>The Sync column: "Possible duplicate" (amber outline) or the stored sync status.</summary>
    public static View SyncBadge(Owner owner)
    {
        if (!owner.PossibleDuplicate) return Badges.Sync(owner.SyncStatus);

        return new Border
        {
            Padding = new Thickness(8, 3),
            Stroke = ThemeResources.Color("WarningBorder"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new LucideIcon { Icon = "circle-alert", StrokeColor = ThemeResources.Color("Warning"), WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Possible duplicate", FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color("Warning"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
    }

    private View OwnerRow(Owner owner, PetSummary pets)
    {
        var grid = PageKit.RowGrid(Columns, 66);

        grid.Add(PageKit.PersonCell(owner.Initials, owner.FullName, owner.Email), 0, 0);
        grid.Add(PageKit.Cell(owner.ContactDisplay), 1, 0);
        grid.Add(PageKit.Cell(string.IsNullOrWhiteSpace(owner.Address) ? "—" : owner.Address.Replace('\n', ' ')), 2, 0);
        grid.Add(PetsCell(pets), 3, 0);
        grid.Add(PageKit.Cell(owner.DateRegistered.ToString("MMM d, yyyy"), muted: true), 4, 0);
        grid.Add(SyncBadge(owner), 5, 0);

        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + owner.FullName,
            () => MenuRequested?.Invoke(this, (menuButton, owner)));
        grid.Add(menuButton, 6, 0);

        // Clicking the row (but not the actions button) opens the profile.
        var open = new TapGestureRecognizer();
        open.Tapped += (s, e) =>
        {
            var position = e.GetPosition(grid);
            if (position is Point p && p.X > grid.Width - Columns[^1].Width - 20) return; // actions column
            OwnerSelected?.Invoke(this, owner);
        };
        grid.GestureRecognizers.Add(open);
        HandCursor.Apply(grid);

        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    private static View PetsCell(PetSummary pets)
    {
        var row = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        row.Children.Add(Badges.Pill(pets.Count.ToString(), "Navy", "Cyan"));
        if (pets.Names is { Count: > 0 })
            row.Children.Add(new Label
            {
                Text = string.Join(", ", pets.Names.Take(2)),
                FontSize = 13,
                TextColor = ThemeResources.Color("Muted"),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaximumWidthRequest = 140,
                VerticalOptions = LayoutOptions.Center
            });
        return row;
    }

    private void ShowState(View content)
    {
        TableStack.Children.Clear();
        TableStack.IsVisible = false;
        PagerRow.IsVisible = false;
        StateHost.Content = content;
        StateHost.IsVisible = true;
    }

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        return timer;
    }

    /// <summary>Summary cards wrap to 2 per row and the toolbar stacks on narrower widths.</summary>
    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        bool wide = width >= 980;
        if (wide == _wide) return;
        _wide = wide;

        PageKit.Reflow(SummaryGrid, _wide ? 4 : 2);

        ToolbarGrid.ColumnDefinitions = _wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(200)))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
        ToolbarGrid.RowDefinitions = _wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        Grid.SetColumn(SortBox, _wide ? 1 : 0);
        Grid.SetRow(SortBox, _wide ? 0 : 1);
    }
}
