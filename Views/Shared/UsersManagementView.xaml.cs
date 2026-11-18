using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>Row actions in the users table.</summary>
public enum UserAction
{
    View,
    Edit,
    ResetPassword,
    Deactivate,
    Activate
}

public sealed class UserActionEventArgs : EventArgs
{
    public UserActionEventArgs(User user, UserAction action)
    {
        User = user;
        Action = action;
    }

    public User User { get; }
    public UserAction Action { get; }
}

/// <summary>The toolbar's current filters.</summary>
public sealed class UserFilter
{
    public string Search { get; init; }
    public int? RoleId { get; init; }
    public string Status { get; init; }
    public int? ClinicId { get; init; }

    public bool IsActive => !string.IsNullOrWhiteSpace(Search) || RoleId != null || Status != null || ClinicId != null;
}

/// <summary>How the controller configures the page for the signed-in role.</summary>
public sealed class UsersPageSetup
{
    /// <summary>The module's sidebar icon, shown beside the title.</summary>
    public string Icon { get; init; }

    public string Title { get; init; }
    public string Subtitle { get; init; }

    /// <summary>Clinic column and "All clinics" filter (Super Admin only).</summary>
    public bool ShowClinic { get; init; }

    public bool CanAdd { get; init; } = true;

    /// <summary>Options for the "All roles" filter.</summary>
    public IReadOnlyList<(int Id, string Name)> RoleOptions { get; init; } = Array.Empty<(int, string)>();

    /// <summary>Options for the "All clinics" filter (only used when <see cref="ShowClinic"/>).</summary>
    public IReadOnlyList<(int Id, string Name)> ClinicOptions { get; init; } = Array.Empty<(int, string)>();

    public int? InitialClinicId { get; init; }

    /// <summary>The fourth summary card: which role to count, and how to label it.</summary>
    public int FourthCardRoleId { get; init; } = RoleIds.Veterinarian;
    public string FourthCardLabel { get; init; } = "Veterinarians";
    public string FourthCardIcon { get; init; } = "stethoscope";
}

/// <summary>
/// The "Users & roles" page body. Displays what the controller gives it and raises events for every
/// user action. All rules (who may see or change what) live in Controllers/UserController.
/// </summary>
public partial class UsersManagementView : ContentView
{
    private static readonly TableColumn[] BaseColumns =
    {
        new("User", 290), new("Username", 150), new("Role", 150), new("Clinic", 190), new("Contact no.", 150),
        new("Status", 120), new("Sync", 120), new("Last updated", 160), new("", 60)
    };

    private readonly Picker _roleFilter = NewFilterPicker();
    private readonly Picker _statusFilter = NewFilterPicker();
    private readonly Picker _clinicFilter = NewFilterPicker();
    private readonly List<int?> _roleValues = new();
    private readonly List<int?> _clinicValues = new();
    private readonly List<(Label Label, BoxView Line)> _tabs = new();

    private UsersPageSetup _setup = new();
    private IDispatcherTimer _searchTimer;
    private PagedResult<User> _page;
    private bool _wide = true;
    private bool _suppressFilterEvents;

    public UsersManagementView()
    {
        InitializeComponent();

        AddButton.Clicked += (s, e) => AddRequested?.Invoke(this, EventArgs.Empty);
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        // Search waits until typing pauses.
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        _statusFilter.Items.Add("All status");
        _statusFilter.Items.Add("Active");
        _statusFilter.Items.Add("Inactive");
        _statusFilter.SelectedIndex = 0;

        foreach (var picker in new[] { _roleFilter, _statusFilter, _clinicFilter })
            picker.SelectedIndexChanged += (s, e) => { if (!_suppressFilterEvents) RaiseFiltersChanged(); };

        BuildTabs();
        BuildPermissionTable();
        SizeChanged += (s, e) => ApplyWidth(Width);
    }

    public event EventHandler AddRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<UserActionEventArgs> ActionRequested;

    /// <summary>Side panel for the Add / Edit form and user details.</summary>
    public SidePanelHost Panel => SidePanel;

    /// <summary>Deactivate confirmation and Reset password dialog.</summary>
    public ConfirmDialogView Dialog => ConfirmDialog;

    public UserFilter CurrentFilter => new()
    {
        Search = SearchEntry.Text?.Trim(),
        RoleId = _roleFilter.SelectedIndex > 0 ? _roleValues[_roleFilter.SelectedIndex] : null,
        Status = _statusFilter.SelectedIndex switch { 1 => AccountStatus.Active, 2 => AccountStatus.Inactive, _ => null },
        ClinicId = _setup.ShowClinic && _clinicFilter.SelectedIndex > 0 ? _clinicValues[_clinicFilter.SelectedIndex] : null
    };

    public void Configure(UsersPageSetup setup)
    {
        _setup = setup;
        Header.Icon = setup.Icon;
        Header.Title = setup.Title;
        Header.Subtitle = setup.Subtitle;
        AddButton.IsVisible = setup.CanAdd;

        _suppressFilterEvents = true;
        _roleFilter.Items.Clear();
        _roleValues.Clear();
        _roleFilter.Items.Add("All roles");
        _roleValues.Add(null);
        foreach (var (id, name) in setup.RoleOptions)
        {
            _roleFilter.Items.Add(name);
            _roleValues.Add(id);
        }
        _roleFilter.SelectedIndex = 0;

        _clinicFilter.Items.Clear();
        _clinicValues.Clear();
        _clinicFilter.Items.Add("All clinics");
        _clinicValues.Add(null);
        foreach (var (id, name) in setup.ClinicOptions)
        {
            _clinicFilter.Items.Add(name);
            _clinicValues.Add(id);
        }
        int initial = setup.InitialClinicId == null ? 0 : _clinicValues.IndexOf(setup.InitialClinicId);
        _clinicFilter.SelectedIndex = Math.Max(0, initial);
        _suppressFilterEvents = false;

        LayoutToolbar();
    }

    public void ShowSummary(UserSummary summary)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("users", "Cyan", summary.Total.ToString(), "Total users"),
            PageKit.SummaryCard("user-check", "Mint", summary.Active.ToString(), "Active"),
            PageKit.SummaryCard("user-x", "PaleBlue", summary.Inactive.ToString(), "Inactive"),
            PageKit.SummaryCard(_setup.FourthCardIcon, "Cyan", summary.CountOf(_setup.FourthCardRoleId).ToString(), _setup.FourthCardLabel)
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, _wide ? 4 : 2);
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading users…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    public void ShowUsers(PagedResult<User> page, Func<User, IReadOnlyList<UserAction>> actionsFor, bool filtersActive)
    {
        _page = page;
        TableStack.Children.Clear();

        if (page.TotalCount == 0)
        {
            ShowState(filtersActive
                ? NoMatches()
                : PageKit.EmptyState("users", "No users yet", null,
                    _setup.CanAdd ? "Add user" : null, () => AddRequested?.Invoke(this, EventArgs.Empty), "user-plus"));
            return;
        }

        StateHost.IsVisible = false;
        TableStack.IsVisible = true;
        PagerRow.IsVisible = true;

        var columns = Columns();
        TableStack.Children.Add(PageKit.HeaderRow(columns));
        foreach (var user in page.Items)
            TableStack.Children.Add(UserRow(user, columns, actionsFor(user)));

        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "user" : "users")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
    }

    /// <summary>Role cards with live user counts (RoleID → count).</summary>
    public void ShowRoleCounts(IDictionary<int, int> counts)
    {
        var cards = PermissionMatrix.RoleCards.Select(role =>
        {
            counts.TryGetValue(role.RoleId, out int count);
            return (View)new Border
            {
                Style = ThemeResources.Get<Style>("WorkspaceCard"),
                Padding = new Thickness(20),
                Content = new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new IconBadge
                        {
                            Icon = role.Icon,
                            Size = 44,
                            IconSize = 22,
                            CircleColor = ThemeResources.Color(role.CircleColorKey),
                            IconColor = ThemeResources.Color("Navy")
                        },
                        new Label { Text = role.Name, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy") },
                        new Label { Text = role.Description, FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                        new Label
                        {
                            Text = count == 1 ? "1 user" : count + " users",
                            FontFamily = "DMSansMedium",
                            FontSize = 13,
                            TextColor = ThemeResources.Color("Muted")
                        }
                    }
                }
            };
        }).ToList();

        PageKit.PlaceInGrid(RoleCardsGrid, cards, _wide ? 4 : 2);
    }

    // Tabs -----------------------------------------------------------------------------------

    private void BuildTabs()
    {
        AddTab("Users", () => SelectTab(0));
        AddTab("Roles & permissions", () => SelectTab(1));
        SelectTab(0);
    }

    private void AddTab(string text, Action onTap)
    {
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var line = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End };
        var tab = new Grid { HeightRequest = 40, Children = { label, line } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);

        _tabs.Add((label, line));
        TabsRow.Children.Add(tab);
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == index;
            _tabs[i].Label.FontFamily = active ? "DMSansBold" : "DMSansMedium";
            _tabs[i].Label.TextColor = ThemeResources.Color(active ? "Orange" : "BodyText");
            _tabs[i].Line.Color = active ? ThemeResources.Color("Orange") : Colors.Transparent;
        }

        UsersTab.IsVisible = index == 0;
        RolesTab.IsVisible = index == 1;
        AddButton.IsVisible = index == 0 && _setup.CanAdd;
    }

    // Toolbar ----------------------------------------------------------------------------------

    private static Picker NewFilterPicker() => new()
    {
        FontFamily = "DMSans",
        FontSize = 15,
        TextColor = ThemeResources.Color("Navy"),
        BackgroundColor = Colors.Transparent,
        VerticalOptions = LayoutOptions.Center
    };

    private static View FilterBox(Picker picker) => new Border
    {
        Style = ThemeResources.Get<Style>("InputBox"),
        HeightRequest = 44,
        Padding = new Thickness(6, 0),
        Content = picker
    };

    private void LayoutToolbar()
    {
        ToolbarGrid.Children.Clear();
        ToolbarGrid.ColumnDefinitions.Clear();
        ToolbarGrid.RowDefinitions.Clear();

        var filters = new List<View> { FilterBox(_roleFilter), FilterBox(_statusFilter) };
        if (_setup.ShowClinic) filters.Add(FilterBox(_clinicFilter));

        if (_wide)
        {
            ToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ToolbarGrid.Add(SearchBox, 0, 0);
            for (int i = 0; i < filters.Count; i++)
            {
                ToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(i == 2 ? 210 : 170)));
                ToolbarGrid.Add(filters[i], i + 1, 0);
            }
        }
        else
        {
            // Narrow: search on its own row, filters side by side below it.
            for (int i = 0; i < filters.Count; i++) ToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ToolbarGrid.Add(SearchBox, 0, 0);
            Grid.SetColumnSpan(SearchBox, filters.Count);
            for (int i = 0; i < filters.Count; i++) ToolbarGrid.Add(filters[i], i, 1);
        }
    }

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => RaiseFiltersChanged();
        return timer;
    }

    private void RaiseFiltersChanged() => FiltersChanged?.Invoke(this, EventArgs.Empty);

    private void ClearFilters()
    {
        _suppressFilterEvents = true;
        SearchEntry.Text = string.Empty;
        _roleFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndex = 0;
        _clinicFilter.SelectedIndex = 0;
        _suppressFilterEvents = false;
        _searchTimer?.Stop();
        RaiseFiltersChanged();
    }

    // Table ------------------------------------------------------------------------------------

    private List<TableColumn> Columns() =>
        BaseColumns.Where(c => _setup.ShowClinic || c.Title != "Clinic").ToList();

    private View UserRow(User user, IReadOnlyList<TableColumn> columns, IReadOnlyList<UserAction> actions)
    {
        var grid = PageKit.RowGrid(columns, 66);
        int col = 0;

        grid.Add(PageKit.PersonCell(user.Initials, user.FullName, user.Email), col++, 0);
        grid.Add(PageKit.Cell(user.Username), col++, 0);
        grid.Add(Badges.Role(user.RoleId, user.RoleName), col++, 0);
        if (_setup.ShowClinic) grid.Add(PageKit.Cell(user.ClinicName ?? "—"), col++, 0);
        grid.Add(PageKit.Cell(string.IsNullOrWhiteSpace(user.ContactNo) ? "—" : user.ContactNo), col++, 0);
        grid.Add(Badges.Status(user.Status), col++, 0);
        grid.Add(Badges.Sync(user.SyncStatus), col++, 0);
        grid.Add(PageKit.Cell(UserDetailsView.LocalTime(user.UpdatedAt), muted: true), col++, 0);

        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + user.FullName,
            () => ActionsMenu.Show(menuButton, actions.Select(a => MenuEntryFor(user, a)).ToList()));
        grid.Add(menuButton, col, 0);

        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    private MenuEntry MenuEntryFor(User user, UserAction action)
    {
        void Raise() => ActionRequested?.Invoke(this, new UserActionEventArgs(user, action));
        return action switch
        {
            UserAction.View => new MenuEntry("View", "eye", Raise),
            UserAction.Edit => new MenuEntry("Edit", "pencil", Raise),
            UserAction.ResetPassword => new MenuEntry("Reset password", "key-round", Raise),
            UserAction.Deactivate => new MenuEntry("Deactivate", "user-x", Raise, Danger: true),
            _ => new MenuEntry("Activate", "user-check", Raise)
        };
    }

    // States -------------------------------------------------------------------------------

    private void ShowState(View content)
    {
        TableStack.Children.Clear();
        TableStack.IsVisible = false;
        PagerRow.IsVisible = false;
        StateHost.Content = content;
        StateHost.IsVisible = true;
    }

    private View NoMatches()
    {
        var clear = new TextLink { Text = "Clear filters", HorizontalOptions = LayoutOptions.Center };
        clear.StyleClass = new[] { "link-accent" };
        clear.Clicked += (s, e) => ClearFilters();

        return new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(24, 40),
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "No users match your filters.", FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center },
                clear
            }
        };
    }

    // Permission table -----------------------------------------------------------------------------

    private void BuildPermissionTable()
    {
        var columns = new List<TableColumn> { new("Module", 220) };
        columns.AddRange(PermissionMatrix.RoleCards.Select(r => new TableColumn(r.Name, 170)));

        PermissionStack.Children.Add(PageKit.HeaderRow(columns));
        foreach (var row in PermissionMatrix.Rows)
        {
            var grid = PageKit.RowGrid(columns, 52);
            grid.Add(new Label
            {
                Text = row.Module,
                FontFamily = "DMSansMedium",
                FontSize = 15,
                TextColor = ThemeResources.Color("Navy"),
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            int col = 1;
            foreach (var role in PermissionMatrix.RoleCards)
                grid.Add(Badges.Permission(row.For(role.RoleId)), col++, 0);

            PermissionStack.Children.Add(PageKit.WithBottomLine(grid));
        }
    }

    /// <summary>Cards wrap to 2 per row and the toolbar stacks on narrower widths.</summary>
    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        bool wide = width >= 980;
        if (wide == _wide) return;
        _wide = wide;

        LayoutToolbar();
        PageKit.Reflow(SummaryGrid, _wide ? 4 : 2);
        PageKit.Reflow(RoleCardsGrid, _wide ? 4 : 2);
    }
}
