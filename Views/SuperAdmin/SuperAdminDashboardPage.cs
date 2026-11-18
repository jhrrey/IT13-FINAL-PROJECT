using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Super Admin Dashboard (Tasks/VetCare_Super_Admin_Dashboard.md, section 5): header with the server pill,
/// quick actions, six stat cards, Attention needed + Sync activity, Clinic growth / Users by role / Platform usage,
/// and Recent sign-ups / Clinic sync health / Recent setting changes. Counts and statuses only.
/// </summary>
public sealed class SuperAdminDashboardPage : ContentView
{
    private readonly ContentView _body = new();

    public SuperAdminDashboardPage()
    {
        Content = _body;
        ShowState(PageKit.StateMessage("Loading the dashboard…"));
    }

    /// <summary>Open a module: (menu key, record id, intent). Rule 6: every card and row opens the related page.</summary>
    public Action<string, int?, string> Open { get; set; } = (_, _, _) => { };

    public event EventHandler RefreshRequested;

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    /// <param name="online">False keeps the last loaded numbers and shows the red server pill (rule 8).</param>
    public void Show(SuperAdminDashboardData d, bool online)
    {
        _body.Content = DashboardKit.Page(
            Header(d, online),
            QuickActions(d),
            StatCards(d),
            DashboardKit.Columns(860, new double[] { 45, 55 }, AttentionCard(d), SyncActivityCard(d)),
            DashboardKit.Columns(980, new double[] { 1, 1, 1 }, GrowthCard(d), UsersCard(d), UsageCard(d)),
            DashboardKit.Columns(980, new double[] { 1, 1, 1 }, SignupsCard(d), SyncHealthCard(d), SettingsCard(d)));
    }

    private void ShowState(View state) => _body.Content = DashboardKit.Page(Card(state));

    // A. Header ------------------------------------------------------------------------------------------------------

    private View Header(SuperAdminDashboardData d, bool online)
    {
        var now = DateTime.Now;
        var titles = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = GreetingHelper.Greeting(now) + ", Super Admin", FontFamily = "NunitoExtraBold", FontSize = 30, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = now.ToString("dddd, MMM d, yyyy") + " · VetCare Platform", FontSize = 15, TextColor = ThemeResources.Color("BodyText") }
            }
        };
        var (icon, text, bg, fg) = online
            ? ("cloud-check", "Server online · Updated " + d.LoadedAt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture), "Mint", "CompletedText")
            : ("cloud-off", "Can't reach server · Showing last loaded data", "DangerSoft", "DangerHover");
        var pill = new Border
        {
            HeightRequest = 36,
            Padding = new Thickness(14, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = ThemeResources.Color(bg),
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(fg), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13.5, TextColor = ThemeResources.Color(fg), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var refresh = PharmacyKit.SmallButton("Refresh", "refresh-cw", false, () => RefreshRequested?.Invoke(this, EventArgs.Empty), "Refresh the dashboard");
        var right = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center, Children = { pill, refresh } };

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(titles, 0, 0);
        grid.Add(right, 1, 0);
        bool? narrowNow = null;
        grid.SizeChanged += (s, e) =>
        {
            bool narrow = grid.Width > 0 && grid.Width < 760;
            if (narrow == narrowNow) return;
            narrowNow = narrow;
            Dispatcher.Dispatch(() =>
            {
                if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
                Grid.SetRow(right, narrow ? 1 : 0);
                Grid.SetColumn(right, narrow ? 0 : 1);
            });
        };
        return grid;
    }

    // G. Quick actions ------------------------------------------------------------------------------------------------

    private View QuickActions(SuperAdminDashboardData d)
    {
        var wrap = new WrapLayout();
        var review = new Border
        {
            HeightRequest = 44,
            Padding = new Thickness(18, 0),
            Margin = new Thickness(0, 0, 10, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = ThemeResources.Color("Orange"),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = "clock", StrokeColor = Colors.White, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Review sign-ups", FontFamily = "DMSansBold", FontSize = 15, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center },
                    new Border
                    {
                        IsVisible = d.PendingClinics > 0,
                        Padding = new Thickness(8, 1),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 10 },
                        BackgroundColor = Colors.White,
                        VerticalOptions = LayoutOptions.Center,
                        Content = new Label { Text = d.PendingClinics.ToString(), FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color("Orange") }
                    }
                }
            }
        };
        DashboardKit.Clickable(review, () => Open(MenuCatalog.ClinicManagement, null, "tab:pending"), "Review sign-ups");
        review.Stroke = Colors.Transparent;
        wrap.Children.Add(review);
        foreach (var (text, icon, action) in new (string, string, Action)[]
                 {
                     ("Add clinic", "building-2", () => Open(MenuCatalog.ClinicManagement, null, "add")),
                     ("Add admin", "user-cog", () => Open(MenuCatalog.ClinicAdminAccounts, null, "add")),
                     ("System settings", "settings", () => Open(MenuCatalog.SystemSettings, null, null))
                 })
        {
            var button = new IconTextButton { Icon = icon, Text = text == "System settings" ? text : "+ " + text, Outline = true, Margin = new Thickness(0, 0, 10, 10) };
            button.Clicked += (s, e) => action();
            wrap.Children.Add(button);
        }
        return wrap;
    }

    // B. Stat cards -----------------------------------------------------------------------------------------------------

    private View StatCards(SuperAdminDashboardData d)
    {
        string oldest = d.OldestPending == null ? "None waiting" : "Oldest: " + GreetingHelper.Relative(d.OldestPending.Value, DateTime.Now);
        int healthyPercent = d.Devices == 0 ? 0 : (int)Math.Round(100.0 * d.HealthyDevices / d.Devices);
        var cards = new List<View>
        {
            DashboardKit.StatCard("building-2", "Cyan", "Navy", d.Clinics.ToString(), "Clinics",
                d.ActiveClinics + " active · " + d.PendingClinics + " pending · " + d.InactiveClinics + " inactive",
                onTap: () => Open(MenuCatalog.ClinicManagement, null, null)),
            DashboardKit.StatCard("clock", "WarningSoft", "Warning", d.PendingClinics.ToString(), "Pending approvals", oldest,
                onTap: () => Open(MenuCatalog.ClinicManagement, null, "tab:pending")),
            DashboardKit.StatCard("users", "PaleBlue", "Navy", d.Users.ToString(), "Users",
                Plural(d.Admins, "admin") + " · " + Plural(d.Vets, "vet") + " · " + d.Staff + " staff",
                onTap: () => Open(MenuCatalog.ClinicAdminAccounts, null, null)),
            DashboardKit.StatCard("monitor", "Mint", "Success", d.Devices.ToString(), "Devices",
                d.Devices == 0 ? "None registered yet" : healthyPercent + "% healthy",
                onTap: () => Open(MenuCatalog.DevicesSyncMonitoring, null, null)),
            DashboardKit.StatCard("cloud-off", "DangerSoft", "Danger", d.FailedSyncsToday.ToString(), "Failed syncs today", null,
                onTap: () => Open(MenuCatalog.DevicesSyncMonitoring, null, "tab:sync:failed:today")),
            DashboardKit.StatCard("git-merge", "OrangeSoft", "Orange", d.ConflictsToday.ToString(), "Conflicts today", null,
                onTap: () => Open(MenuCatalog.ClinicManagement, null, null))
        };
        return DashboardKit.StatGrid(cards, 6);
    }

    // C. Attention needed ---------------------------------------------------------------------------------------------

    private View AttentionCard(SuperAdminDashboardData d)
    {
        var head = new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new LucideIcon { Icon = "circle-alert", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center },
                new Label { Text = "Attention needed", FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
            }
        };
        if (d.Attention.Count > 0) head.Children.Add(Badges.Pill(d.Attention.Count.ToString(), "Warning", "WarningSoft"));
        var stack = new VerticalStackLayout { Spacing = 0, Children = { head, new BoxView { HeightRequest = 10, Color = Colors.Transparent } } };

        if (d.Attention.Count == 0)
        {
            stack.Children.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Padding = new Thickness(0, 14),
                Children =
                {
                    new IconBadge { Icon = "circle-check", Size = 36, IconSize = 18, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("Success") },
                    new Label { Text = "All clear — nothing needs your attention.", FontFamily = "DMSansMedium", FontSize = 15, TextColor = ThemeResources.Color("Success"), VerticalOptions = LayoutOptions.Center }
                }
            });
        }
        for (int i = 0; i < d.Attention.Count; i++)
        {
            if (i > 0) stack.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            stack.Children.Add(AttentionRow(d.Attention[i]));
        }
        return Card(stack);
    }

    private View AttentionRow(AttentionItem a)
    {
        var (icon, circle, color, button, primary, action) = a.Kind switch
        {
            AttentionKind.PendingSignups => ("clock", "WarningSoft", "Warning", "Review", true, (Action)(() => Open(MenuCatalog.ClinicManagement, null, "tab:pending"))),
            AttentionKind.ClinicWithoutAdmin => ("user-cog", "OrangeSoft", "Orange", "Add admin", false, () => Open(MenuCatalog.ClinicAdminAccounts, null, "add:" + a.ClinicId)),
            AttentionKind.FailingDevice => ("cloud-off", "DangerSoft", "Danger", "View device", false, () => Open(MenuCatalog.DevicesSyncMonitoring, null, "device:" + a.DeviceId)),
            AttentionKind.StaleDevice => ("clock", "WarningSoft", "Warning", "View device", false, () => Open(MenuCatalog.DevicesSyncMonitoring, null, "device:" + a.DeviceId)),
            AttentionKind.ClinicNotSyncing => ("refresh-cw", "WarningSoft", "Warning", "View clinic", false, () => Open(MenuCatalog.ClinicManagement, a.ClinicId, "clinic:" + a.ClinicId)),
            _ => ("circle-pause", "NeutralSoft", "BodyText", "View clinic", false, () => Open(MenuCatalog.ClinicManagement, a.ClinicId, "clinic:" + a.ClinicId))
        };
        var badge = new IconBadge { Icon = icon, Size = 36, IconSize = 18, CircleColor = ThemeResources.Color(circle), IconColor = ThemeResources.Color(color), VerticalOptions = LayoutOptions.Center };
        // Titles wrap (up to 2 lines) so "Consultation Room 2 hasn't synced in 3 days" isn't cut short.
        var left = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        left.Add(badge, 0, 0);
        left.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = a.Title, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 },
                new Label { Text = a.Detail, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 }
            }
        }, 1, 0);
        return DashboardKit.Row(left, PharmacyKit.SmallButton(button, null, primary, action, button + ": " + a.Title), action);
    }

    // Sync activity ----------------------------------------------------------------------------------------------------

    private View SyncActivityCard(SuperAdminDashboardData d)
    {
        var chart = new StackedBarChartView
        {
            HeightRequest = 220,
            Series = new[] { ("Success", Color.FromArgb("#0A2E4F")), ("Failed", Color.FromArgb("#D4421C")) },
            Points = d.SyncDays.Select(x => new ChartPoint(x.Day, x.Success, x.Failed)).ToList()
        };
        int success = d.SyncDays.Sum(x => x.Success), total = success + d.SyncDays.Sum(x => x.Failed);
        var stats = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        stats.Add(MiniStat("Success rate", total == 0 ? "—" : (100.0 * success / total).ToString("0.0") + "%"), 0, 0);
        stats.Add(MiniStat("Avg. sync time", d.AvgSyncSeconds == null ? "—" : d.AvgSyncSeconds.Value.ToString("0.0") + " s"), 1, 0);
        stats.Add(MiniStat("Records synced", d.RecordsSynced.ToString("N0")), 2, 0);
        var body = new VerticalStackLayout { Spacing = 14, Children = { Title("Sync activity – last 7 days"), chart, stats } };
        if (total == 0)
            body.Children.Insert(2, Note("No syncs yet. Clinic computers appear here once the Devices & sync module registers them."));
        return Card(body);
    }

    // D. Charts --------------------------------------------------------------------------------------------------------

    private View GrowthCard(SuperAdminDashboardData d)
    {
        var chart = new BarLineChartView
        {
            HeightRequest = 200,
            BarName = "New clinics",
            LineName = "Active clinics",
            Points = d.Growth.Select(g => new ChartPoint(g.Month, g.New, g.Active)).ToList()
        };
        var body = new VerticalStackLayout { Spacing = 12, Children = { Title("Clinic growth"), chart } };
        if (d.PendingClinics > 0)
            body.Children.Add(Note(d.PendingClinics == 1 ? "1 new sign-up is still pending." : d.PendingClinics + " new sign-ups are still pending."));
        return Card(body);
    }

    private View UsersCard(SuperAdminDashboardData d)
    {
        var slices = new (string Name, int Count)[] { ("Administrator", d.Admins), ("Veterinarian", d.Vets), ("Cashier/Staff", d.Staff) };
        var donut = new DonutChartView
        {
            HeightRequest = 170,
            WidthRequest = 170,
            HorizontalOptions = LayoutOptions.Center,
            Points = slices.Select(s => new ChartPoint(s.Name, s.Count)).ToList()
        };
        var legend = new VerticalStackLayout { Spacing = 8 };
        var colors = new[] { Color.FromArgb("#0A2E4F"), Color.FromArgb("#5DCFD6"), Color.FromArgb("#D4421C") };
        for (int i = 0; i < slices.Length; i++)
        {
            var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 2, Color = colors[i], VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(new Label { Text = slices[i].Name, FontSize = 14, TextColor = ThemeResources.Color("Navy") }, 1, 0);
            row.Add(new Label { Text = slices[i].Count.ToString(), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy") }, 2, 0);
            legend.Children.Add(row);
        }
        return Card(new VerticalStackLayout { Spacing = 14, Children = { Title("Users by role"), donut, legend } });
    }

    // F. Platform usage --------------------------------------------------------------------------------------------------

    private View UsageCard(SuperAdminDashboardData d)
    {
        string lastMonth = DateTime.Today.AddMonths(-1).ToString("MMM");
        var u = d.Usage;
        var rows = new VerticalStackLayout { Spacing = 0 };
        var items = new (string Icon, string Label, int Now, int Last)[]
        {
            ("calendar", "Appointments booked", u.Appointments, u.AppointmentsLast),
            ("paw-print", "New pets registered", u.Pets, u.PetsLast),
            ("file-pen", "Medical records written", u.Records, u.RecordsLast),
            ("receipt", "Bills created", u.Bills, u.BillsLast)
        };
        for (int i = 0; i < items.Length; i++)
        {
            var (icon, label, now, last) = items[i];
            if (i > 0) rows.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            var grid = new Grid { Padding = new Thickness(0, 10), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            grid.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("NavyMuted"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }, 1, 0);
            var change = Compare(now, last, lastMonth);
            grid.Add(new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.End,
                Children =
                {
                    new Label { Text = now.ToString("N0"), FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.End },
                    new Label { Text = change.Text, FontSize = 12, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color(change.Color), HorizontalOptions = LayoutOptions.End }
                }
            }, 2, 0);
            rows.Children.Add(grid);
        }
        var note = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center },
                new Label { Text = "Counts only. Clinic records and amounts stay private.", FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        };
        return Card(new VerticalStackLayout { Spacing = 10, Children = { Title("Platform usage this month"), rows, note } });
    }

    private static (string Text, string Color) Compare(int now, int last, string lastMonth)
    {
        if (last == 0) return (now == 0 ? "No change" : "New this month", now == 0 ? "Muted" : "Success");
        double percent = Math.Round((now - last) * 100.0 / last);
        return ((percent >= 0 ? "+" : "−") + Math.Abs(percent) + "% vs " + lastMonth, percent >= 0 ? "Success" : "Orange");
    }

    // E. Lists ---------------------------------------------------------------------------------------------------------

    private View SignupsCard(SuperAdminDashboardData d)
    {
        var rows = d.RecentSignups.Select(c =>
        {
            var tile = PageKit.Avatar(c.Initials, 34, 8, 12);
            var left = DashboardKit.TwoLines(c.ClinicName, Area(c.Address) + (c.DateCreated == null ? string.Empty : " · " + c.DateCreated.Value.ToString("MMM d")), leading: tile);
            View right = c.Status == AccountStatus.Pending
                ? new HorizontalStackLayout { Spacing = 6, Children = { Badges.ClinicStatus(c.Status), PharmacyKit.SmallButton("Review", null, true, () => Open(MenuCatalog.ClinicManagement, null, "tab:pending"), "Review " + c.ClinicName) } }
                : Badges.ClinicStatus(c.Status);
            return (View)DashboardKit.Row(left, right, () => Open(MenuCatalog.ClinicManagement, c.ClinicId, "clinic:" + c.ClinicId));
        }).ToList();
        return DashboardKit.ListCard("building-2", "Recent sign-ups", rows, "No clinics yet.", () => Open(MenuCatalog.ClinicManagement, null, null));
    }

    private View SyncHealthCard(SuperAdminDashboardData d)
    {
        var rows = d.SyncHealth.Select(h =>
        {
            string counts = Plural(h.Devices, "device") + " · " + h.Healthy + " healthy" + (h.Stale > 0 ? " · " + h.Stale + " stale" : string.Empty) + (h.Failing > 0 ? " · " + h.Failing + " failing" : string.Empty);
            string last = h.LastSync == null ? "Never synced" : "Last sync " + GreetingHelper.Relative(h.LastSync.Value, DateTime.Now);
            var (bg, fg) = h.Overall switch
            {
                SyncHealthHelper.Problem => ("DangerSoft", "DangerHover"),
                SyncHealthHelper.NeedsAttention => ("WarningSoft", "Warning"),
                SyncHealthHelper.AllGood => ("Mint", "Success"),
                _ => ("NeutralSoft", "BodyText")
            };
            var left = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = h.ClinicName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = counts, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = last, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                }
            };
            return (View)DashboardKit.Row(left, Badges.Pill(h.Overall, fg, bg), () => Open(MenuCatalog.DevicesSyncMonitoring, null, "clinic:" + h.ClinicId));
        }).ToList();
        return DashboardKit.ListCard("refresh-cw", "Clinic sync health", rows,
            "No clinic computers registered yet. A computer appears once a clinic Administrator signs in on it.",
            () => Open(MenuCatalog.DevicesSyncMonitoring, null, null), "monitor");
    }

    /// <summary>The last 5 System settings changes: setting, old → new, who and when.</summary>
    private View SettingsCard(SuperAdminDashboardData d)
    {
        var rows = d.SettingChanges.Select(c =>
        {
            var def = SettingCatalog.Get(c.SettingKey);
            string label = (def?.Label ?? c.SettingKey) + (c.ClinicName == null ? string.Empty : " (" + c.ClinicName + ")");
            var change = new FormattedString();
            change.Spans.Add(new Span { Text = SettingCatalog.Display(c.SettingKey, c.OldValue), TextColor = ThemeResources.Color("Muted"), TextDecorations = TextDecorations.Strikethrough, FontSize = 12.5 });
            change.Spans.Add(new Span { Text = " → ", TextColor = ThemeResources.Color("Muted"), FontSize = 12.5 });
            change.Spans.Add(new Span { Text = SettingCatalog.Display(c.SettingKey, c.NewValue), TextColor = ThemeResources.Color("Navy"), FontFamily = "DMSansBold", FontSize = 12.5 });
            var left = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = label, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { FormattedText = change, LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = (c.ChangedByName ?? "—") + " · " + c.ChangedAt.ToString("MMM d"), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                }
            };
            return (View)DashboardKit.Row(left, null, () => Open(MenuCatalog.SystemSettings, null, null));
        }).ToList();
        var card = DashboardKit.ListCard("settings", "Recent setting changes", rows, "No setting changes yet.", null, "settings");
        if (card.Content is VerticalStackLayout stack && stack.Children.FirstOrDefault() is Grid head)
        {
            var link = new TextLink { Text = "View history →", FontSize = 14, VerticalOptions = LayoutOptions.Center };
            link.StyleClass = new[] { "link-accent" };
            link.Clicked += (s, e) => Open(MenuCatalog.SystemSettings, null, "history");
            head.Add(link, 2, 0);
        }
        return card;
    }

    // Building blocks ----------------------------------------------------------------------------------------------------

    private static string Plural(int n, string word) => n + " " + word + (n == 1 ? string.Empty : "s");

    /// <summary>The first part of an address ("Mintal, Davao City" → "Mintal").</summary>
    private static string Area(string address) => string.IsNullOrWhiteSpace(address) ? "—" : address.Split(',')[0].Trim();

    private static Label Title(string text) => new() { Text = text, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy") };

    private static Label Note(string text) => new() { Text = text, FontSize = 13, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap };

    private static View MiniStat(string label, string value) => new VerticalStackLayout
    {
        Children =
        {
            new Label { Text = value, FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy") },
            new Label { Text = label, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
        }
    };

    private static Border Card(View content) => new()
    {
        Style = ThemeResources.Get<Style>("WorkspaceCard"),
        Padding = new Thickness(20),
        VerticalOptions = LayoutOptions.Fill,
        Content = content
    };
}
