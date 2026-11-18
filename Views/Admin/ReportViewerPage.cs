using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Admin;

/// <summary>
/// The report viewer (section 3.B), one layout for all 17 reports: title bar with Print / Export, filter bar,
/// offline banner, summary cards, chart(s), a sortable and searchable table with a TOTAL row, and the footer note.
/// </summary>
public sealed class ReportViewerPage : ContentView
{
    private const int PageSize = 31;

    private readonly DateBox _from, _to;
    private readonly Label _dateError;
    private readonly PickerField _vet, _status, _species, _method, _cashier, _category, _type;
    private readonly IReadOnlyList<(int Id, string Name)> _vets, _cashiers;
    private readonly VerticalStackLayout _body;
    private readonly ScrollView _scroll;

    private IReadOnlyList<object[]> _rows = Array.Empty<object[]>();
    private int _sortColumn = -1;
    private bool _sortDescending;
    private string _search = string.Empty;
    private int _page = 1;
    private VerticalStackLayout _table;
    private Label _pagerLabel;
    private HoverButton _prev, _next;
    private IDispatcherTimer _searchTimer;

    public ReportViewerPage(ReportDefinition definition, DateTime from, DateTime to,
        IReadOnlyList<(int Id, string Name)> vets, IReadOnlyList<(int Id, string Name)> cashiers)
    {
        Definition = definition;
        _vets = vets ?? Array.Empty<(int, string)>();
        _cashiers = cashiers ?? Array.Empty<(int, string)>();
        var (category, _, tile, tileText) = ReportCatalog.Category(definition.Category);

        // Title bar ------------------------------------------------------------------------------------------------
        var back = new TextLink { Text = "← Reports", FontSize = 14 };
        back.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var print = new IconTextButton { Icon = "printer", Text = "Print", Outline = true };
        print.Clicked += (s, e) => PrintRequested?.Invoke(this, EventArgs.Empty);
        var excel = new IconTextButton { Icon = "file-spreadsheet", Text = "Export Excel", Outline = true };
        excel.Clicked += (s, e) => ExcelRequested?.Invoke(this, EventArgs.Empty);
        var pdf = new IconTextButton { Icon = "file-down", Text = "Export PDF" };
        pdf.Clicked += (s, e) => PdfRequested?.Invoke(this, EventArgs.Empty);
        var titleRow = new Grid { ColumnSpacing = 16, RowSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        titleRow.Add(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Badges.Pill(category, tileText, tile),
                new Label { Text = definition.Name + " report", FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = definition.Description, FontSize = 14.5, TextColor = ThemeResources.Color("BodyText") }
            }
        }, 0, 0);
        titleRow.Add(new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.End, Children = { print, excel, pdf } }, 1, 0);

        // Filter bar -----------------------------------------------------------------------------------------------
        _from = new DateBox { Placeholder = "From", MaximumDate = DateTime.Today.AddYears(1), Date = from };
        _to = new DateBox { Placeholder = "To", MaximumDate = DateTime.Today.AddYears(1), Date = to };
        _dateError = new Label { IsVisible = false, FontSize = 13, TextColor = ThemeResources.Color("Danger"), Margin = new Thickness(0, 6, 0, 0) };
        _from.DateChanged += (s, e) => _dateError.IsVisible = false;
        _to.DateChanged += (s, e) => _dateError.IsVisible = false;
        var chips = new WrapLayout();
        foreach (var (period, text) in DateRangeHelper.Periods.Where(p => p.Period != ReportPeriod.Custom))
            chips.Children.Add(QuickChip(text, () =>
            {
                var (f, t) = DateRangeHelper.RangeOf(period, DateTime.Today);
                _from.Date = f;
                _to.Date = t;
                GenerateRequested?.Invoke(this, EventArgs.Empty);
            }));

        var filters = new WrapLayout();
        void AddFilter(PickerField field) { field.WidthRequest = 190; field.Margin = new Thickness(0, 0, 12, 8); filters.Children.Add(field); }
        var f = definition.Filters;
        if (f.HasFlag(ReportFilter.Vet)) AddFilter(_vet = Picker("Vet", "All vets", _vets.Select(v => "Dr. " + v.Name)));
        if (f.HasFlag(ReportFilter.AppointmentStatus)) AddFilter(_status = Picker("Status", "All status", AppointmentStatuses.All));
        if (f.HasFlag(ReportFilter.Species)) AddFilter(_species = Picker("Species", "All species", PetSpecies.All));
        if (f.HasFlag(ReportFilter.PaymentMethod)) AddFilter(_method = Picker("Method", "All methods", PaymentMethods.All));
        if (f.HasFlag(ReportFilter.Cashier)) AddFilter(_cashier = Picker("Cashier", "All cashiers", _cashiers.Select(c => c.Name)));
        if (f.HasFlag(ReportFilter.MedicineCategory)) AddFilter(_category = Picker("Category", "All categories", Medicine.Categories));
        if (f.HasFlag(ReportFilter.TransactionType)) AddFilter(_type = Picker("Type", "All types", StockStatusHelper.TransactionTypes));

        var generate = new HoverButton { Text = "Generate", Style = ThemeResources.Get<Style>("NavyOutlineButton"), BackgroundColor = ThemeResources.Color("Navy"), TextColor = Colors.White, Padding = new Thickness(22, 0), HeightRequest = 44, VerticalOptions = LayoutOptions.End };
        generate.Clicked += (s, e) => GenerateRequested?.Invoke(this, EventArgs.Empty);

        var dates = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(new GridLength(165)), new ColumnDefinition(new GridLength(165)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        dates.Add(Labeled("From *", _from), 0, 0);
        dates.Add(Labeled("To *", _to), 1, 0);
        dates.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Children = { chips } }, 2, 0);
        dates.Add(generate, 3, 0);
        var filterCard = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(18, 16),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    dates,
                    _dateError,
                    new Label { IsVisible = definition.Snapshot, Text = "This report shows today's state; the dates only label the printout.", FontSize = 13, TextColor = ThemeResources.Color("Muted") },
                    filters
                }
            }
        };
        if (filters.Children.Count == 0) filters.IsVisible = false;

        _body = new VerticalStackLayout { Spacing = 20 };
        _scroll = new ScrollView
        {
            Content = new VerticalStackLayout { Padding = new Thickness(40, 28, 40, 40), Spacing = 18, Children = { back, titleRow, filterCard, _body } }
        };
        ScrollFocus.DontScrollOnFocus(_scroll);
        Content = _scroll;
    }

    public ReportDefinition Definition { get; }

    /// <summary>The report on screen (what Print and Export use).</summary>
    public ReportResult Result { get; private set; }

    public event EventHandler BackRequested;
    public event EventHandler GenerateRequested;
    public event EventHandler PrintRequested;
    public event EventHandler PdfRequested;
    public event EventHandler ExcelRequested;

    /// <summary>The filters, or a date problem (rule 4) shown under the dates.</summary>
    public ReportFilters ReadFilters(out string dateProblem)
    {
        dateProblem = DateRangeHelper.Problem(_from.Date, _to.Date);
        if (dateProblem != null) return null;
        string Pick(PickerField p) => p?.SelectedValue as string;
        int? Person(PickerField p) => p?.SelectedValue is int id && id > 0 ? id : null;
        return new ReportFilters
        {
            From = _from.Date.Value.Date,
            To = _to.Date.Value.Date,
            VetId = Person(_vet),
            AppointmentStatus = Pick(_status),
            Species = Pick(_species),
            PaymentMethod = Pick(_method),
            CashierId = Person(_cashier),
            MedicineCategory = Pick(_category),
            TransactionType = Pick(_type)
        };
    }

    public void ShowDateError(string message)
    {
        _dateError.Text = message;
        _dateError.IsVisible = true;
    }

    public void ShowLoading() => SetBody(PageKit.StateMessage("Generating the report…"));

    public void ShowError(string message) => SetBody(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    // The report --------------------------------------------------------------------------------------------------

    public void Show(ReportResult result, SyncState sync, string generatedBy)
    {
        Result = result;
        _rows = result.Rows;
        _sortColumn = -1;
        _search = string.Empty;
        _page = 1;
        var parts = new List<View>();

        if (!sync.Online || sync.Pending > 0)
        {
            var sync1 = new TextLink { Text = "Sync now", FontSize = 13.5, VerticalOptions = LayoutOptions.Center };
            sync1.Clicked += (s, e) => GenerateRequested?.Invoke(this, EventArgs.Empty);
            var banner = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            banner.Add(new LucideIcon { Icon = "circle-alert", StrokeColor = ThemeResources.Color("ConsultText"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center }, 0, 0);
            banner.Add(new Label { Text = (sync.Online ? sync.Pending + " changes are still pending — " : "Offline — ") + "this report only includes data saved on this computer. Sync first for complete numbers.", FontSize = 14, TextColor = ThemeResources.Color("ConsultText"), VerticalOptions = LayoutOptions.Center }, 1, 0);
            banner.Add(sync1, 2, 0);
            parts.Add(new Border { Padding = new Thickness(16, 10), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, BackgroundColor = ThemeResources.Color("WarningBox"), Content = banner });
        }
        if (!string.IsNullOrEmpty(result.Note))
            parts.Add(new Label { Text = result.Note, FontSize = 14, TextColor = ThemeResources.Color("BodyText") });

        parts.Add(DashboardKit.StatGrid(result.Cards.Select(c => DashboardKit.StatCard(c.Icon, c.Circle, c.IconColor, c.Value, c.Label, c.Sub)).ToList(), Math.Max(3, result.Cards.Count)));

        if (result.IsEmpty)
        {
            var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
            empty.Children.Add(new IconBadge { Icon = "chart-column", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Cyan"), IconColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center });
            empty.Children.Add(new Label { Text = "No data for this period", FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) });
            empty.Children.Add(new Label { Text = "Try a different date range.", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.Center });
            parts.Add(new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Content = empty });
        }
        else
        {
            var main = ChartCard(result.Chart);
            parts.Add(result.SideChart == null ? main : DashboardKit.Columns(820, new[] { 13.0, 7.0 }, main, ChartCard(result.SideChart)));
            parts.Add(TableCard(result));
        }

        parts.Add(new Label
        {
            Text = generatedBy + " · " + (sync.Online ? sync.Pending > 0 ? sync.Pending + " changes pending" : "Online · Synced" : "Offline"),
            FontSize = 13,
            TextColor = ThemeResources.Color("Muted")
        });
        SetBody(parts.ToArray());
    }

    private void SetBody(params View[] parts)
    {
        _body.Children.Clear();
        foreach (var p in parts) _body.Children.Add(p);
    }

    // Charts --------------------------------------------------------------------------------------------------------

    private static View ChartCard(ReportChart chart)
    {
        Func<double, string> format = chart.Money ? v => Short(v, true) : v => Short(v, false);
        View view;
        switch (chart.Kind)
        {
            case ChartKind.Donut:
            {
                var donut = new DonutChartView { HeightRequest = 190, WidthRequest = 190, HorizontalOptions = LayoutOptions.Center, Format = chart.Money ? v => CurrencyHelper.Peso((decimal)v) : v => v.ToString("N0") };
                var values = chart.Series[0].Values;
                donut.Points = chart.Labels.Select((l, i) => new ChartPoint(l, values[i])).ToList();
                double total = values.Sum();
                var legend = new VerticalStackLayout { Spacing = 6 };
                for (int i = 0; i < chart.Labels.Count; i++)
                {
                    if (values[i] <= 0) continue;
                    var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                    row.Add(new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 2, Color = DonutChartView.ColorAt(i), VerticalOptions = LayoutOptions.Center }, 0, 0);
                    row.Add(new Label { Text = chart.Labels[i], FontSize = 13, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation }, 1, 0);
                    row.Add(new Label
                    {
                        Text = (chart.Money ? CurrencyHelper.Peso((decimal)values[i]) : values[i].ToString("N0")) + " (" + (total == 0 ? 0 : values[i] * 100 / total).ToString("0.#") + "%)",
                        FontSize = 13,
                        TextColor = ThemeResources.Color("BodyText")
                    }, 2, 0);
                    legend.Children.Add(row);
                }
                view = new VerticalStackLayout { Spacing = 14, Children = { donut, legend } };
                break;
            }
            case ChartKind.HorizontalBar:
            {
                var bars = new HorizontalBarChartView { Format = chart.Money ? v => CurrencyHelper.Peso((decimal)v) : v => v.ToString("N0") };
                bars.Points = chart.Labels.Select((l, i) => new ChartPoint(l, chart.Series[0].Values[i])).ToList();
                bars.HeightRequest = Math.Max(1, chart.Labels.Count) * HorizontalBarChartView.RowHeight;
                view = bars;
                break;
            }
            case ChartKind.Line:
            {
                var line = new LineChartView { HeightRequest = 240, Format = format };
                line.Points = chart.Labels.Select((l, i) => new ChartPoint(l, chart.Series[0].Values[i])).ToList();
                view = line;
                break;
            }
            default:
            {
                var bars = new StackedBarChartView { HeightRequest = 240, Format = format };
                bars.Series = chart.Series.Select((s, i) => (s.Name, DonutChartView.ColorAt(i))).ToArray();
                bars.Points = chart.Labels.Select((l, i) => new ChartPoint(l, chart.Series.Select(s => s.Values[i]).ToArray())).ToList();
                view = bars;
                break;
            }
        }
        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(20),
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { new Label { Text = chart.Title, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy") }, view }
            }
        };
    }

    /// <summary>Axis labels: "₱12.5k", "1.2k", "40".</summary>
    private static string Short(double v, bool money)
    {
        string text = Math.Abs(v) >= 1000 ? (v / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k" : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return money ? "₱" + text : text;
    }

    // Table -------------------------------------------------------------------------------------------------------------

    private View TableCard(ReportResult result)
    {
        var search = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Placeholder = "Search" };
        search.TextChanged += (s, e) =>
        {
            _search = search.Text?.Trim() ?? string.Empty;
            _searchTimer ??= CreateTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        var searchBox = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = 40, WidthRequest = 220, Padding = new Thickness(10, 0), Content = search };
        var head = new Grid { Padding = new Thickness(20, 16), ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new Label { Text = result.TableTitle, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        head.Add(searchBox, 1, 0);

        _table = new VerticalStackLayout();
        _pagerLabel = new Label { FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center };
        _prev = new HoverButton { Text = "Prev", Style = ThemeResources.Get<Style>("OutlineButton"), HeightRequest = 36, Padding = new Thickness(14, 0) };
        _next = new HoverButton { Text = "Next", Style = ThemeResources.Get<Style>("OutlineButton"), HeightRequest = 36, Padding = new Thickness(14, 0) };
        _prev.Clicked += (s, e) => { _page--; RenderTable(); };
        _next.Clicked += (s, e) => { _page++; RenderTable(); };
        var pager = new Grid { Padding = new Thickness(20, 14), ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        pager.Add(_pagerLabel, 0, 0);
        pager.Add(_prev, 1, 0);
        pager.Add(_next, 2, 0);

        var scroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _table };
        ScrollFocus.DontScrollOnFocus(scroll);
        RenderTable();
        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = 0,
            Content = new VerticalStackLayout { Children = { head, scroll, pager } }
        };
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => { _page = 1; RenderTable(); };
        return timer;
    }

    private void RenderTable()
    {
        var result = Result;
        var columns = result.Columns.Select(c => new TableColumn(c.Title, c.Width)).ToArray();
        IEnumerable<object[]> rows = _rows;
        if (!string.IsNullOrEmpty(_search))
            rows = rows.Where(r => r.Select((v, i) => ReportFormat.Cell(v, result.Columns[i].Kind)).Any(t => t.Contains(_search, StringComparison.OrdinalIgnoreCase)));
        if (_sortColumn >= 0)
        {
            Func<object[], object> key = r => r[_sortColumn] is string s ? s.ToLowerInvariant() : r[_sortColumn];
            var comparer = Comparer<object>.Create(CompareValues);
            rows = _sortDescending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
        }
        var list = rows.ToList();
        int pages = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 1, pages);

        _table.Children.Clear();
        _table.Children.Add(HeaderRow(result, columns));
        foreach (var row in list.Skip((_page - 1) * PageSize).Take(PageSize)) _table.Children.Add(Row(result, columns, row, total: false));
        if (list.Count == 0)
            _table.Children.Add(new Label { Text = "No rows match your search.", FontSize = 14, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(20, 14) });
        if (result.Total != null && string.IsNullOrEmpty(_search)) _table.Children.Add(Row(result, columns, result.Total, total: true));

        int from = list.Count == 0 ? 0 : (_page - 1) * PageSize + 1, to = Math.Min(_page * PageSize, list.Count);
        _pagerLabel.Text = $"Showing {from}–{to} of {list.Count} {(list.Count == 1 ? "row" : "rows")}";
        _prev.IsEnabled = _page > 1;
        _next.IsEnabled = _page < pages;
        _prev.IsVisible = _next.IsVisible = pages > 1;
    }

    private static int CompareValues(object a, object b)
    {
        if (a == null || a is string { Length: 0 }) return b == null ? 0 : -1;
        if (b == null) return 1;
        if (a.GetType() == b.GetType() && a is IComparable ca) return ca.CompareTo(b);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sortable headers: a chevron shows the sort column and direction.</summary>
    private View HeaderRow(ReportResult result, TableColumn[] columns)
    {
        var grid = PageKit.RowGrid(columns, 44);
        grid.BackgroundColor = ThemeResources.Color("InputBackground");
        for (int i = 0; i < columns.Length; i++)
        {
            int index = i;
            var label = new Label
            {
                Text = columns[i].Title.ToUpperInvariant(),
                FontFamily = "DMSansBold",
                FontSize = 12,
                CharacterSpacing = 0.67,
                TextColor = ThemeResources.Color(_sortColumn == i ? "Navy" : "SectionLabel"),
                VerticalOptions = LayoutOptions.Center
            };
            var cell = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = result.Columns[i].RightAligned ? LayoutOptions.End : LayoutOptions.Start,
                Margin = new Thickness(0, 0, 12, 0),
                Children = { label }
            };
            if (_sortColumn == i)
                cell.Children.Add(new LucideIcon { Icon = _sortDescending ? "chevron-down" : "chevron-up", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 13, HeightRequest = 13, VerticalOptions = LayoutOptions.Center });
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (_sortColumn == index) _sortDescending = !_sortDescending;
                else { _sortColumn = index; _sortDescending = result.Columns[index].RightAligned || result.Columns[index].Kind is ColumnKind.Date or ColumnKind.DateTime; }
                _page = 1;
                RenderTable();
            };
            cell.GestureRecognizers.Add(tap);
            HandCursor.Apply(cell);
            SemanticProperties.SetDescription(cell, "Sort by " + columns[i].Title);
            grid.Add(cell, i, 0);
        }
        return PageKit.WithBottomLine(grid);
    }

    private static View Row(ReportResult result, TableColumn[] columns, object[] values, bool total)
    {
        var grid = PageKit.RowGrid(columns, total ? 50 : 48);
        if (total) grid.BackgroundColor = ThemeResources.Color("CyanSoft");
        for (int i = 0; i < columns.Length && i < values.Length; i++)
        {
            var column = result.Columns[i];
            string text = ReportFormat.Cell(values[i], column.Kind);
            if (column.Kind == ColumnKind.Pill && !total && !string.IsNullOrEmpty(text))
            {
                var (background, color) = ReportFormat.PillColors(text);
                grid.Add(Badges.Pill(text, color, background), i, 0);
                continue;
            }
            grid.Add(new Label
            {
                Text = text,
                FontFamily = total || (column.Kind == ColumnKind.Money && i == columns.Length - 1) ? "DMSansBold" : "DMSans",
                FontSize = 14,
                TextColor = ThemeResources.Color(column.Kind == ColumnKind.Number && values[i] is int n && n < 0 ? "Danger" : "Navy"),
                HorizontalTextAlignment = column.RightAligned ? TextAlignment.End : TextAlignment.Start,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 12, 0)
            }, i, 0);
        }
        if (!total) PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    // Building blocks ---------------------------------------------------------------------------------------------------

    private PickerField Picker(string label, string all, IEnumerable<string> items)
    {
        var field = new PickerField(label.ToLowerInvariant(), label);
        var list = new List<(string, object)> { (all, null) };
        if (label == "Vet") list.AddRange(_vets.Select(v => ("Dr. " + v.Name, (object)v.Id)));
        else if (label == "Cashier") list.AddRange(_cashiers.Select(c => (c.Name, (object)c.Id)));
        else list.AddRange(items.Select(x => (x, (object)x)));
        field.SetItems(list, null);
        field.Select(null);
        return field;
    }

    private static View Labeled(string label, View field) => new VerticalStackLayout { Children = { PharmacyKit.FieldLabel(label), field } };

    private static View QuickChip(string text, Action onTap)
    {
        var chip = new Border
        {
            Padding = new Thickness(12, 7),
            Margin = new Thickness(0, 0, 8, 6),
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            BackgroundColor = ThemeResources.Color("Surface"),
            Content = new Label { Text = text, FontSize = 13.5, TextColor = ThemeResources.Color("Navy") }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        chip.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => chip.BackgroundColor = ThemeResources.Color("Cyan");
        hover.PointerExited += (s, e) => chip.BackgroundColor = ThemeResources.Color("Surface");
        chip.GestureRecognizers.Add(hover);
        HandCursor.Apply(chip);
        SemanticProperties.SetDescription(chip, text);
        return chip;
    }
}
