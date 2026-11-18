using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Clinic admins page (Clinic Admin Accounts, section 3.A, Super Admin): summary cards, the "clinics without an
/// active admin" warning, a toolbar (search, clinic, status, Group by clinic) and the admins table.
/// </summary>
public sealed class ClinicAdminsPage : ContentView
{
    private const int PageSize = 10;

    private static readonly FlexColumn[] Columns =
    {
        new("Admin", 196, true), new("Username", 104, true), new("Clinic", 150, true),
        new("Status", 172), new("Created", 92), new("Sync", 84), new("", 34)
    };

    private readonly Grid _summary = new();
    private readonly ContentView _warning = new();
    private readonly Entry _search;
    private readonly PickerField _clinic, _status;
    private readonly ToggleSwitch _group;
    private readonly FlexTable _table;
    private readonly Grid _pager;
    private readonly Label _pagerLabel;
    private readonly Border _prev, _next;
    private CancellationTokenSource _searchDelay;
    private IReadOnlyList<User> _admins = Array.Empty<User>();
    private Func<User, bool> _isOnlyActiveAdmin = _ => false;
    private int _page = 1;
    private bool _quiet;

    public ClinicAdminsPage()
    {
        var add = new HoverButton { Text = "+  Add admin", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(22, 0), VerticalOptions = LayoutOptions.Center };
        add.Clicked += (s, e) => AddRequested?.Invoke(this, null);
        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new PageHeader { Icon = "user-cog", Title = "Clinic admins", Subtitle = "The people who run each clinic on VetCare." }, 0, 0);
        header.Add(add, 1, 0);

        // Toolbar ---------------------------------------------------------------------------------------------------
        Border searchBox;
        (searchBox, _search) = PharmacyKit.TextInput("Search name, username, or email", maxLength: 100);
        _search.TextChanged += async (s, e) => await DelayedFilterAsync();
        _clinic = new PickerField("clinic", "Clinic") { WidthRequest = 210, Margin = new Thickness(0, 0, 14, 8) };
        _clinic.SetItems(new (string, object)[] { ("All clinics", null) }, null);
        _clinic.SelectionChanged += (s, e) => { if (!_quiet) Changed(); };
        _status = new PickerField("status", "Status") { WidthRequest = 210, Margin = new Thickness(0, 0, 14, 8) };
        _status.SetItems(new (string, object)[]
        {
            ("All status", null), (AccountDisplayStatus.Active, AccountDisplayStatus.Active), (AccountDisplayStatus.Inactive, AccountDisplayStatus.Inactive),
            (AccountDisplayStatus.WaitingForApproval, AccountDisplayStatus.WaitingForApproval), (AccountDisplayStatus.ClinicInactive, AccountDisplayStatus.ClinicInactive)
        }, null);
        _status.Select(null);
        _status.SelectionChanged += (s, e) => Changed();
        _group = new ToggleSwitch("Group by clinic") { Margin = new Thickness(0, 0, 0, 20), VerticalOptions = LayoutOptions.End };
        _group.Toggled += (s, on) => Changed();

        var filters = new WrapLayout();
        filters.Children.Add(_clinic);
        filters.Children.Add(_status);
        filters.Children.Add(new ContentView { HeightRequest = 92, VerticalOptions = LayoutOptions.End, Content = _group });
        var toolbar = ClinicsPage.Card(new VerticalStackLayout
        {
            Spacing = 14,
            Children = { new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Search"), searchBox } }, filters }
        }, new Thickness(20, 16, 20, 8));

        _table = new FlexTable(Columns);
        _pagerLabel = new Label { FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center };
        _prev = PageKit.IconButton("chevron-left", "Previous page", () => { if (_page > 1) { _page--; Render(); } });
        _next = PageKit.IconButton("chevron-right", "Next page", () => { if (_page < PageCount) { _page++; Render(); } });
        _pager = new Grid { Padding = new Thickness(20, 12), ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        _pager.Add(_pagerLabel, 0, 0);
        _pager.Add(_prev, 1, 0);
        _pager.Add(_next, 2, 0);
        var tableCard = ClinicsPage.Card(new VerticalStackLayout { Children = { _table, _pager } }, new Thickness(0));

        // DashboardKit.Page turns off scroll anchoring, so the page stays at the top when the cards above load.
        Content = DashboardKit.Page(header, _summary, _warning, toolbar, tableCard);
        ShowSummary(null);
    }

    /// <summary>Add admin; the clinic id when it comes from a clinic row (warning card, group header).</summary>
    public event EventHandler<int?> AddRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<User> AdminSelected;
    public event EventHandler<(View Anchor, User Admin)> MenuRequested;

    public string SearchText => _search.Text?.Trim();
    public int? ClinicFilter => _clinic.SelectedValue as int?;
    public string StatusFilter => _status.SelectedValue as string;
    public bool Grouped => _group.IsOn;

    public void SetClinics(IReadOnlyList<Clinic> clinics)
    {
        object selected = _clinic.SelectedValue;
        _quiet = true;
        _clinic.SetItems(new (string, object)[] { ("All clinics", null) }.Concat(clinics.Select(c => (c.ClinicName, (object)c.ClinicId))), selected);
        if (_clinic.SelectedValue == null) _clinic.Select(null);
        _quiet = false;
    }

    // Summary and warning ----------------------------------------------------------------------------------------------

    public void ShowSummary((int Total, int Active, int Inactive, int ClinicsWithoutAdmin)? s)
    {
        string N(int? v) => v?.ToString() ?? "–";
        int without = s?.ClinicsWithoutAdmin ?? 0;
        var cards = new List<View>
        {
            DashboardKit.StatCard("user-cog", "Cyan", "Navy", N(s?.Total), "Total admins"),
            DashboardKit.StatCard("user-check", "Mint", "Success", N(s?.Active), "Active"),
            DashboardKit.StatCard("user-x", "NeutralSoft", "BodyText", N(s?.Inactive), "Inactive"),
            DashboardKit.StatCard("building-2", "OrangeSoft", "Orange", N(s?.ClinicsWithoutAdmin), "Clinics without an active admin")
        };
        var grid = DashboardKit.StatGrid(cards, 4);
        if (without > 0 && cards[3] is Border card && FindValueLabel(card) is Label value) value.TextColor = ThemeResources.Color("Orange");
        _summary.Children.Clear();
        _summary.Children.Add(grid);
    }

    public void ShowWarning(IReadOnlyList<Clinic> clinicsWithoutAdmin)
    {
        if (clinicsWithoutAdmin.Count == 0)
        {
            _warning.Content = new Border
            {
                Padding = new Thickness(16, 10),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = ThemeResources.Color("Mint"),
                Content = new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new LucideIcon { Icon = "circle-check", StrokeColor = ThemeResources.Color("Success"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = "Every active clinic has an active admin.", FontFamily = "DMSansMedium", FontSize = 14.5, TextColor = ThemeResources.Color("Success"), VerticalOptions = LayoutOptions.Center }
                    }
                }
            };
            return;
        }

        var rows = new VerticalStackLayout { Spacing = 0 };
        rows.Children.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new LucideIcon { Icon = "triangle-alert", StrokeColor = ThemeResources.Color("Warning"), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center },
                new Label { Text = "These clinics have no active admin", FontFamily = "NunitoExtraBold", FontSize = 17, TextColor = ThemeResources.Color("Warning"), VerticalOptions = LayoutOptions.Center }
            }
        });
        foreach (var c in clinicsWithoutAdmin)
        {
            var row = new Grid { Padding = new Thickness(0, 8), ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = c.ClinicName, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                    Badges.SmallClinicStatus(c.Status)
                }
            }, 0, 0);
            int id = c.ClinicId;
            row.Add(PharmacyKit.SmallButton("Add admin", "plus", true, () => AddRequested?.Invoke(this, id), "Add admin to " + c.ClinicName), 1, 0);
            rows.Children.Add(row);
        }
        _warning.Content = new Border
        {
            Padding = new Thickness(20, 16),
            Stroke = ThemeResources.Color("WarningBorder"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = ThemeResources.Color("WarningSoft"),
            Content = rows
        };
    }

    // Table --------------------------------------------------------------------------------------------------------------

    public void ShowLoading()
    {
        _table.Clear();
        _table.AddMessage(PageKit.StateMessage("Loading admins…"));
        _pager.IsVisible = false;
    }

    public void ShowError(string message)
    {
        _table.Clear();
        _table.AddMessage(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
        _pager.IsVisible = false;
    }

    /// <param name="admins">Every admin matching the filters (the page pages them, 10 at a time).</param>
    /// <param name="isOnlyActiveAdmin">True for an Active clinic's only active admin (Deactivate is disabled).</param>
    public void ShowAdmins(IReadOnlyList<User> admins, Func<User, bool> isOnlyActiveAdmin, bool keepPage = false)
    {
        _admins = admins;
        _isOnlyActiveAdmin = isOnlyActiveAdmin;
        if (!keepPage) _page = 1;
        _page = Math.Min(_page, PageCount);
        Render();
    }

    public bool IsOnlyActiveAdmin(User admin) => _isOnlyActiveAdmin(admin);

    private int PageCount => Grouped ? 1 : Math.Max(1, (_admins.Count + PageSize - 1) / PageSize);

    private void Render()
    {
        _table.Clear();
        if (_admins.Count == 0)
        {
            bool filtered = !string.IsNullOrEmpty(SearchText) || ClinicFilter != null || StatusFilter != null;
            _table.AddMessage(filtered
                ? PageKit.StateMessage("No admins match your filters.")
                : PageKit.EmptyState("user-cog", "No clinic admins yet", null, "Add admin", () => AddRequested?.Invoke(this, null)));
            _pager.IsVisible = false;
            return;
        }

        if (Grouped)
        {
            foreach (var group in _admins.GroupBy(a => (a.ClinicId, a.ClinicName, a.ClinicStatus)).OrderBy(g => g.Key.ClinicName))
            {
                _table.AddMessage(GroupHeader(group.Key.ClinicId, group.Key.ClinicName, group.Key.ClinicStatus, group.Count()));
                foreach (var a in group) AddRow(a);
            }
            _pagerLabel.Text = _admins.Count == 1 ? "1 admin" : _admins.Count + " admins";
            _prev.IsVisible = _next.IsVisible = false;
        }
        else
        {
            foreach (var a in _admins.Skip((_page - 1) * PageSize).Take(PageSize)) AddRow(a);
            int from = (_page - 1) * PageSize + 1, to = Math.Min(_page * PageSize, _admins.Count);
            _pagerLabel.Text = $"Showing {from}–{to} of {_admins.Count} {(_admins.Count == 1 ? "admin" : "admins")}";
            _prev.IsVisible = _next.IsVisible = PageCount > 1;
            _prev.Opacity = _page > 1 ? 1 : 0.4;
            _next.Opacity = _page < PageCount ? 1 : 0.4;
        }
        _pager.IsVisible = true;
    }

    private void AddRow(User a)
    {
        var row = _table.NewRow(66);
        var person = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 12, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        person.Add(PageKit.Avatar(a.Initials), 0, 0);
        person.Add(Lines(a.FullName, a.Email, bold: true), 1, 0);
        row.Add(person, 0, 0);
        row.Add(Lines(a.Username, string.IsNullOrEmpty(a.ContactNo) ? "No contact no." : ClinicsPage.Phone(a.ContactNo), bold: false), 1, 0);
        row.Add(new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new Label { Text = a.ClinicName ?? "—", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 },
                Badges.SmallClinicStatus(a.ClinicStatus)
            }
        }, 2, 0);
        row.Add(Badges.AdminStatus(a.DisplayStatus), 3, 0);
        row.Add(PageKit.Cell(User.ToLocal(a.CreatedAt)?.ToString("MMM d, yyyy")), 4, 0);
        row.Add(Badges.Sync(a.SyncStatus ?? SyncStatuses.Synced), 5, 0);
        Border menu = null;
        menu = PageKit.IconButton("ellipsis-vertical", "More for " + a.FullName, () => MenuRequested?.Invoke(this, (menu, a)));
        row.Add(menu, 6, 0);
        var admin = a;
        _table.AddRow(row, () => AdminSelected?.Invoke(this, admin));
    }

    private View GroupHeader(int? clinicId, string clinicName, string clinicStatus, int count)
    {
        var grid = new Grid
        {
            Padding = new Thickness(20, 10),
            ColumnSpacing = 12,
            BackgroundColor = ThemeResources.Color("NeutralSoft"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(ClinicsPage.Tile(new Clinic { ClinicName = clinicName }.Initials, 32, 12), 0, 0);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = clinicName ?? "—", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                Badges.SmallClinicStatus(clinicStatus),
                new Label { Text = count == 1 ? "1 admin" : count + " admins", FontSize = 13, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        }, 1, 0);
        if (clinicStatus is AccountStatus.Active or AccountStatus.Pending)
        {
            var link = new TextLink { Text = "+ Add admin", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
            link.Clicked += (s, e) => AddRequested?.Invoke(this, clinicId);
            grid.Add(link, 2, 0);
        }
        return PageKit.WithBottomLine(grid);
    }

    private static View Lines(string first, string second, bool bold) => new VerticalStackLayout
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

    private void Changed()
    {
        _page = 1;
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DelayedFilterAsync()
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        try { await Task.Delay(300, cts.Token); }
        catch (TaskCanceledException) { return; }
        Changed();
    }

    /// <summary>The big number label inside a DashboardKit stat card.</summary>
    private static Label FindValueLabel(Border card) =>
        card.Content is Grid g && g.Children.OfType<VerticalStackLayout>().FirstOrDefault() is VerticalStackLayout v ? v.Children.OfType<Label>().FirstOrDefault() : null;
}
