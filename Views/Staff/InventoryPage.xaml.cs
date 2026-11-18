using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>
/// The Medicines &amp; stock page: summary cards, the medicines table and the stock history.
/// Displays what PharmacyController gives it and raises events; the rules live in the controller.
/// </summary>
public partial class InventoryPage : ContentView
{
    private static readonly (StockStatus Status, string Text)[] Statuses =
    {
        (StockStatus.InStock, "In stock"), (StockStatus.LowStock, "Low stock"), (StockStatus.OutOfStock, "Out of stock"),
        (StockStatus.ExpiringSoon, "Expiring soon"), (StockStatus.Expired, "Expired")
    };

    // The sync badge shows under the status only when the row isn't synced, so the table fits the normal window.
    private static readonly TableColumn[] HistoryColumns =
    {
        new("Date & time", 120), new("Medicine", 175), new("Type", 120), new("Qty", 80), new("Done by", 135), new("Remarks", 230)
    };

    private readonly List<(Label Label, BoxView Line)> _tabs = new();
    private IReadOnlyList<Medicine> _options = Array.Empty<Medicine>();
    private TableColumn[] _columns;
    private IDispatcherTimer _searchTimer;
    private PagedResult<Medicine> _page;
    private PagedResult<StockLogEntry> _historyPage;
    private bool _canManage;
    private bool _silent;
    private double _width;

    public InventoryPage()
    {
        InitializeComponent();
        SetCanManage(false);

        AddTab("Medicines", 0);
        AddTab("Stock history", 1);
        SelectTab(0, raise: false);

        _silent = true;
        Fill(CategoryPicker, "All categories", Medicine.Categories);
        Fill(StatusPicker, "All status", Statuses.Select(s => s.Text));
        Fill(TypePicker, "All types", StockStatusHelper.TransactionTypes);
        Fill(HistoryMedicinePicker, "All medicines", Array.Empty<string>());
        _silent = false;

        CategoryPicker.SelectedIndexChanged += (s, e) => Raise();
        StatusPicker.SelectedIndexChanged += (s, e) => Raise();
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        HistoryMedicinePicker.SelectedIndexChanged += (s, e) => RaiseHistory();
        TypePicker.SelectedIndexChanged += (s, e) => RaiseHistory();
        FromDate.DateChanged += (s, e) => RaiseHistory();
        ToDate.DateChanged += (s, e) => RaiseHistory();
        ClearDatesButton.Clicked += (s, e) => { _silent = true; FromDate.Date = null; ToDate.Date = null; _silent = false; RaiseHistory(); };
        HistoryPrevButton.Clicked += (s, e) => { if (_historyPage?.HasPrevious == true) HistoryPageRequested?.Invoke(this, _historyPage.Page - 1); };
        HistoryNextButton.Clicked += (s, e) => { if (_historyPage?.HasNext == true) HistoryPageRequested?.Invoke(this, _historyPage.Page + 1); };

        AddButton.Clicked += (s, e) => AddRequested?.Invoke(this, EventArgs.Empty);

        ScrollFocus.DontScrollOnFocus(PageScroll, MedicinesScroll, HistoryScroll);
        SizeChanged += (s, e) =>
        {
            if (Width <= 0 || Math.Abs(Width - _width) < 1) return;
            bool wasWide = _width <= 0 || _width >= 980;
            _width = Width;
            if ((_width >= 980) != wasWide) PageKit.Reflow(SummaryGrid, SummaryColumns());
        };
    }

    public event EventHandler FiltersChanged;
    public event EventHandler HistoryFiltersChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<int> HistoryPageRequested;
    public event EventHandler<int> TabChanged;
    public event EventHandler AddRequested;
    public event EventHandler<Medicine> StockInRequested;
    public event EventHandler<Medicine> DetailsRequested;
    public event EventHandler<(View Anchor, Medicine Medicine)> MenuRequested;

    public int CurrentTab { get; private set; }
    public string CurrentSearch => SearchEntry.Text?.Trim();
    public string CurrentCategory => CategoryPicker.SelectedIndex > 0 ? Medicine.Categories[CategoryPicker.SelectedIndex - 1] : null;
    public StockStatus? CurrentStatus => StatusPicker.SelectedIndex > 0 ? Statuses[StatusPicker.SelectedIndex - 1].Status : null;
    public int? HistoryMedicineId => HistoryMedicinePicker.SelectedIndex > 0 && HistoryMedicinePicker.SelectedIndex - 1 < _options.Count ? _options[HistoryMedicinePicker.SelectedIndex - 1].MedicineId : null;
    public string HistoryType => TypePicker.SelectedIndex > 0 ? StockStatusHelper.TransactionTypes[TypePicker.SelectedIndex - 1] : null;
    public DateTime? HistoryFrom => FromDate.Date;
    public DateTime? HistoryTo => ToDate.Date;

    /// <summary>Rule 1: only Cashier/Staff add medicines and change stock.</summary>
    public void SetCanManage(bool canManage)
    {
        _canManage = canManage;
        AddButton.IsVisible = canManage;
        _columns = new TableColumn[]
        {
            new("Medicine", canManage ? 180 : 250), new("Category", 105), new("Price", 75), new("Stock", 90), new("Reorder", 72),
            new("Expiry", 100), new("Status", 105), new("", canManage ? 135 : 65)
        };
    }

    /// <summary>The "All medicines" dropdown of the history tab.</summary>
    public void SetMedicineOptions(IReadOnlyList<Medicine> medicines)
    {
        int? selected = HistoryMedicineId;
        _silent = true;
        _options = medicines;
        Fill(HistoryMedicinePicker, "All medicines", medicines.Select(m => m.MedicineName + " · " + m.Unit));
        if (selected != null) SelectHistoryMedicine(selected.Value);
        _silent = false;
    }

    /// <summary>"View history": the history tab filtered to one medicine.</summary>
    public void ShowHistoryOf(int medicineId)
    {
        _silent = true;
        SelectHistoryMedicine(medicineId);
        TypePicker.SelectedIndex = 0;
        FromDate.Date = null;
        ToDate.Date = null;
        _silent = false;
        if (CurrentTab != 1) SelectTab(1, raise: true);
        else HistoryFiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A summary card was clicked: filter the table by that status (or clear it).</summary>
    public void ApplyStatus(StockStatus? status)
    {
        _silent = true;
        StatusPicker.SelectedIndex = status == null ? 0 : Array.FindIndex(Statuses, s => s.Status == status) + 1;
        _silent = false;
        if (CurrentTab != 0) SelectTab(0, raise: true);
        else FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowSummary(StockSummary s)
    {
        var cards = new[]
        {
            Clickable(PageKit.SummaryCard("package", "Cyan", s.TotalItems.ToString("N0"), "Total items"), null, "Show all medicines"),
            Clickable(PageKit.SummaryCard("trending-down", "WarningBox", s.LowStock.ToString("N0"), "Low stock", "ConsultText"), StockStatus.LowStock, "Show low-stock medicines"),
            Clickable(PageKit.SummaryCard("package-x", "DangerSoft", s.OutOfStock.ToString("N0"), "Out of stock", "Danger"), StockStatus.OutOfStock, "Show out-of-stock medicines"),
            Clickable(PageKit.SummaryCard("calendar-clock", "OrangeSoft", s.ExpiringSoon.ToString("N0"), "Expiring in " + StockStatusHelper.ExpiringDays + " days", "Orange"), StockStatus.ExpiringSoon, "Show medicines expiring soon")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());
    }

    public void ShowLoading() => ShowState(MedicinesStack, MedicinesScroll, MedicinesState, PagerRow, PageKit.StateMessage("Loading medicines…"));

    public void ShowError(string message)
    {
        ShowState(MedicinesStack, MedicinesScroll, MedicinesState, PagerRow, PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
        ShowState(HistoryStack, HistoryScroll, HistoryState, HistoryPagerRow, PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
    }

    // Medicines ----------------------------------------------------------------------------------------

    public void ShowMedicines(PagedResult<Medicine> page)
    {
        _page = page;
        MedicinesStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            bool filtered = !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentCategory != null || CurrentStatus != null;
            ShowState(MedicinesStack, MedicinesScroll, MedicinesState, PagerRow, filtered
                ? PageKit.StateMessage("No medicines match your filters.")
                : PageKit.EmptyState("package", "No medicines yet", null, _canManage ? "Add medicine" : null, () => AddRequested?.Invoke(this, EventArgs.Empty)));
            return;
        }

        MedicinesState.IsVisible = false;
        MedicinesScroll.IsVisible = true;
        PagerRow.IsVisible = true;
        MedicinesStack.Children.Add(PageKit.HeaderRow(_columns));
        var today = DateTime.Today;
        foreach (var m in page.Items) MedicinesStack.Children.Add(MedicineRow(m, today));
        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "medicine" : "medicines")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await MedicinesScroll.ScrollToAsync(0, 0, false));
    }

    private View MedicineRow(Medicine m, DateTime today)
    {
        var grid = PageKit.RowGrid(_columns, 68);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                PharmacyKit.MedicineTile(m.IsVaccine),
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = m.MedicineName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = _columns[0].Width - 54, HorizontalOptions = LayoutOptions.Start },
                        new Label { Text = m.PerUnitText, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                    }
                }
            }
        }, 0, 0);
        grid.Add(Badges.Pill(m.Category, "BodyText", "LightGray"), 1, 0);
        grid.Add(PageKit.Cell(PharmacyKit.Peso(m.UnitPrice)), 2, 0);
        grid.Add(PharmacyKit.StockCell(m), 3, 0);
        grid.Add(PageKit.Cell(m.ReorderLevel.ToString("N0")), 4, 0);
        grid.Add(PharmacyKit.ExpiryLabel(m), 5, 0);

        var status = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { PharmacyKit.StatusPill(StockStatusHelper.StatusOf(m, today)) } };
        if (m.SyncStatus != SyncStatuses.Synced)
        {
            var sync = Badges.Sync(m.SyncStatus);
            if (m.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Stock count needs checking");
            status.Children.Add(sync);
        }
        grid.Add(status, 6, 0);

        var actions = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (_canManage) actions.Children.Add(PharmacyKit.SmallButton("Stock in", "plus", false, () => StockInRequested?.Invoke(this, m), "Stock in " + m.MedicineName));
        Border menu = null;
        menu = PageKit.IconButton("ellipsis-vertical", "More for " + m.MedicineName, () => MenuRequested?.Invoke(this, (menu, m)));
        actions.Children.Add(menu);
        grid.Add(actions, 7, 0);

        AddRowTap(grid, _columns, () => DetailsRequested?.Invoke(this, m));
        return PageKit.WithBottomLine(grid);
    }

    // Stock history ------------------------------------------------------------------------------------

    public void ShowHistory(PagedResult<StockLogEntry> page)
    {
        _historyPage = page;
        HistoryStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            bool filtered = HistoryMedicineId != null || HistoryType != null || HistoryFrom != null || HistoryTo != null;
            ShowState(HistoryStack, HistoryScroll, HistoryState, HistoryPagerRow, filtered
                ? PageKit.StateMessage("No stock changes match your filters.")
                : PageKit.EmptyState("package", "No stock changes yet", "Stock in, dispensing and adjustments appear here."));
            return;
        }

        HistoryState.IsVisible = false;
        HistoryScroll.IsVisible = true;
        HistoryPagerRow.IsVisible = true;
        HistoryStack.Children.Add(PageKit.HeaderRow(HistoryColumns));
        foreach (var e in page.Items) HistoryStack.Children.Add(HistoryRow(e));
        HistoryPagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "change" : "changes")}";
        HistoryPrevButton.IsEnabled = page.HasPrevious;
        HistoryNextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await HistoryScroll.ScrollToAsync(0, 0, false));
    }

    private static View HistoryRow(StockLogEntry e)
    {
        var grid = PageKit.RowGrid(HistoryColumns, 60);
        var date = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = e.Date.ToString("MMM dd, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = e.Date.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
            }
        };
        if (e.SyncStatus != SyncStatuses.Synced) date.Children.Add(Badges.Sync(e.SyncStatus));
        grid.Add(date, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = e.MedicineName, FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 162, HorizontalOptions = LayoutOptions.Start },
                new Label { Text = e.Unit, FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
            }
        }, 1, 0);
        grid.Add(PharmacyKit.TransactionPill(e.TransactionType), 2, 0);
        grid.Add(PharmacyKit.SignedQuantity(e.Quantity, e.QuantityText), 3, 0);
        grid.Add(PageKit.Cell(e.DoneBy), 4, 0);
        var remarks = PageKit.Cell(string.IsNullOrEmpty(e.Remarks) ? "—" : e.Remarks, muted: true);
        if (!string.IsNullOrEmpty(e.Remarks)) ToolTipProperties.SetText(remarks, e.Remarks);
        grid.Add(remarks, 5, 0);
        return PageKit.WithBottomLine(grid);
    }

    // Building blocks -------------------------------------------------------------------------------

    private static void AddRowTap(Grid grid, TableColumn[] columns, Action open)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (e.GetPosition(grid) is Point p && p.X > grid.Width - columns[^1].Width - 20) return; // actions column
            open();
        };
        grid.GestureRecognizers.Add(tap);
        HandCursor.Apply(grid);
        PageKit.AddRowHover(grid);
    }

    private View Clickable(View card, StockStatus? status, string tooltip)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => ApplyStatus(status);
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        ToolTipProperties.SetText(card, tooltip);
        return card;
    }

    private void AddTab(string text, int index)
    {
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var line = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End };
        var tab = new Grid { HeightRequest = 40, Children = { label, line } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectTab(index, raise: true);
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);
        _tabs.Add((label, line));
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
        MedicinesSection.IsVisible = index == 0;
        HistorySection.IsVisible = index == 1;
        if (raise && changed) TabChanged?.Invoke(this, index);
    }

    private void SelectHistoryMedicine(int medicineId)
    {
        int i = _options.ToList().FindIndex(m => m.MedicineId == medicineId);
        HistoryMedicinePicker.SelectedIndex = i >= 0 ? i + 1 : 0;
    }

    private static void ShowState(VerticalStackLayout stack, ScrollView scroll, ContentView state, View pager, View content)
    {
        stack.Children.Clear();
        scroll.IsVisible = false;
        pager.IsVisible = false;
        state.Content = content;
        state.IsVisible = true;
    }

    private void Raise() { if (!_silent) FiltersChanged?.Invoke(this, EventArgs.Empty); }

    private void RaiseHistory() { if (!_silent) HistoryFiltersChanged?.Invoke(this, EventArgs.Empty); }

    private static void Fill(Picker picker, string all, IEnumerable<string> items)
    {
        picker.Items.Clear();
        picker.Items.Add(all);
        foreach (string item in items) picker.Items.Add(item);
        picker.SelectedIndex = 0;
    }

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 4 : 2;

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => Raise();
        return timer;
    }
}
