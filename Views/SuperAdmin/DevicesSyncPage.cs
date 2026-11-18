using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Devices & sync logs (Devices & Sync Monitoring, section 4.A): summary cards, the 7-day chart, and the Devices,
/// Sync log and Clinic sync health tabs. The Administrator version (one clinic) has no Clinic column, Clinic filter or
/// Clinic sync health tab.
/// </summary>
public sealed class DevicesSyncPage : ContentView
{
    public const int DevicesTab = 0;
    public const int LogTab = 1;
    public const int HealthTab = 2;

    private readonly bool _allClinics;
    private readonly Label _updated;
    private readonly Grid _summary = new();
    private readonly StackedBarChartView _chart;
    private readonly TabStrip _tabs;
    private readonly ContentView _toolbar = new();
    private readonly ContentView _body = new();

    // Devices tab filters
    private readonly Entry _search;
    private readonly Border _searchBox;
    private readonly PickerField _clinic, _health;
    // Sync log filters
    private readonly DateBox _from, _to;
    private readonly PickerField _logClinic, _logDevice, _direction, _status;
    private readonly View _deviceFilters, _logFilters;
    private CancellationTokenSource _searchDelay;
    private bool _quiet;

    public DevicesSyncPage(bool allClinics)
    {
        _allClinics = allClinics;

        _updated = new Label { FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center };
        var refresh = PharmacyKit.SmallButton("Refresh", "refresh-cw", false, () => RefreshRequested?.Invoke(this, EventArgs.Empty), "Refresh");
        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new PageHeader
        {
            Icon = "refresh-cw",
            Title = allClinics ? "Devices & sync logs" : "Devices & sync",
            Subtitle = allClinics ? "Make sure every clinic computer is saving to the cloud." : "Your clinic's computers and how they're syncing."
        }, 0, 0);
        header.Add(new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = { _updated, refresh } }, 1, 0);

        _chart = new StackedBarChartView
        {
            HeightRequest = 200,
            Series = new[] { ("Success", Color.FromArgb("#0A2E4F")), ("Failed", Color.FromArgb("#D4421C")) }
        };
        var chartCard = ClinicsPage.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { new Label { Text = "Syncs – last 7 days", FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy") }, _chart }
        }, new Thickness(20));

        _tabs = new TabStrip().Add("Devices").Add("Sync log");
        if (allClinics) _tabs.Add("Clinic sync health");
        _tabs.TabChanged += (s, i) => { ShowToolbar(); TabChanged?.Invoke(this, i); };

        // Devices tab toolbar ---------------------------------------------------------------------------------------
        (_searchBox, _search) = PharmacyKit.TextInput("Search device name or machine code", maxLength: 100);
        _search.TextChanged += async (s, e) => await DelayedAsync();
        _clinic = new PickerField("clinic", "Clinic") { WidthRequest = 220, Margin = new Thickness(0, 0, 12, 8) };
        _clinic.SetItems(new (string, object)[] { ("All clinics", null) }, null);
        _clinic.SelectionChanged += (s, e) => { if (!_quiet) FiltersChanged?.Invoke(this, EventArgs.Empty); };
        _health = new PickerField("health", "Health") { WidthRequest = 190, Margin = new Thickness(0, 0, 12, 8) };
        _health.SetItems(new (string, object)[]
        {
            ("All health", null), (SyncHealthHelper.Healthy, SyncHealthHelper.Healthy), (SyncHealthHelper.Stale, SyncHealthHelper.Stale),
            (SyncHealthHelper.Failing, SyncHealthHelper.Failing), (SyncHealthHelper.NeverSynced, SyncHealthHelper.NeverSynced), (SyncHealthHelper.Inactive, SyncHealthHelper.Inactive)
        }, null);
        _health.Select(null);
        _health.SelectionChanged += (s, e) => { if (!_quiet) FiltersChanged?.Invoke(this, EventArgs.Empty); };
        var deviceWrap = new WrapLayout();
        if (allClinics) deviceWrap.Children.Add(_clinic);
        deviceWrap.Children.Add(_health);
        _deviceFilters = ClinicsPage.Card(new VerticalStackLayout
        {
            Spacing = 12,
            Children = { new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Search"), _searchBox } }, deviceWrap }
        }, new Thickness(20, 16, 20, 8));

        // Sync log toolbar ------------------------------------------------------------------------------------------
        _from = new DateBox { Placeholder = "From", WidthRequest = 160, MaximumDate = DateTime.Today, Date = DateTime.Today.AddDays(-6) };
        _to = new DateBox { Placeholder = "To", WidthRequest = 160, MaximumDate = DateTime.Today, Date = DateTime.Today };
        _from.DateChanged += (s, e) => { if (!_quiet) FiltersChanged?.Invoke(this, EventArgs.Empty); };
        _to.DateChanged += (s, e) => { if (!_quiet) FiltersChanged?.Invoke(this, EventArgs.Empty); };
        _logClinic = Picker("clinic", "Clinic", 200, ("All clinics", null));
        _logDevice = Picker("device", "Device", 200, ("All devices", null));
        _direction = Picker("direction", "Direction", 170, ("All directions", null), ("Push", "Push"), ("Pull", "Pull"));
        _status = Picker("status", "Status", 160, ("All status", null), ("Success", "Success"), ("Failed", "Failed"));
        var logWrap = new WrapLayout();
        foreach (var (label, view) in new (string, View)[] { ("From", _from), ("To", _to) })
            logWrap.Children.Add(new VerticalStackLayout { Margin = new Thickness(0, 0, 12, 8), Children = { PharmacyKit.FieldLabel(label), view } });
        if (allClinics) logWrap.Children.Add(_logClinic);
        logWrap.Children.Add(_logDevice);
        logWrap.Children.Add(_direction);
        logWrap.Children.Add(_status);
        _logFilters = ClinicsPage.Card(logWrap, new Thickness(20, 16, 8, 8));

        Content = DashboardKit.Page(header, _summary, chartCard, _tabs, _toolbar, _body);
        ShowToolbar();
        ShowSummary(null);
        ShowLoading();
    }

    public event EventHandler RefreshRequested;
    public event EventHandler FiltersChanged;
    public event EventHandler<int> TabChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<ClinicDevice> DeviceSelected;
    public event EventHandler<(View Anchor, ClinicDevice Device)> DeviceMenuRequested;
    public event EventHandler<ClinicDevice> DeviceToggleRequested;
    public event EventHandler<int> ClinicSelected;

    public int CurrentTab => _tabs.Current;
    public string SearchText => _search.Text?.Trim();
    public int? ClinicFilter => _clinic.SelectedValue as int?;
    public string HealthFilter => _health.SelectedValue as string;

    public SyncLogQuery ReadLogQuery(int page)
    {
        var from = _from.Date ?? DateTime.Today.AddDays(-6);
        var to = _to.Date ?? DateTime.Today;
        if (to < from) (from, to) = (to, from);
        return new SyncLogQuery(from, to, _allClinics ? _logClinic.SelectedValue as int? : null, _logDevice.SelectedValue as int?,
            _direction.SelectedValue as string, _status.SelectedValue as string, page);
    }

    public void OpenTab(int tab) => _tabs.Select(tab, raise: true);

    /// <summary>Sets filters without reloading (dashboard shortcuts, health cards, clinic rows).</summary>
    public void Preset(int? clinicId = null, string health = null, DateTime? logDay = null, string logStatus = null)
    {
        _quiet = true;
        _clinic.Select(clinicId);
        _health.Select(health);
        if (logDay != null) { _from.Date = logDay; _to.Date = logDay; }
        if (logStatus != null) _status.Select(logStatus);
        _quiet = false;
    }

    public void SetPickers(IReadOnlyList<Clinic> clinics, IReadOnlyList<ClinicDevice> devices)
    {
        _quiet = true;
        object c1 = _clinic.SelectedValue, c2 = _logClinic.SelectedValue, d = _logDevice.SelectedValue;
        var clinicItems = new (string, object)[] { ("All clinics", null) }.Concat(clinics.Select(c => (c.ClinicName, (object)c.ClinicId))).ToList();
        _clinic.SetItems(clinicItems, c1);
        if (_clinic.SelectedValue == null) _clinic.Select(null);
        _logClinic.SetItems(clinicItems, c2);
        if (_logClinic.SelectedValue == null) _logClinic.Select(null);
        _logDevice.SetItems(new (string, object)[] { ("All devices", null) }
            .Concat(devices.Select(x => (_allClinics ? x.DeviceName + " · " + x.ClinicName : x.DeviceName, (object)x.DeviceId))), d);
        if (_logDevice.SelectedValue == null) _logDevice.Select(null);
        _quiet = false;
    }

    // Summary and chart ------------------------------------------------------------------------------------------------

    public void ShowSummary(SyncSummary s)
    {
        string N(Func<SyncSummary, int> f) => s == null ? "–" : f(s).ToString();
        var cards = new List<View>
        {
            DashboardKit.StatCard("monitor", "Cyan", "Navy", N(x => x.Devices), "Total devices", onTap: () => HealthCard(null)),
            DashboardKit.StatCard("cloud-check", "Mint", "Success", N(x => x.Healthy), "Healthy", onTap: () => HealthCard(SyncHealthHelper.Healthy)),
            DashboardKit.StatCard("clock", "WarningSoft", "Warning", N(x => x.Stale), "Stale (" + SyncHealthHelper.StaleHours + "h+)", onTap: () => HealthCard(SyncHealthHelper.Stale)),
            DashboardKit.StatCard("cloud-off", "DangerSoft", "Danger", N(x => x.Failing), "Failing", onTap: () => HealthCard(SyncHealthHelper.Failing)),
            DashboardKit.StatCard("triangle-alert", "OrangeSoft", "Orange", N(x => x.FailedToday), "Failed syncs today", onTap: () =>
            {
                Preset(logDay: DateTime.Today, logStatus: "Failed");
                OpenTab(LogTab);
            }),
            DashboardKit.StatCard("git-merge", "PaleBlue", "Navy", N(x => x.ConflictsToday), "Conflicts today")
        };
        _summary.Children.Clear();
        _summary.Children.Add(DashboardKit.StatGrid(cards, 6));
    }

    private void HealthCard(string health)
    {
        Preset(ClinicFilter, health);
        if (CurrentTab != DevicesTab) OpenTab(DevicesTab);
        else FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowChart(IReadOnlyList<(DateTime Day, int Success, int Failed)> days) =>
        _chart.Points = days.Select(d => new ChartPoint(d.Day.ToString("MMM d"), d.Success, d.Failed)).ToList();

    public void ShowUpdated(DateTime when) => _updated.Text = "Updated " + when.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public void ShowLoading() => _body.Content = ClinicsPage.Card(PageKit.StateMessage("Loading…"), new Thickness(0));

    public void ShowError(string message) => _body.Content = ClinicsPage.Card(PageKit.StateMessage(message, "circle-alert", "DangerSoft"), new Thickness(0));

    // Devices tab -------------------------------------------------------------------------------------------------------

    public void ShowDevices(IReadOnlyList<ClinicDevice> devices)
    {
        var columns = new List<FlexColumn> { new("Device", 190, true) };
        if (_allClinics) columns.Add(new FlexColumn("Clinic", 130, true));
        columns.AddRange(new FlexColumn[] { new("Registered", 96), new("Last sync", 96), new("Pending", 72), new("Health", 130), new("Active", 58), new("", 34) });
        var table = new FlexTable(columns.ToArray());

        if (devices.Count == 0)
        {
            bool filtered = !string.IsNullOrEmpty(SearchText) || ClinicFilter != null || HealthFilter != null;
            table.AddMessage(filtered ? PageKit.StateMessage("No devices match your filters.")
                : PageKit.EmptyState("monitor", "No devices yet", "Devices appear when a clinic admin signs in on a new computer."));
        }
        var now = DateTime.Now;
        foreach (var d in devices)
        {
            int col = 0;
            var row = table.NewRow(64);
            var tile = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                BackgroundColor = ThemeResources.Color("Cyan"),
                VerticalOptions = LayoutOptions.Center,
                Content = new LucideIcon { Icon = "monitor", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 18, HeightRequest = 18, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var name = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 12, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            name.Add(tile, 0, 0);
            name.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = d.DeviceName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 },
                    new Label { Text = d.MachineCode, FontFamily = "Consolas", FontSize = 12, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 }
                }
            }, 1, 0);
            row.Add(name, col++, 0);
            if (_allClinics) row.Add(PageKit.Cell(d.ClinicName), col++, 0);
            row.Add(PageKit.Cell(d.RegisteredAt.ToString("MMM d, yyyy")), col++, 0);
            row.Add(LastSyncCell(d.LastSyncAt, now), col++, 0);
            row.Add(d.PendingChanges > 0 ? Badges.Pill(d.PendingChanges.ToString(), "Warning", "WarningSoft") : PageKit.Cell("0", muted: true), col++, 0);
            row.Add(Badges.DeviceHealth(d.Health), col++, 0);
            var device = d;
            var toggle = new ToggleSwitch(null, d.IsActive) { AskFirst = true, VerticalOptions = LayoutOptions.Center };
            toggle.TapRequested += (s, e) => DeviceToggleRequested?.Invoke(this, device);
            row.Add(toggle, col++, 0);
            Border menu = null;
            menu = PageKit.IconButton("ellipsis-vertical", "More for " + d.DeviceName, () => DeviceMenuRequested?.Invoke(this, (menu, device)));
            row.Add(menu, col, 0);
            table.AddRow(row, () => DeviceSelected?.Invoke(this, device));
        }
        _body.Content = ClinicsPage.Card(table, new Thickness(0));
    }

    /// <summary>"2 min ago"; amber after the stale time, red after 3 days, "Never" in blue.</summary>
    public static View LastSyncCell(DateTime? when, DateTime now)
    {
        if (when == null)
        {
            var never = PageKit.Cell("Never");
            never.TextColor = ThemeResources.Color("Navy");
            never.FontFamily = "DMSansMedium";
            return never;
        }
        var label = PageKit.Cell(GreetingHelper.Relative(when.Value, now));
        var age = now - when.Value;
        if (age > TimeSpan.FromDays(3)) { label.TextColor = ThemeResources.Color("Danger"); label.FontFamily = "DMSansMedium"; }
        else if (age > TimeSpan.FromHours(SyncHealthHelper.StaleHours)) { label.TextColor = ThemeResources.Color("Warning"); label.FontFamily = "DMSansMedium"; }
        return label;
    }

    // Sync log tab --------------------------------------------------------------------------------------------------------

    public void ShowLogs(PagedResult<SyncLogEntry> page)
    {
        var table = new FlexTable(new("Date & time", 112), new("Device", 160, true), new("User", 100, true), new("Direction", 96),
            new("Records", 92), new("Conflicts", 76), new("Duration", 76), new("Status", 100));
        if (page.TotalCount == 0) table.AddMessage(PageKit.StateMessage("No syncs in this period."));
        foreach (var e in page.Items)
        {
            bool failed = e.Status == "Failed";
            var row = table.NewRow(failed && !string.IsNullOrEmpty(e.ErrorMessage) ? 74 : 60);
            if (failed) row.BackgroundColor = Color.FromArgb("#FFF6F6");
            row.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = e.StartedAt.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                    new Label { Text = e.StartedAt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                }
            }, 0, 0);
            var device = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 12, 0) };
            device.Children.Add(new Label { Text = e.DeviceName, FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
            if (_allClinics) device.Children.Add(new Label { Text = e.ClinicName, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
            if (failed && !string.IsNullOrEmpty(e.ErrorMessage))
            {
                var error = new Label { Text = e.ErrorMessage, FontSize = 12.5, TextColor = ThemeResources.Color("DangerHover"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
                ToolTipProperties.SetText(error, e.ErrorMessage);
                device.Children.Add(error);
            }
            row.Add(device, 1, 0);
            row.Add(PageKit.Cell(e.UserName ?? "—", muted: e.UserName == null), 2, 0);
            row.Add(Badges.SyncDirection(e.Direction), 3, 0);
            row.Add(PageKit.Cell("↑ " + e.RecordsSent + " / ↓ " + e.RecordsReceived), 4, 0);
            var conflicts = PageKit.Cell(e.ConflictsFound.ToString());
            if (e.ConflictsFound > 0) { conflicts.TextColor = ThemeResources.Color("Orange"); conflicts.FontFamily = "DMSansBold"; }
            row.Add(conflicts, 5, 0);
            row.Add(PageKit.Cell(e.DurationSeconds == null ? "—" : e.DurationSeconds.Value.ToString("0.0") + " s", muted: e.DurationSeconds == null), 6, 0);
            row.Add(Badges.SyncResult(e.Status), 7, 0);
            table.AddRow(row);
        }

        var pager = new Grid { Padding = new Thickness(20, 12), ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        int pages = Math.Max(1, (page.TotalCount + page.PageSize - 1) / page.PageSize);
        pager.Add(new Label
        {
            Text = page.TotalCount == 0 ? "Logs older than " + SettingsCache.GetInt(SettingKeys.SyncLogDays) + " days are removed automatically."
                : $"Showing {page.From}–{page.To} of {page.TotalCount:N0} logs · Logs older than {SettingsCache.GetInt(SettingKeys.SyncLogDays)} days are removed automatically.",
            FontSize = 13.5,
            TextColor = ThemeResources.Color("BodyText"),
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);
        if (pages > 1)
        {
            var prev = PageKit.IconButton("chevron-left", "Previous page", () => { if (page.Page > 1) PageRequested?.Invoke(this, page.Page - 1); });
            var next = PageKit.IconButton("chevron-right", "Next page", () => { if (page.Page < pages) PageRequested?.Invoke(this, page.Page + 1); });
            prev.Opacity = page.Page > 1 ? 1 : 0.4;
            next.Opacity = page.Page < pages ? 1 : 0.4;
            pager.Add(prev, 1, 0);
            pager.Add(next, 2, 0);
        }
        _body.Content = ClinicsPage.Card(new VerticalStackLayout { Children = { table, pager } }, new Thickness(0));
    }

    // Clinic sync health tab ------------------------------------------------------------------------------------------------

    public void ShowClinicHealth(IReadOnlyList<ClinicSyncRow> rows)
    {
        var table = new FlexTable(new("Clinic", 190, true), new("Devices", 70), new("Healthy", 70), new("Stale", 60), new("Failing", 64),
            new("Failed (7d)", 86), new("Conflicts (7d)", 104), new("Last sync", 92), new("Overall", 128));
        if (rows.Count == 0) table.AddMessage(PageKit.EmptyState("monitor", "No devices yet", "Clinics appear here once their computers are registered."));
        var now = DateTime.Now;
        foreach (var r in rows)
        {
            var row = table.NewRow(64);
            var clinic = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 12, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            clinic.Add(ClinicsPage.Tile(new Clinic { ClinicName = r.ClinicName }.Initials, 34, 12), 0, 0);
            clinic.Add(new VerticalStackLayout
            {
                Spacing = 3,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = r.ClinicName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 },
                    Badges.SmallClinicStatus(r.ClinicStatus)
                }
            }, 1, 0);
            row.Add(clinic, 0, 0);
            row.Add(PageKit.Cell(r.Devices.ToString()), 1, 0);
            row.Add(PageKit.Cell(r.Healthy.ToString()), 2, 0);
            row.Add(PageKit.Cell(r.Stale.ToString()), 3, 0);
            row.Add(PageKit.Cell(r.Failing.ToString()), 4, 0);
            row.Add(PageKit.Cell(r.FailedWeek.ToString()), 5, 0);
            row.Add(PageKit.Cell(r.ConflictsWeek.ToString()), 6, 0);
            row.Add(LastSyncCell(r.LastSync, now), 7, 0);
            var (bg, fg) = r.Overall switch
            {
                SyncHealthHelper.Problem => ("DangerSoft", "DangerHover"),
                SyncHealthHelper.NeedsAttention => ("WarningSoft", "Warning"),
                SyncHealthHelper.AllGood => ("Mint", "Success"),
                _ => ("NeutralSoft", "BodyText")
            };
            row.Add(Badges.Pill(r.Overall, fg, bg), 8, 0);
            int id = r.ClinicId;
            table.AddRow(row, () => ClinicSelected?.Invoke(this, id), lastColumnIsActions: false);
        }
        _body.Content = ClinicsPage.Card(table, new Thickness(0));
    }

    // Building blocks --------------------------------------------------------------------------------------------------------

    private void ShowToolbar()
    {
        _toolbar.Content = CurrentTab switch { DevicesTab => _deviceFilters, LogTab => _logFilters, _ => null };
        _toolbar.IsVisible = _toolbar.Content != null;
    }

    private PickerField Picker(string field, string label, double width, params (string Text, object Value)[] items)
    {
        var picker = new PickerField(field, label) { WidthRequest = width, Margin = new Thickness(0, 0, 12, 8) };
        picker.SetItems(items, null);
        picker.Select(null);
        picker.SelectionChanged += (s, e) => { if (!_quiet) FiltersChanged?.Invoke(this, EventArgs.Empty); };
        return picker;
    }

    private async Task DelayedAsync()
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        try { await Task.Delay(300, cts.Token); }
        catch (TaskCanceledException) { return; }
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }
}
