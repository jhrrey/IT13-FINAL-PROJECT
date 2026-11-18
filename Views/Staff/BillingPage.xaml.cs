using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>
/// The Bills &amp; payments page: summary cards and the Ready to bill / Bills / Payments tabs.
/// Displays what BillingController gives it and raises events; the rules live in the controller.
/// </summary>
public partial class BillingPage : ContentView
{
    public const int ReadyTab = 0, BillsTab = 1, PaymentsTab = 2;

    private static readonly TableColumn[] ReadyColumns =
    {
        new("Visit date", 110), new("Pet", 150), new("Owner", 140), new("Vet", 150), new("Reason · items", 190), new("", 120)
    };

    // Total and paid share a column so the actions stay visible at the normal window size.
    private static readonly TableColumn[] PaymentColumns =
    {
        new("Date & time", 120), new("Bill no.", 120), new("Owner", 150), new("Method", 125), new("Reference no.", 115), new("Amount", 105), new("Received by", 125)
    };

    private readonly List<(Label Label, BoxView Line, Border Count)> _tabs = new();
    private TableColumn[] _billColumns;
    private IDispatcherTimer _searchTimer;
    private int _page = 1, _pageCount = 1;
    private bool _canManage;
    private bool _silent;
    private double _width;

    public BillingPage()
    {
        InitializeComponent();
        SetCanManage(false);

        AddTab("Ready to bill", ReadyTab, withCount: true);
        AddTab("Bills", BillsTab, withCount: false);
        AddTab("Payments", PaymentsTab, withCount: false);

        _silent = true;
        Fill(StatusPicker, "All status", BillStatuses.All);
        Fill(MethodPicker, "All methods", PaymentMethods.All);
        _silent = false;
        SelectTab(ReadyTab, raise: false);

        StatusPicker.SelectedIndexChanged += (s, e) => Raise();
        MethodPicker.SelectedIndexChanged += (s, e) => Raise();
        FromDate.DateChanged += (s, e) => Raise();
        ToDate.DateChanged += (s, e) => Raise();
        ClearDatesButton.Clicked += (s, e) => { _silent = true; FromDate.Date = null; ToDate.Date = null; _silent = false; Raise(); };
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        PrevButton.Clicked += (s, e) => { if (_page > 1) PageRequested?.Invoke(this, _page - 1); };
        NextButton.Clicked += (s, e) => { if (_page < _pageCount) PageRequested?.Invoke(this, _page + 1); };

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
    public event EventHandler<ReadyToBillVisit> CreateBillRequested;
    public event EventHandler<Bill> BillSelected;
    public event EventHandler<Bill> PaymentRequested;
    public event EventHandler<(View Anchor, Bill Bill)> MenuRequested;
    public event EventHandler<int> PaymentBillSelected;

    public int CurrentTab { get; private set; }
    public string CurrentSearch => SearchEntry.Text?.Trim();
    public string CurrentStatus => StatusPicker.SelectedIndex > 0 ? BillStatuses.All[StatusPicker.SelectedIndex - 1] : null;
    public string CurrentMethod => MethodPicker.SelectedIndex > 0 ? PaymentMethods.All[MethodPicker.SelectedIndex - 1] : null;
    public DateTime? CurrentFrom => FromDate.Date;
    public DateTime? CurrentTo => ToDate.Date;

    /// <summary>Rule 1: only Cashier/Staff create bills and take payments.</summary>
    public void SetCanManage(bool canManage)
    {
        _canManage = canManage;
        _billColumns = new TableColumn[]
        {
            new("Bill no.", 112), new("Date", 102), new("Owner · pet", canManage ? 146 : 196), new("Total · paid", 108), new("Balance", 90),
            new("Status", 92), new("", canManage ? 210 : 60)
        };
    }

    public void ShowSummary(BillingSummary s)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("wallet", "Mint", CurrencyHelper.Peso(s.CollectedToday), "Collected today", "CompletedText"),
            PageKit.SummaryCard("receipt", "OrangeSoft", s.Unpaid.ToString("N0"), "Unpaid bills", "Orange"),
            PageKit.SummaryCard("hourglass", "WarningBox", s.Partial.ToString("N0"), "Partially paid", "ConsultText"),
            PageKit.SummaryCard("circle-dollar-sign", "PaleBlue", CurrencyHelper.Peso(s.Outstanding), "Outstanding balance")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());
        var count = _tabs[ReadyTab].Count;
        ((Label)count.Content).Text = s.ReadyToBill.ToString();
        count.IsVisible = s.ReadyToBill > 0;
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading bills…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    /// <summary>Opens a tab (e.g. Bills after a bill was created).</summary>
    public void OpenTab(int tab) => SelectTab(tab, raise: true);

    // Ready to bill --------------------------------------------------------------------------------------------

    public void ShowReadyToBill(IReadOnlyList<ReadyToBillVisit> visits)
    {
        TableStack.Children.Clear();
        PagerRow.IsVisible = false;
        TotalBar.IsVisible = false;
        if (visits.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(CurrentSearch)) { ShowState(PageKit.StateMessage("No visits match your search.")); return; }
            var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
            empty.Children.Add(new IconBadge { Icon = "receipt", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText"), HorizontalOptions = LayoutOptions.Center });
            empty.Children.Add(new Label { Text = "Nothing to bill", FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) });
            empty.Children.Add(new Label { Text = "Completed visits will appear here.", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.Center });
            ShowState(empty);
            return;
        }

        TableState.IsVisible = false;
        TableScroll.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(ReadyColumns));
        foreach (var v in visits)
        {
            var grid = PageKit.RowGrid(ReadyColumns, 66);
            grid.Add(TwoLines(v.VisitDate.ToString("MMM d, yyyy"), TimeSlotHelper.Format(v.VisitTime)), 0, 0);
            grid.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    PetsPage.SpeciesAvatar(v.Species, 34),
                    new Label { Text = v.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 95 }
                }
            }, 1, 0);
            grid.Add(PageKit.Cell(v.OwnerName), 2, 0);
            grid.Add(PageKit.Cell(v.VetName), 3, 0);
            grid.Add(new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Badges.Pill(Shorten(v.Reason, 24), "BodyText", "LightGray"),
                    new Label { Text = v.ItemsText, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 175, HorizontalOptions = LayoutOptions.Start }
                }
            }, 4, 0);
            if (_canManage) grid.Add(PharmacyKit.SmallButton("Create bill", "receipt", true, () => CreateBillRequested?.Invoke(this, v), "Create bill for " + v.PetName), 5, 0);
            PageKit.AddRowHover(grid);
            TableStack.Children.Add(PageKit.WithBottomLine(grid));
        }
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    // Bills -------------------------------------------------------------------------------------------------------

    public void ShowBills(PagedResult<Bill> page)
    {
        TableStack.Children.Clear();
        TotalBar.IsVisible = false;
        if (page.TotalCount == 0)
        {
            bool filtered = !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentStatus != null || CurrentFrom != null || CurrentTo != null;
            ShowState(filtered ? PageKit.StateMessage("No bills match your filters.")
                               : PageKit.EmptyState("receipt", "No bills yet", "Create one from the Ready to bill tab."));
            return;
        }
        TableState.IsVisible = false;
        TableScroll.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(_billColumns));
        foreach (var b in page.Items) TableStack.Children.Add(BillRow(b));
        ShowPager(page.Page, page.PageSize, page.TotalCount, page.From, page.To, "bill", "bills");
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    private View BillRow(Bill b)
    {
        var grid = PageKit.RowGrid(_billColumns, 64);
        bool isVoid = b.Status == BillStatuses.Void;
        grid.Add(new Label { Text = b.NumberText, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        grid.Add(PageKit.Cell(b.BillDate.ToString("MMM d, yyyy")), 1, 0);
        grid.Add(TwoLines(b.OwnerName, b.PetName), 2, 0);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = CurrencyHelper.Peso(b.TotalAmount), FontSize = 14, TextColor = ThemeResources.Color(isVoid ? "Muted" : "Navy"), TextDecorations = isVoid ? TextDecorations.Strikethrough : TextDecorations.None },
                new Label { Text = "Paid " + CurrencyHelper.Peso(b.Paid), FontSize = 12.5, TextColor = ThemeResources.Color(b.Paid > 0 ? "Success" : "Muted"), IsVisible = !isVoid }
            }
        }, 3, 0);
        grid.Add(new Label
        {
            Text = b.Balance > 0 ? CurrencyHelper.Peso(b.Balance) : "—",
            FontFamily = b.Balance > 0 ? "DMSansBold" : "DMSans",
            FontSize = 14,
            TextColor = ThemeResources.Color(b.Balance > 0 ? "Orange" : "Muted"),
            VerticalOptions = LayoutOptions.Center
        }, 4, 0);

        var status = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { Badges.BillStatus(b.Status) } };
        if (b.SyncStatus != SyncStatuses.Synced)
        {
            var sync = Badges.Sync(b.SyncStatus);
            if (b.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Possible duplicate bill");
            status.Children.Add(sync);
        }
        grid.Add(status, 5, 0);

        var actions = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (_canManage && b.IsOpen) actions.Children.Add(PharmacyKit.SmallButton("Receive payment", "wallet", false, () => PaymentRequested?.Invoke(this, b), "Receive payment for " + b.NumberText));
        Border menu = null;
        menu = PageKit.IconButton("ellipsis-vertical", "More for " + b.NumberText, () => MenuRequested?.Invoke(this, (menu, b)));
        actions.Children.Add(menu);
        grid.Add(actions, 6, 0);

        AddRowTap(grid, _billColumns, () => BillSelected?.Invoke(this, b));
        return PageKit.WithBottomLine(grid);
    }

    // Payments ----------------------------------------------------------------------------------------------------

    public void ShowPayments(PagedResult<Payment> page, decimal total)
    {
        TableStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            bool filtered = CurrentMethod != null || CurrentFrom != null || CurrentTo != null;
            ShowState(filtered ? PageKit.StateMessage("No payments match your filters.")
                               : PageKit.EmptyState("wallet", "No payments yet", "Payments appear here once received."));
            return;
        }
        TableState.IsVisible = false;
        TableScroll.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(PaymentColumns));
        foreach (var p in page.Items)
        {
            var grid = PageKit.RowGrid(PaymentColumns, 60);
            grid.Add(TwoLines(p.PaymentDate.ToString("MMM d, yyyy"), p.PaymentDate.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)), 0, 0);
            grid.Add(new Label { Text = p.BillNo, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 1, 0);
            grid.Add(PageKit.Cell(p.OwnerName), 2, 0);
            grid.Add(Badges.PaymentMethod(p.PaymentMethod), 3, 0);
            grid.Add(PageKit.Cell(string.IsNullOrEmpty(p.ReferenceNo) ? "—" : p.ReferenceNo, muted: true), 4, 0);
            grid.Add(new Label { Text = CurrencyHelper.Peso(p.AmountPaid), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 5, 0);
            var by = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { PageKit.Cell(p.ReceivedByName) } };
            if (p.SyncStatus != SyncStatuses.Synced) by.Children.Add(Badges.Sync(p.SyncStatus));
            grid.Add(by, 6, 0);
            int billId = p.BillId;
            AddRowTap(grid, PaymentColumns, () => PaymentBillSelected?.Invoke(this, billId), actionsColumn: false);
            TableStack.Children.Add(PageKit.WithBottomLine(grid));
        }
        TotalLabel.Text = "Total for selected period: " + CurrencyHelper.Peso(total);
        TotalBar.IsVisible = true;
        ShowPager(page.Page, page.PageSize, page.TotalCount, page.From, page.To, "payment", "payments");
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    // Building blocks -------------------------------------------------------------------------------------------------

    private void ShowPager(int page, int pageSize, int total, int from, int to, string one, string many)
    {
        _page = page;
        _pageCount = Math.Max(1, (total + pageSize - 1) / pageSize);
        PagerRow.IsVisible = true;
        PagerLabel.Text = $"Showing {from}–{to} of {total} {(total == 1 ? one : many)}";
        PrevButton.IsEnabled = page > 1;
        NextButton.IsEnabled = page < _pageCount;
    }

    private static View TwoLines(string first, string second) => new VerticalStackLayout
    {
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = first, FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150, HorizontalOptions = LayoutOptions.Start },
            new Label { Text = second ?? string.Empty, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150, HorizontalOptions = LayoutOptions.Start }
        }
    };

    private static string Shorten(string text, int max) => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private static void AddRowTap(Grid grid, TableColumn[] columns, Action open, bool actionsColumn = true)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (actionsColumn && e.GetPosition(grid) is Point p && p.X > grid.Width - columns[^1].Width - 20) return;
            open();
        };
        grid.GestureRecognizers.Add(tap);
        HandCursor.Apply(grid);
        PageKit.AddRowHover(grid);
    }

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
        LayOutToolbar();
        if (raise && changed) TabChanged?.Invoke(this, index);
    }

    /// <summary>Ready to bill: search. Bills: search, status, dates. Payments: dates, method.</summary>
    private void LayOutToolbar()
    {
        Toolbar.Children.Clear();
        Toolbar.ColumnDefinitions.Clear();
        void Put(View view, GridLength width)
        {
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition(width));
            Toolbar.Add(view, Toolbar.ColumnDefinitions.Count - 1, 0);
        }
        switch (CurrentTab)
        {
            case ReadyTab:
                SearchEntry.Placeholder = "Search pet or owner";
                Put(SearchBox, GridLength.Star);
                break;
            case BillsTab:
                SearchEntry.Placeholder = "Search bill no., owner, or pet";
                Put(SearchBox, GridLength.Star);
                Put(StatusBox, new GridLength(150));
                Put(ClearDatesButton, GridLength.Auto);
                Put(FromDate, new GridLength(145));
                Put(ToDate, new GridLength(145));
                break;
            default:
                Put(ClearDatesButton, GridLength.Auto);
                Put(FromDate, new GridLength(150));
                Put(ToDate, new GridLength(150));
                Put(new BoxView { Color = Colors.Transparent }, GridLength.Star);
                Put(MethodBox, new GridLength(190));
                break;
        }
    }

    private void ShowState(View content)
    {
        TableStack.Children.Clear();
        TableScroll.IsVisible = false;
        PagerRow.IsVisible = false;
        TotalBar.IsVisible = false;
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
