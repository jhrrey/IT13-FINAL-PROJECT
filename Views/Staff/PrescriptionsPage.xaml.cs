using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>
/// The Prescriptions page: "To dispense" (grouped per visit, with "Dispense all") and "Dispensed".
/// Displays what PharmacyController gives it and raises events; the rules live in the controller.
/// </summary>
public partial class PrescriptionsPage : ContentView
{
    // Pet, owner, vet and date are in each visit's group header, so the rows fit the normal window.
    private static readonly TableColumn[] DispenseColumns =
    {
        new("Medicine", 200), new("Qty", 85), new("Dosage", 120), new("Instructions", 190), new("Stock", 120), new("", 145)
    };

    private static readonly TableColumn[] DispensedColumns =
    {
        new("Dispensed", 115), new("Pet", 140), new("Owner", 135), new("Medicine", 170), new("Qty", 80), new("Dispensed by", 125), new("", 95)
    };

    private readonly List<(Label Label, BoxView Line, Border Count)> _tabs = new();
    private IReadOnlyList<User> _vets = Array.Empty<User>();
    private IDispatcherTimer _searchTimer;
    private PagedResult<PharmacyPrescription> _page;
    private bool _canDispense;
    private bool _silent;
    private double _width;

    public PrescriptionsPage()
    {
        InitializeComponent();

        AddTab("To dispense", 0, withCount: true);
        AddTab("Dispensed", 1, withCount: false);
        SelectTab(0, raise: false);

        _silent = true;
        Fill(VetPicker, "All vets", Array.Empty<string>());
        _silent = false;
        VetPicker.SelectedIndexChanged += (s, e) => Raise();
        FromDate.DateChanged += (s, e) => Raise();
        ToDate.DateChanged += (s, e) => Raise();
        ClearDatesButton.Clicked += (s, e) => { _silent = true; FromDate.Date = null; ToDate.Date = null; _silent = false; Raise(); };
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        ScrollFocus.DontScrollOnFocus(PageScroll, TableScroll);
        SizeChanged += (s, e) =>
        {
            if (Width <= 0 || Math.Abs(Width - _width) < 1) return;
            bool wasWide = _width <= 0 || _width >= 980;
            _width = Width;
            if ((_width >= 980) != wasWide) PageKit.Reflow(SummaryGrid, SummaryColumns());
        };
    }

    public event EventHandler FiltersChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<int> TabChanged;
    public event EventHandler<IReadOnlyList<PharmacyPrescription>> DispenseRequested;
    public event EventHandler<PharmacyPrescription> ReprintRequested;
    public event EventHandler<int> RecordRequested;

    public int CurrentTab { get; private set; }
    public string CurrentSearch => SearchEntry.Text?.Trim();
    public DateTime? CurrentFrom => FromDate.Date;
    public DateTime? CurrentTo => ToDate.Date;
    public int? CurrentVetId => VetPicker.SelectedIndex > 0 && VetPicker.SelectedIndex - 1 < _vets.Count ? _vets[VetPicker.SelectedIndex - 1].UserId : null;

    /// <summary>Rule 1: only Cashier/Staff dispense.</summary>
    public void SetCanDispense(bool canDispense) => _canDispense = canDispense;

    /// <summary>Opening the record from a group header (when the user may see medical records).</summary>
    public bool CanOpenRecord { get; set; }

    public void SetVets(IReadOnlyList<User> vets)
    {
        _silent = true;
        _vets = vets;
        Fill(VetPicker, "All vets", vets.Select(v => "Dr. " + v.FullName));
        _silent = false;
    }

    public void ShowSummary(PrescriptionSummary s)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("clipboard-list", "WarningBox", s.ToDispense.ToString("N0"), "To dispense", "ConsultText"),
            PageKit.SummaryCard("package-check", "Mint", s.DispensedToday.ToString("N0"), "Dispensed today", "CompletedText"),
            PageKit.SummaryCard("package-x", "DangerSoft", s.WaitingForStock.ToString("N0"), "Waiting for stock", "Danger")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());

        var count = _tabs[0].Count;
        ((Label)count.Content).Text = s.ToDispense.ToString();
        count.IsVisible = s.ToDispense > 0;
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading prescriptions…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    // To dispense -----------------------------------------------------------------------------------------

    public void ShowToDispense(IReadOnlyList<PharmacyPrescription> items)
    {
        TableStack.Children.Clear();
        PagerRow.IsVisible = false;
        if (items.Count == 0)
        {
            bool filtered = !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentVetId != null || CurrentFrom != null || CurrentTo != null;
            if (filtered) { ShowState(PageKit.StateMessage("No prescriptions match your filters.")); return; }
            var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
            empty.Children.Add(new IconBadge { Icon = "package-check", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText"), HorizontalOptions = LayoutOptions.Center });
            empty.Children.Add(new Label { Text = "All prescriptions dispensed", FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) });
            empty.Children.Add(new Label { Text = "New prescriptions appear here after a consultation.", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.Center });
            ShowState(empty);
            return;
        }

        TableState.IsVisible = false;
        TableScroll.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(DispenseColumns));
        foreach (var visit in items.GroupBy(p => p.AppointmentId))
        {
            var rows = visit.ToList();
            TableStack.Children.Add(GroupHeader(rows));
            foreach (var p in rows) TableStack.Children.Add(DispenseRow(p));
        }
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    /// <summary>"Luna · Pedro Reyes · Dr. Grace Lim · Nov 16, 2026" with "Dispense all" on the right.</summary>
    private View GroupHeader(IReadOnlyList<PharmacyPrescription> rows)
    {
        var first = rows[0];
        double width = DispenseColumns.Sum(c => c.Width);
        var grid = new Grid
        {
            WidthRequest = width + 40,
            HeightRequest = 52,
            Padding = new Thickness(20, 0),
            BackgroundColor = ThemeResources.Color("LightGray"),
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        var title = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                PetsPage.SpeciesAvatar(first.Species, 30),
                new Label
                {
                    FormattedText = new FormattedString
                    {
                        Spans =
                        {
                            new Span { Text = first.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                            new Span { Text = "  ·  " + first.OwnerName + "  ·  " + first.VetDisplay + "  ·  " + first.RecordDate.ToString("MMM dd, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
                        }
                    },
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
        if (CanOpenRecord)
        {
            var link = new TextLink { Text = "View record", FontSize = 13, VerticalOptions = LayoutOptions.Center };
            link.Clicked += (s, e) => RecordRequested?.Invoke(this, first.RecordId);
            title.Children.Add(link);
        }
        grid.Add(title, 0, 0);

        var ready = rows.Where(p => p.CanDispense).ToList();
        if (_canDispense && rows.Count > 1 && ready.Count > 0)
            grid.Add(PharmacyKit.SmallButton(ready.Count == rows.Count ? "Dispense all" : "Dispense " + ready.Count + " ready", "package-check", false,
                () => DispenseRequested?.Invoke(this, ready), "Dispense all for " + first.PetName), 1, 0);
        return PageKit.WithBottomLine(grid);
    }

    private View DispenseRow(PharmacyPrescription p)
    {
        var grid = PageKit.RowGrid(DispenseColumns, 64);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                PharmacyKit.MedicineTile(false, 34),
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = p.MedicineName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150, HorizontalOptions = LayoutOptions.Start },
                        new Label { Text = p.AsMedicine().PerUnitText, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                    }
                }
            }
        }, 0, 0);
        grid.Add(PageKit.Cell(p.QuantityText), 1, 0);
        grid.Add(PageKit.Cell(p.Dosage ?? "—"), 2, 0);
        var instructions = PageKit.Cell(p.Instructions ?? "—", muted: true);
        if (!string.IsNullOrEmpty(p.Instructions)) ToolTipProperties.SetText(instructions, p.Instructions);
        grid.Add(instructions, 3, 0);
        grid.Add(new Label
        {
            Text = p.IsExpired ? "Expired" : p.StockQty.ToString("N0") + " available",
            FontFamily = "DMSansBold",
            FontSize = 13.5,
            TextColor = ThemeResources.Color(p.CanDispense ? "Success" : "Danger"),
            VerticalOptions = LayoutOptions.Center
        }, 4, 0);

        View action;
        if (!_canDispense) action = Badges.Pill("Waiting", "ConsultText", "WarningBox");
        else if (p.CanDispense) action = PharmacyKit.SmallButton("Dispense", "package-check", true, () => DispenseRequested?.Invoke(this, new[] { p }), "Dispense " + p.MedicineName);
        else
            action = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    PharmacyKit.DisabledButton("Dispense", "package-check"),
                    new Label { Text = p.IsExpired ? "Medicine expired" : "Not enough stock", FontSize = 12, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color("Danger") }
                }
            };
        grid.Add(action, 5, 0);
        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    // Dispensed --------------------------------------------------------------------------------------

    public void ShowDispensed(PagedResult<PharmacyPrescription> page)
    {
        _page = page;
        TableStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            bool filtered = !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentVetId != null || CurrentFrom != null || CurrentTo != null;
            ShowState(filtered ? PageKit.StateMessage("No prescriptions match your filters.")
                               : PageKit.EmptyState("package-check", "Nothing dispensed yet", "Dispensed prescriptions appear here."));
            return;
        }

        TableState.IsVisible = false;
        TableScroll.IsVisible = true;
        PagerRow.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(DispensedColumns));
        foreach (var p in page.Items) TableStack.Children.Add(DispensedRow(p));
        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "prescription" : "prescriptions")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    private View DispensedRow(PharmacyPrescription p)
    {
        var grid = PageKit.RowGrid(DispensedColumns, 64);
        var date = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = p.DispensedAt?.ToString("MMM dd, yyyy") ?? "—", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = p.DispensedAt?.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
            }
        };
        if (p.SyncStatus != SyncStatuses.Synced) date.Children.Add(Badges.Sync(p.SyncStatus));
        grid.Add(date, 0, 0);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                PetsPage.SpeciesAvatar(p.Species, 34),
                new Label { Text = p.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 90 }
            }
        }, 1, 0);
        grid.Add(PageKit.Cell(p.OwnerName), 2, 0);
        grid.Add(PageKit.Cell(p.MedicineName), 3, 0);
        grid.Add(PageKit.Cell(p.QuantityText), 4, 0);
        grid.Add(PageKit.Cell(p.DispensedByName ?? "—"), 5, 0);
        if (_canDispense)
        {
            var link = new TextLink { Text = "Reprint label", FontSize = 13.5, VerticalOptions = LayoutOptions.Center };
            link.Clicked += (s, e) => ReprintRequested?.Invoke(this, p);
            grid.Add(link, 6, 0);
        }
        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    // Building blocks -----------------------------------------------------------------------------------

    private void AddTab(string text, int index, bool withCount)
    {
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var count = new Border
        {
            IsVisible = false,
            Padding = new Thickness(7, 1),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color("Orange"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label { FontFamily = "DMSansBold", FontSize = 11.5, TextColor = Colors.White }
        };
        var line = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End };
        var content = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { label } };
        if (withCount) content.Children.Add(count);
        var tab = new Grid { HeightRequest = 40, Children = { content, line } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectTab(index, raise: true);
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);
        _tabs.Add((label, line, count));
        TabsRow.Children.Add(tab);
    }

    private void SelectTab(int index, bool raise)
    {
        bool changed = CurrentTab != index;
        CurrentTab = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == index;
            _tabs[i].Label.FontFamily = active ? "DMSansBold" : "DMSansMedium";
            _tabs[i].Label.TextColor = ThemeResources.Color(active ? "Orange" : "BodyText");
            _tabs[i].Line.Color = active ? ThemeResources.Color("Orange") : Colors.Transparent;
        }
        if (raise && changed) TabChanged?.Invoke(this, index);
    }

    private void ShowState(View content)
    {
        TableStack.Children.Clear();
        TableScroll.IsVisible = false;
        PagerRow.IsVisible = false;
        TableState.Content = content;
        TableState.IsVisible = true;
    }

    private void Raise() { if (!_silent) FiltersChanged?.Invoke(this, EventArgs.Empty); }

    private static void Fill(Picker picker, string all, IEnumerable<string> items)
    {
        picker.Items.Clear();
        picker.Items.Add(all);
        foreach (string item in items) picker.Items.Add(item);
        picker.SelectedIndex = 0;
    }

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 3 : 2;

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => Raise();
        return timer;
    }
}
