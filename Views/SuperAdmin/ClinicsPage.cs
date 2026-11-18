using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Clinics page (section 4.A, Super Admin): summary cards, then two tabs – All clinics (search, status filter, table)
/// and Pending approval (one card per sign-up, oldest first).
/// </summary>
public sealed class ClinicsPage : ContentView
{
    public const int AllTab = 0;
    public const int PendingTab = 1;
    private const int PageSize = 20;

    private static readonly FlexColumn[] Columns =
    {
        new("Clinic", 184, true), new("Address", 128, true), new("Administrator", 120, true),
        new("Users", 50), new("Devices", 62), new("Last sync", 86), new("Status", 100), new("Registered", 96), new("", 34)
    };

    private readonly Grid _summary;
    private readonly TabStrip _tabs;
    private readonly View _toolbar;
    private readonly Entry _search;
    private readonly PickerField _status;
    private readonly Border _tableCard;
    private readonly FlexTable _table;
    private readonly Grid _pager;
    private readonly Label _pagerLabel;
    private readonly Border _prev, _next;
    private readonly VerticalStackLayout _pending;
    private CancellationTokenSource _searchDelay;
    private IReadOnlyList<Clinic> _clinics = Array.Empty<Clinic>();
    private int _page = 1;

    public ClinicsPage()
    {
        var add = new HoverButton { Text = "+  Add clinic", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(22, 0), VerticalOptions = LayoutOptions.Center };
        add.Clicked += (s, e) => AddRequested?.Invoke(this, EventArgs.Empty);
        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new PageHeader { Icon = "building-2", Title = "Clinics", Subtitle = "Every clinic on VetCare, in one place." }, 0, 0);
        header.Add(add, 1, 0);

        _summary = new Grid();

        _tabs = new TabStrip().Add("All clinics").Add("Pending approval", ("WarningSoft", "Warning"));
        _tabs.TabChanged += (s, i) => { ShowTab(i); TabChanged?.Invoke(this, i); };

        // Toolbar (All clinics) -----------------------------------------------------------------------------------
        Border searchBox;
        (searchBox, _search) = PharmacyKit.TextInput("Search clinic name, email, or address", maxLength: 100);
        _search.TextChanged += async (s, e) => await DelayedFilterAsync();
        _status = new PickerField("status", "Status") { WidthRequest = 190 };
        _status.SetItems(new (string, object)[]
        {
            ("All status", null), (AccountStatus.Active, AccountStatus.Active), (AccountStatus.Pending, AccountStatus.Pending),
            (AccountStatus.Inactive, AccountStatus.Inactive), (AccountStatus.Rejected, AccountStatus.Rejected)
        }, null);
        _status.Select(null);   // "All status"
        _status.SelectionChanged += (s, e) => { _page = 1; FiltersChanged?.Invoke(this, EventArgs.Empty); };
        var toolbarGrid = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        toolbarGrid.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Children = { PharmacyKit.FieldLabel("Search"), searchBox } }, 0, 0);
        toolbarGrid.Add(_status, 1, 0);
        _toolbar = Card(toolbarGrid, new Thickness(20, 16));

        _table = new FlexTable(Columns);
        _pagerLabel = new Label { FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center };
        _prev = PageKit.IconButton("chevron-left", "Previous page", () => { if (_page > 1) { _page--; RenderTable(); } });
        _next = PageKit.IconButton("chevron-right", "Next page", () => { if (_page < PageCount) { _page++; RenderTable(); } });
        _pager = new Grid { Padding = new Thickness(20, 12), ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        _pager.Add(_pagerLabel, 0, 0);
        _pager.Add(_prev, 1, 0);
        _pager.Add(_next, 2, 0);
        _tableCard = Card(new VerticalStackLayout { Children = { _table, _pager } }, new Thickness(0));

        _pending = new VerticalStackLayout { Spacing = 14 };

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(40, 32, 40, 40),
            Spacing = 22,
            Children = { header, _summary, _tabs, _toolbar, _tableCard, _pending }
        };
        var scroll = new ScrollView { Content = stack };
        ScrollFocus.DontScrollOnFocus(scroll);
        Content = scroll;
        ShowTab(AllTab);
        ShowSummary(null);
    }

    public event EventHandler AddRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<int> TabChanged;
    public event EventHandler<Clinic> ClinicSelected;
    public event EventHandler<Clinic> ReviewRequested;
    public event EventHandler<(View Anchor, Clinic Clinic)> MenuRequested;

    public int CurrentTab => _tabs.Current;
    public string SearchText => _search.Text?.Trim();
    public string StatusFilter => _status.SelectedValue as string;

    public void OpenTab(int tab)
    {
        _tabs.Select(tab, raise: false);
        ShowTab(tab);
        TabChanged?.Invoke(this, tab);
    }

    // Summary --------------------------------------------------------------------------------------------------------

    public void ShowSummary(ClinicSummary s)
    {
        string N(Func<ClinicSummary, int> f) => s == null ? "–" : f(s).ToString();
        var cards = new List<View>
        {
            DashboardKit.StatCard("building-2", "Cyan", "Navy", N(x => x.Total), "Total clinics"),
            DashboardKit.StatCard("circle-check", "Mint", "Success", N(x => x.Active), "Active"),
            DashboardKit.StatCard("clock", "WarningSoft", "Warning", N(x => x.Pending), "Pending approval", onTap: () => OpenTab(PendingTab)),
            DashboardKit.StatCard("circle-pause", "NeutralSoft", "BodyText", N(x => x.Inactive), "Inactive")
        };
        var grid = DashboardKit.StatGrid(cards, 4);
        _summary.Children.Clear();
        _summary.Children.Add(grid);
        _tabs.SetCount(PendingTab, s?.Pending ?? 0);
    }

    // All clinics ----------------------------------------------------------------------------------------------------

    public void ShowLoading()
    {
        if (CurrentTab == PendingTab)
        {
            _pending.Children.Clear();
            _pending.Children.Add(Card(PageKit.StateMessage("Loading…"), new Thickness(0)));
            return;
        }
        _table.Clear();
        _table.AddMessage(PageKit.StateMessage("Loading clinics…"));
        _pager.IsVisible = false;
    }

    public void ShowError(string message)
    {
        var state = PageKit.StateMessage(message, "circle-alert", "DangerSoft");
        if (CurrentTab == PendingTab)
        {
            _pending.Children.Clear();
            _pending.Children.Add(Card(state, new Thickness(0)));
            return;
        }
        _table.Clear();
        _table.AddMessage(state);
        _pager.IsVisible = false;
    }

    public void ShowClinics(IReadOnlyList<Clinic> clinics, bool keepPage = false)
    {
        _clinics = clinics;
        if (!keepPage) _page = 1;
        _page = Math.Min(_page, PageCount);
        RenderTable();
    }

    private int PageCount => Math.Max(1, (_clinics.Count + PageSize - 1) / PageSize);

    private void RenderTable()
    {
        _table.Clear();
        if (_clinics.Count == 0)
        {
            bool filtered = !string.IsNullOrEmpty(SearchText) || StatusFilter != null;
            _table.AddMessage(filtered
                ? PageKit.StateMessage("No clinics match your search.")
                : PageKit.EmptyState("building-2", "No clinics yet", "Clinics appear here once they sign up or you add one."));
            _pager.IsVisible = false;
            return;
        }

        var now = DateTime.Now;
        foreach (var c in _clinics.Skip((_page - 1) * PageSize).Take(PageSize))
        {
            var row = _table.NewRow(68);
            row.Add(ClinicCell(c), 0, 0);
            row.Add(TwoLines(c.Address ?? "—", Phone(c.ContactNo), bold: false), 1, 0);
            row.Add(PageKit.Cell(c.AdminName ?? "—", muted: c.AdminName == null), 2, 0);
            row.Add(PageKit.Cell(c.UserCount.ToString()), 3, 0);
            row.Add(PageKit.Cell(c.DeviceCount.ToString()), 4, 0);
            row.Add(LastSync(c.LastSyncAt, now), 5, 0);
            row.Add(Badges.ClinicStatus(c.Status), 6, 0);
            row.Add(PageKit.Cell(c.DateCreated?.ToString("MMM d, yyyy") ?? "—"), 7, 0);
            Border menu = null;
            menu = PageKit.IconButton("ellipsis-vertical", "More for " + c.ClinicName, () => MenuRequested?.Invoke(this, (menu, c)));
            row.Add(menu, 8, 0);
            var clinic = c;
            _table.AddRow(row, () => ClinicSelected?.Invoke(this, clinic));
        }

        int from = (_page - 1) * PageSize + 1, to = Math.Min(_page * PageSize, _clinics.Count);
        _pagerLabel.Text = $"Showing {from}–{to} of {_clinics.Count} {(_clinics.Count == 1 ? "clinic" : "clinics")}";
        _prev.IsVisible = _next.IsVisible = PageCount > 1;
        _prev.Opacity = _page > 1 ? 1 : 0.4;
        _next.Opacity = _page < PageCount ? 1 : 0.4;
        _pager.IsVisible = true;
    }

    // Pending approval -----------------------------------------------------------------------------------------------

    public void ShowPending(IReadOnlyList<Clinic> clinics)
    {
        _pending.Children.Clear();
        if (clinics.Count == 0)
        {
            var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
            empty.Children.Add(new IconBadge { Icon = "building-2", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("Success"), HorizontalOptions = LayoutOptions.Center });
            empty.Children.Add(new Label { Text = "No pending sign-ups", FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) });
            empty.Children.Add(new Label { Text = "New clinic registrations will appear here.", FontSize = 15, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.Center });
            _pending.Children.Add(Card(empty, new Thickness(0)));
            return;
        }
        var now = DateTime.Now;
        foreach (var c in clinics) _pending.Children.Add(PendingCard(c, now));
    }

    private View PendingCard(Clinic c, DateTime now)
    {
        var name = new HorizontalStackLayout { Spacing = 10, Children = { new Label { Text = c.ClinicName, FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center } } };
        if (c.PossibleDuplicate) name.Children.Add(Badges.IconPill("Possible duplicate", "triangle-alert", ("WarningSoft", "Warning")));
        string signedUp = c.DateCreated == null ? "Signed up" : "Signed up " + c.DateCreated.Value.ToString("MMM d, yyyy") + " · " + GreetingHelper.Relative(c.DateCreated.Value, now);
        var left = new HorizontalStackLayout
        {
            Spacing = 14,
            Children =
            {
                Tile(c.Initials, 48, 15),
                new VerticalStackLayout
                {
                    Spacing = 3,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        name,
                        new Label { Text = (c.Address ?? "—") + " · " + Phone(c.ContactNo), FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap },
                        new Label { Text = signedUp, FontSize = 13, TextColor = ThemeResources.Color("Muted") }
                    }
                }
            }
        };
        var admin = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "ADMINISTRATOR", FontFamily = "DMSansBold", FontSize = 11.5, CharacterSpacing = 0.6, TextColor = ThemeResources.Color("Muted") },
                new Label { Text = c.AdminName ?? "—", FontFamily = "DMSansMedium", FontSize = 15, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = c.AdminEmail ?? string.Empty, FontSize = 13, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.TailTruncation }
            }
        };
        var review = new HoverButton { Text = "Review", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(26, 0), VerticalOptions = LayoutOptions.Center };
        review.Clicked += (s, e) => ReviewRequested?.Invoke(this, c);

        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 14 };
        void Layout(bool narrow)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (narrow)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.Add(left, 0, 0);
                Grid.SetColumnSpan(left, 2);
                grid.Add(admin, 0, 1);
                grid.Add(review, 1, 1);
            }
            else
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(230)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Grid.SetColumnSpan(left, 1);
                grid.Add(left, 0, 0);
                grid.Add(admin, 1, 0);
                grid.Add(review, 2, 0);
            }
        }
        bool? narrowNow = null;
        grid.SizeChanged += (s, e) =>
        {
            bool narrow = grid.Width > 0 && grid.Width < 720;
            if (narrow == narrowNow) return;
            narrowNow = narrow;
            Dispatcher.Dispatch(() => Layout(narrow));
        };
        Layout(false);
        return Card(grid, new Thickness(22, 18));
    }

    // Building blocks ------------------------------------------------------------------------------------------------

    private void ShowTab(int tab)
    {
        bool all = tab == AllTab;
        _toolbar.IsVisible = all;
        _tableCard.IsVisible = all;
        _pending.IsVisible = !all;
    }

    private async Task DelayedFilterAsync()
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        try { await Task.Delay(300, cts.Token); }
        catch (TaskCanceledException) { return; }
        _page = 1;
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private static View ClinicCell(Clinic c)
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 12, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(Tile(c.Initials, 40, 14), 0, 0);
        grid.Add(TwoLines(c.ClinicName, c.Email, bold: true), 1, 0);
        return grid;
    }

    /// <summary>Two truncating lines that stay inside their column.</summary>
    private static View TwoLines(string first, string second, bool bold) => new VerticalStackLayout
    {
        Spacing = 1,
        VerticalOptions = LayoutOptions.Center,
        Margin = bold ? new Thickness(0) : new Thickness(0, 0, 12, 0),
        Children =
        {
            new Label { Text = first, FontFamily = bold ? "DMSansBold" : "DMSans", FontSize = bold ? 14.5 : 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 },
            new Label { Text = second ?? string.Empty, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 }
        }
    };

    /// <summary>"2 min ago"; red when over 24 hours; "Never" when no device has synced.</summary>
    public static View LastSync(DateTime? when, DateTime now)
    {
        if (when == null) return PageKit.Cell("Never", muted: true);
        bool stale = now - when.Value > TimeSpan.FromHours(SyncHealthHelper.StaleHours);
        var label = PageKit.Cell(GreetingHelper.Relative(when.Value, now));
        if (stale) { label.TextColor = ThemeResources.Color("Danger"); label.FontFamily = "DMSansMedium"; }
        return label;
    }

    /// <summary>Rounded-square tile with the clinic's initials on cyan.</summary>
    public static View Tile(string initials, double size, double fontSize) => PageKit.Avatar(initials, size, size >= 60 ? 14 : 10, fontSize);

    public static string Phone(string contact)
    {
        if (string.IsNullOrWhiteSpace(contact)) return "—";
        string digits = Validator.Digits(contact);
        if (digits.Length == 11 && digits.StartsWith("09")) return Validator.FormatPhMobile(digits);
        if (digits.Length == 11 && digits.StartsWith("0")) return digits.Substring(0, 4) + " " + digits.Substring(4, 3) + " " + digits.Substring(7);
        return contact;
    }

    public static Border Card(View content, Thickness padding) => new()
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = padding,
        Content = content
    };
}
