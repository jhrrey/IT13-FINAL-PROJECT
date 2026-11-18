using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>Sync log filters (Clinic details → Sync log).</summary>
public sealed record SyncLogFilter(DateTime From, DateTime To, int? DeviceId, string Status);

/// <summary>
/// Clinic details page (section 4.D): header card with Edit and Deactivate / Activate, the privacy note, and the
/// Overview (counts only), Administrators, Devices and Sync log tabs.
/// </summary>
public sealed class ClinicDetailsPage : ContentView
{
    public const int OverviewTab = 0;
    public const int AdminsTab = 1;
    public const int DevicesTab = 2;
    public const int SyncTab = 3;

    private static readonly FlexColumn[] AdminColumns = { new("Admin", 230, true), new("Username", 130, true), new("Status", 110), new("Last login", 150), new("", 44) };
    private static readonly FlexColumn[] DeviceColumns =
        { new("Device", 190, true), new("Machine code", 130), new("Registered", 116), new("Last sync", 104), new("Status", 100), new("Active", 70) };
    private static readonly FlexColumn[] SyncColumns =
        { new("Date & time", 150), new("Device", 160, true), new("User", 120, true), new("Direction", 92), new("Sent", 58), new("Received", 78), new("Conflicts", 80), new("Status", 104) };

    private readonly Clinic _clinic;
    private readonly TabStrip _tabs;
    private readonly ContentView _body = new();
    private readonly DateBox _from, _to;
    private readonly PickerField _device, _status;
    private readonly View _syncFilters;
    private bool _quiet;

    public ClinicDetailsPage(Clinic clinic)
    {
        _clinic = clinic;

        var back = new TextLink { Text = "← Clinics", FontSize = 15, TextColor = ThemeResources.Color("Navy") };
        back.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);

        _tabs = new TabStrip().Add("Overview").Add("Administrators").Add("Devices").Add("Sync log");
        _tabs.TabChanged += (s, i) => { _syncFilters.IsVisible = i == SyncTab; TabChanged?.Invoke(this, i); };

        _from = new DateBox { Placeholder = "From", WidthRequest = 160, MaximumDate = DateTime.Today, Date = DateTime.Today.AddDays(-6) };
        _to = new DateBox { Placeholder = "To", WidthRequest = 160, MaximumDate = DateTime.Today, Date = DateTime.Today };
        _device = new PickerField("device", "Device") { WidthRequest = 200 };
        _device.SetItems(new (string, object)[] { ("All devices", null) }, null);
        _status = new PickerField("status", "Status") { WidthRequest = 170 };
        _status.SetItems(new (string, object)[] { ("All status", null), ("Success", "Success"), ("Failed", "Failed") }, null);
        _status.Select(null);
        _from.DateChanged += (s, e) => { if (!_quiet) SyncFiltersChanged?.Invoke(this, EventArgs.Empty); };
        _to.DateChanged += (s, e) => { if (!_quiet) SyncFiltersChanged?.Invoke(this, EventArgs.Empty); };
        _device.SelectionChanged += (s, e) => { if (!_quiet) SyncFiltersChanged?.Invoke(this, EventArgs.Empty); };
        _status.SelectionChanged += (s, e) => { if (!_quiet) SyncFiltersChanged?.Invoke(this, EventArgs.Empty); };
        var filters = new WrapLayout();
        foreach (var (label, view) in new (string, View)[] { ("From", _from), ("To", _to) })
            filters.Children.Add(new VerticalStackLayout { Margin = new Thickness(0, 0, 12, 8), Children = { PharmacyKit.FieldLabel(label), view } });
        _device.Margin = new Thickness(0, 0, 12, 8);
        _status.Margin = new Thickness(0, 0, 12, 8);
        filters.Children.Add(_device);
        filters.Children.Add(_status);
        _syncFilters = ClinicsPage.Card(filters, new Thickness(20, 16, 8, 8));
        _syncFilters.IsVisible = false;

        var privacy = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                new Label { Text = "Super Admins can see counts only. Patient and billing details stay private to the clinic.", FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        };

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(40, 28, 40, 40),
            Spacing = 20,
            Children = { back, HeaderCard(), privacy, _tabs, _syncFilters, _body }
        };
        var scroll = new ScrollView { Content = stack };
        ScrollFocus.DontScrollOnFocus(scroll);
        Content = scroll;
        ShowLoading();
    }

    public event EventHandler BackRequested;
    public event EventHandler EditRequested;
    public event EventHandler StatusToggleRequested;
    public event EventHandler ReviewRequested;
    public event EventHandler<int> TabChanged;
    public event EventHandler AddAdminRequested;
    public event EventHandler<(View Anchor, User Admin)> AdminMenuRequested;
    public event EventHandler<ClinicDevice> DeviceToggleRequested;
    public event EventHandler SyncFiltersChanged;

    public Clinic Clinic => _clinic;
    public int CurrentTab => _tabs.Current;

    public SyncLogFilter ReadSyncFilter()
    {
        var from = _from.Date ?? DateTime.Today.AddDays(-6);
        var to = _to.Date ?? DateTime.Today;
        if (to < from) (from, to) = (to, from);
        return new SyncLogFilter(from, to, _device.SelectedValue as int?, _status.SelectedValue as string);
    }

    public void OpenTab(int tab) => _tabs.Select(tab, raise: true);

    /// <summary>Presets the Sync log filters (e.g. today's failed syncs from the dashboard) without reloading.</summary>
    public void SetSyncFilter(DateTime day, string status)
    {
        _quiet = true;
        _from.Date = day;
        _to.Date = day;
        _status.Select(status);
        _quiet = false;
    }

    public void ShowLoading() => _body.Content = ClinicsPage.Card(PageKit.StateMessage("Loading…"), new Thickness(0));

    public void ShowError(string message) => _body.Content = ClinicsPage.Card(PageKit.StateMessage(message, "circle-alert", "DangerSoft"), new Thickness(0));

    // Overview --------------------------------------------------------------------------------------------------------

    public void ShowOverview(ClinicOverview o)
    {
        string usersSub = o.Admins + (o.Admins == 1 ? " admin" : " admins") + " · " + o.Vets + (o.Vets == 1 ? " vet" : " vets") + " · " + o.Staff + " staff";
        string last = o.LastActivity == null ? "None yet"
            : o.LastActivity.Value.Date == DateTime.Today ? "Today, " + o.LastActivity.Value.ToString("h:mm tt")
            : o.LastActivity.Value.Date == DateTime.Today.AddDays(-1) ? "Yesterday, " + o.LastActivity.Value.ToString("h:mm tt")
            : o.LastActivity.Value.ToString("MMM d, yyyy");
        var cards = new List<View>
        {
            DashboardKit.StatCard("users", "Cyan", "Navy", o.Users.ToString(), "Users", usersSub),
            DashboardKit.StatCard("contact", "PaleBlue", "Navy", o.Owners.ToString(), "Owners"),
            DashboardKit.StatCard("paw-print", "Mint", "Success", o.Pets.ToString(), "Pets"),
            DashboardKit.StatCard("calendar", "OrangeSoft", "Orange", o.AppointmentsThisMonth.ToString(), "Appointments this month"),
            DashboardKit.StatCard("monitor", "CyanSoft", "Navy", o.Devices.ToString(), "Devices"),
            DashboardKit.StatCard("clock", "WarningSoft", "Warning", last, "Last activity")
        };

        string healthText;
        bool healthy = o.StaleDevices == 0;
        if (o.Devices == 0) healthText = "No computers registered yet.";
        else if (healthy) healthText = "All devices synced in the last " + SyncHealthHelper.StaleHours + " hours.";
        else
        {
            string age = o.OldestSync == null ? "has never synced" : "hasn't synced in " + GreetingHelper.Relative(o.OldestSync.Value, DateTime.Now).Replace(" ago", string.Empty);
            healthText = o.StaleDevices == 1 ? "1 device " + age + "." : o.StaleDevices + " devices haven't synced in the last " + SyncHealthHelper.StaleHours + " hours.";
        }
        var health = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color(o.Devices == 0 ? "NeutralSoft" : healthy ? "Mint" : "WarningSoft"),
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new LucideIcon { Icon = healthy ? "circle-check" : "triangle-alert", StrokeColor = ThemeResources.Color(o.Devices == 0 ? "BodyText" : healthy ? "Success" : "Warning"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = healthText, FontFamily = "DMSansMedium", FontSize = 14.5, TextColor = ThemeResources.Color(o.Devices == 0 ? "BodyText" : healthy ? "Success" : "Warning"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var healthCard = ClinicsPage.Card(new VerticalStackLayout
        {
            Spacing = 12,
            Children = { new Label { Text = "Sync health", FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy") }, health }
        }, new Thickness(22, 18));

        _body.Content = new VerticalStackLayout { Spacing = 18, Children = { DashboardKit.StatGrid(cards, 3), healthCard } };
    }

    // Administrators ----------------------------------------------------------------------------------------------

    public void ShowAdmins(IReadOnlyList<User> admins)
    {
        var add = new HoverButton { Text = "+  Add admin", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(20, 0), HorizontalOptions = LayoutOptions.End };
        add.Clicked += (s, e) => AddAdminRequested?.Invoke(this, EventArgs.Empty);

        var table = new FlexTable(AdminColumns);
        if (admins.Count == 0) table.AddMessage(PageKit.EmptyState("user-cog", "No administrators", "Add an administrator so the clinic can sign in."));
        foreach (var a in admins)
        {
            var row = table.NewRow(64);
            row.Add(PageKit.PersonCell(a.Initials, a.FullName, a.Email), 0, 0);
            row.Add(PageKit.Cell(a.Username), 1, 0);
            row.Add(Badges.Status(a.Status), 2, 0);
            row.Add(PageKit.Cell(User.ToLocal(a.LastLoginAt)?.ToString("MMM d, h:mm tt") ?? "Never", muted: a.LastLoginAt == null), 3, 0);
            Border menu = null;
            var admin = a;
            menu = PageKit.IconButton("ellipsis-vertical", "More for " + a.FullName, () => AdminMenuRequested?.Invoke(this, (menu, admin)));
            row.Add(menu, 4, 0);
            table.AddRow(row);
        }
        _body.Content = new VerticalStackLayout { Spacing = 14, Children = { add, ClinicsPage.Card(table, new Thickness(0)) } };
    }

    // Devices ---------------------------------------------------------------------------------------------------------

    public void ShowDevices(IReadOnlyList<ClinicDevice> devices)
    {
        var table = new FlexTable(DeviceColumns);
        if (devices.Count == 0)
            table.AddMessage(PageKit.EmptyState("monitor", "No computers registered yet", "A clinic's computers appear here after its Administrator registers them."));
        var now = DateTime.Now;
        foreach (var d in devices)
        {
            var row = table.NewRow(62);
            row.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new LucideIcon { Icon = "monitor", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = d.DeviceName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }
                }
            }, 0, 0);
            var code = PageKit.Cell(d.MachineCode, muted: true);
            code.FontFamily = "Consolas";
            row.Add(code, 1, 0);
            row.Add(PageKit.Cell(d.RegisteredAt.ToString("MMM d, yyyy")), 2, 0);
            row.Add(ClinicsPage.LastSync(d.LastSyncAt, now), 3, 0);
            row.Add(Badges.Status(d.IsActive ? AccountStatus.Active : AccountStatus.Inactive), 4, 0);
            var device = d;
            row.Add(Toggle(d.IsActive, "Active: " + d.DeviceName, () => DeviceToggleRequested?.Invoke(this, device)), 5, 0);
            table.AddRow(row);
        }
        _body.Content = ClinicsPage.Card(table, new Thickness(0));
    }

    // Sync log --------------------------------------------------------------------------------------------------------

    public void SetDevices(IReadOnlyList<ClinicDevice> devices)
    {
        object selected = _device.SelectedValue;
        _quiet = true;
        _device.SetItems(new (string, object)[] { ("All devices", null) }.Concat(devices.Select(d => (d.DeviceName, (object)d.DeviceId))), selected);
        if (_device.SelectedValue == null) _device.Select(null);   // "All devices"
        _quiet = false;
    }

    public void ShowSyncLog(IReadOnlyList<SyncLogEntry> entries)
    {
        var table = new FlexTable(SyncColumns);
        if (entries.Count == 0)
            table.AddMessage(PageKit.EmptyState("refresh-cw", "No syncs in this period", "Syncs from the clinic's computers appear here."));
        foreach (var e in entries)
        {
            bool failed = e.Status != "Success" && !string.IsNullOrEmpty(e.ErrorMessage);
            var row = table.NewRow(failed ? 70 : 58);
            row.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = e.StartedAt.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                    new Label { Text = e.StartedAt.ToString("h:mm tt"), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                }
            }, 0, 0);
            var device = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { PageKit.Cell(e.DeviceName) } };
            if (failed)
                device.Children.Add(new Label { Text = e.ErrorMessage, FontSize = 12.5, TextColor = ThemeResources.Color("DangerHover"), LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(0, 0, 12, 0) });
            row.Add(device, 1, 0);
            row.Add(PageKit.Cell(e.UserName ?? "—", muted: e.UserName == null), 2, 0);
            row.Add(Badges.SyncDirection(e.Direction), 3, 0);
            row.Add(PageKit.Cell(e.RecordsSent.ToString()), 4, 0);
            row.Add(PageKit.Cell(e.RecordsReceived.ToString()), 5, 0);
            row.Add(PageKit.Cell(e.ConflictsFound.ToString()), 6, 0);
            row.Add(Badges.SyncResult(e.Status), 7, 0);
            table.AddRow(row);
        }
        _body.Content = ClinicsPage.Card(table, new Thickness(0));
    }

    // Header ----------------------------------------------------------------------------------------------------------

    private View HeaderCard()
    {
        var c = _clinic;
        // The status pill follows the name, and moves to the next line when the name is long.
        var pill = Badges.ClinicStatus(c.Status);
        pill.Margin = new Thickness(0, 6, 0, 0);
        var name = new WrapLayout
        {
            Children =
            {
                new Label { Text = c.ClinicName, FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(0, 0, 12, 0) },
                pill
            }
        };
        var info = new WrapLayout();
        foreach (var (icon, text) in new[]
                 {
                     ("map-pin", c.Address ?? "—"), ("phone", ClinicsPage.Phone(c.ContactNo)), ("mail", c.Email ?? "—"),
                     ("calendar", "Registered " + (c.DateCreated?.ToString("MMM d, yyyy") ?? "—"))
                 })
        {
            info.Children.Add(new HorizontalStackLayout
            {
                Spacing = 7,
                Margin = new Thickness(0, 0, 22, 6),
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("BodyText"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontSize = 14.5, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
                }
            });
        }
        var texts = new VerticalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center, Children = { name, info } };
        if (!string.IsNullOrEmpty(c.Remarks) && c.Status is AccountStatus.Inactive or AccountStatus.Rejected)
            texts.Children.Add(new Label { Text = "Reason: " + c.Remarks, FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap });

        var buttons = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Start };
        if (c.Status == AccountStatus.Pending)
        {
            var review = new HoverButton { Text = "Review sign-up", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(20, 0) };
            review.Clicked += (s, e) => ReviewRequested?.Invoke(this, EventArgs.Empty);
            buttons.Children.Add(review);
        }
        else if (c.Status != AccountStatus.Rejected)
        {
            var edit = new HoverButton { Text = "Edit", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
            edit.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
            bool active = c.Status == AccountStatus.Active;
            var toggle = new HoverButton
            {
                Text = active ? "Deactivate" : "Activate",
                Style = ThemeResources.Get<Style>(active ? "DangerOutlineButton" : "OutlineButton"),
                Padding = new Thickness(22, 0)
            };
            if (!active) { toggle.TextColor = ThemeResources.Color("Success"); toggle.BorderColor = ThemeResources.Color("Success"); }
            toggle.Clicked += (s, e) => StatusToggleRequested?.Invoke(this, EventArgs.Empty);
            buttons.Children.Add(edit);
            buttons.Children.Add(toggle);
        }

        var grid = new Grid { ColumnSpacing = 18, RowSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(ClinicsPage.Tile(c.Initials, 64, 20), 0, 0);
        grid.Add(texts, 1, 0);
        grid.Add(buttons, 2, 0);
        bool? narrowNow = null;
        grid.SizeChanged += (s, e) =>
        {
            bool narrow = grid.Width > 0 && grid.Width < 700;
            if (narrow == narrowNow) return;
            narrowNow = narrow;
            Dispatcher.Dispatch(() =>
            {
                if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
                Grid.SetRow(buttons, narrow ? 1 : 0);
                Grid.SetColumn(buttons, narrow ? 1 : 2);
            });
        };
        return ClinicsPage.Card(grid, new Thickness(24, 22));
    }

    /// <summary>A small on/off toggle (40×22) that asks the controller before changing.</summary>
    private static View Toggle(bool on, string description, Action onTap)
    {
        var knob = new Ellipse { WidthRequest = 16, HeightRequest = 16, Fill = Colors.White, HorizontalOptions = on ? LayoutOptions.End : LayoutOptions.Start, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(3, 0) };
        var track = new Border
        {
            WidthRequest = 40,
            HeightRequest = 22,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            BackgroundColor = ThemeResources.Color(on ? "SuccessDot" : "NeutralDot"),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid { Children = { knob } }
        };
        SemanticProperties.SetDescription(track, description + (on ? " (on)" : " (off)"));
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        track.GestureRecognizers.Add(tap);
        HandCursor.Apply(track);
        return track;
    }
}
