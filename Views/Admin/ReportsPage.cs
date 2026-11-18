using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Admin;

public sealed record PeriodChange(ReportPeriod Period, DateTime From, DateTime To);

/// <summary>Reports home (section 3.A): the period selector, the quick look, and the report cards by category.</summary>
public sealed class ReportsPage : ContentView
{
    private readonly List<(ReportPeriod Period, Border Button, Label Text)> _periods = new();
    private readonly DateBox _from, _to;
    private readonly View _custom;
    private readonly Label _rangeLabel, _customError;
    private readonly Grid _quickLook;
    private ReportPeriod _period;

    public ReportsPage(ReportPeriod period, DateTime from, DateTime to)
    {
        _period = period;

        // Period selector (segmented control) ---------------------------------------------------------------------
        var segments = new HorizontalStackLayout { Spacing = 0 };
        foreach (var (p, text) in DateRangeHelper.Periods)
        {
            var label = new Label { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
            var button = new Border
            {
                Padding = new Thickness(14, 8),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Content = label
            };
            var tap = new TapGestureRecognizer();
            var period1 = p;
            tap.Tapped += (s, e) => SelectPeriod(period1, raise: true);
            button.GestureRecognizers.Add(tap);
            HandCursor.Apply(button);
            SemanticProperties.SetDescription(button, text);
            _periods.Add((p, button, label));
            segments.Children.Add(button);
        }
        var segmentBox = new Border
        {
            Padding = new Thickness(4),
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color("Surface"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Content = segments
        };

        _from = new DateBox { Placeholder = "From", WidthRequest = 160, MaximumDate = DateTime.Today.AddYears(1), Date = from };
        _to = new DateBox { Placeholder = "To", WidthRequest = 160, MaximumDate = DateTime.Today.AddYears(1), Date = to };
        _customError = new Label { IsVisible = false, FontSize = 13, TextColor = ThemeResources.Color("Danger"), VerticalOptions = LayoutOptions.Center };
        _from.DateChanged += (s, e) => CustomChanged();
        _to.DateChanged += (s, e) => CustomChanged();
        _custom = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.End, IsVisible = false, Children = { _from, _to, _customError } };
        _rangeLabel = new Label { FontSize = 14, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.End };

        var header = new Grid { ColumnSpacing = 16, RowSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new PageHeader { Icon = "chart-column", Title = "Reports", Subtitle = "See how your clinic is doing, and share it in one click." }, 0, 0);
        header.Add(new VerticalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.End, Children = { segmentBox, _rangeLabel } }, 1, 0);

        _quickLook = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var stack = new VerticalStackLayout { Padding = new Thickness(40, 32, 40, 40), Spacing = 20, Children = { header, _custom, _quickLook } };
        foreach (ReportCategory category in Enum.GetValues(typeof(ReportCategory))) stack.Children.Add(CategorySection(category));

        var scroll = new ScrollView { Content = stack };
        ScrollFocus.DontScrollOnFocus(scroll);
        Content = scroll;
        SelectPeriod(period, raise: false);
        _rangeLabel.Text = DateRangeHelper.Label(from, to);
        ShowQuickLook(null);
    }

    public event EventHandler<PeriodChange> PeriodChanged;
    public event EventHandler<ReportType> ReportRequested;

    /// <summary>Collected, appointments, no-show rate (with the change vs the period before) and stock alerts.</summary>
    public void ShowQuickLook(ReportQuickLook q)
    {
        string label = _period switch
        {
            ReportPeriod.Today => "today",
            ReportPeriod.ThisWeek => "this week",
            ReportPeriod.ThisMonth => "this month",
            ReportPeriod.LastMonth => "last month",
            _ => "in this period"
        };
        string before = _period is ReportPeriod.ThisMonth or ReportPeriod.LastMonth ? "vs month before" : "vs period before";
        View[] cards;
        if (q == null)
        {
            cards = new View[]
            {
                DashboardKit.StatCard("wallet", "Mint", "CompletedText", "…", "Collected " + label),
                DashboardKit.StatCard("calendar-days", "Cyan", "Navy", "…", "Appointments " + label),
                DashboardKit.StatCard("user-x", "OrangeSoft", "Orange", "…", "No-show rate"),
                DashboardKit.StatCard("package-x", "WarningBox", "ConsultText", "…", "Stock alerts")
            };
        }
        else
        {
            string collectedChange = null, collectedColor = "Muted";
            if (q.CollectedBefore > 0)
            {
                decimal pct = Math.Round((q.Collected - q.CollectedBefore) / q.CollectedBefore * 100);
                collectedChange = (pct >= 0 ? "+" : "−") + Math.Abs(pct) + "% " + before;
                collectedColor = pct >= 0 ? "Success" : "Orange";
            }
            double diff = Math.Round(q.NoShowRate - q.NoShowRateBefore, 1);
            string noShowChange = (diff >= 0 ? "+" : "−") + Math.Abs(diff).ToString("0.#") + "% " + before;
            cards = new View[]
            {
                DashboardKit.StatCard("wallet", "Mint", "CompletedText", CurrencyHelper.Peso(q.Collected), "Collected " + label, collectedChange, collectedColor,
                    () => ReportRequested?.Invoke(this, ReportType.DailyCollection)),
                DashboardKit.StatCard("calendar-days", "Cyan", "Navy", q.Appointments.ToString("N0"), "Appointments " + label,
                    onTap: () => ReportRequested?.Invoke(this, ReportType.AppointmentSummary)),
                DashboardKit.StatCard("user-x", "OrangeSoft", "Orange", q.NoShowRate.ToString("0.#") + "%", "No-show rate", noShowChange,
                    diff <= 0 ? "Success" : "Orange", () => ReportRequested?.Invoke(this, ReportType.NoShowCancellation)),
                DashboardKit.StatCard("package-x", "WarningBox", "ConsultText", q.StockAlerts.ToString("N0"), "Stock alerts", "Low, out, expiring or expired",
                    onTap: () => ReportRequested?.Invoke(this, ReportType.StockLevels))
            };
        }
        var grid = DashboardKit.StatGrid(cards, 4);
        _quickLook.Children.Clear();
        _quickLook.Children.Add(grid);
    }

    public void ShowQuickLookError(string message)
    {
        _quickLook.Children.Clear();
        _quickLook.Children.Add(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
    }

    private void SelectPeriod(ReportPeriod period, bool raise)
    {
        _period = period;
        foreach (var (p, button, text) in _periods)
        {
            bool on = p == period;
            button.BackgroundColor = ThemeResources.Color(on ? "Cyan" : "Surface");
            text.FontFamily = on ? "DMSansBold" : "DMSans";
            text.TextColor = ThemeResources.Color(on ? "Navy" : "BodyText");
        }
        _custom.IsVisible = period == ReportPeriod.Custom;
        if (!raise) return;
        if (period == ReportPeriod.Custom) { CustomChanged(); return; }
        var (from, to) = DateRangeHelper.RangeOf(period, DateTime.Today);
        _from.Date = from;
        _to.Date = to;
        _rangeLabel.Text = DateRangeHelper.Label(from, to);
        PeriodChanged?.Invoke(this, new PeriodChange(period, from, to));
    }

    private void CustomChanged()
    {
        if (_period != ReportPeriod.Custom) return;
        string problem = DateRangeHelper.Problem(_from.Date, _to.Date);
        _customError.Text = problem ?? string.Empty;
        _customError.IsVisible = problem != null;
        if (problem != null) return;
        _rangeLabel.Text = DateRangeHelper.Label(_from.Date.Value, _to.Date.Value);
        PeriodChanged?.Invoke(this, new PeriodChange(ReportPeriod.Custom, _from.Date.Value.Date, _to.Date.Value.Date));
    }

    // Report cards ----------------------------------------------------------------------------------------------------

    private View CategorySection(ReportCategory category)
    {
        var (name, icon, tile, iconColor) = ReportCatalog.Category(category);
        var reports = ReportCatalog.All.Where(r => r.Category == category).ToList();
        var title = new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Tile(icon, tile, iconColor, 36),
                new Label { Text = name, FontFamily = "NunitoExtraBold", FontSize = 20, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                new Label { Text = reports.Count + " reports", FontSize = 14, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        };
        var cards = reports.Select(r => (View)ReportCard(r, tile, iconColor)).ToList();
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        int columns = 0;
        grid.SizeChanged += (s, e) =>
        {
            int c = grid.Width >= 820 ? 3 : grid.Width >= 520 ? 2 : 1;
            if (c == columns) return;
            columns = c;
            PageKit.PlaceInGrid(grid, cards, c);
        };
        columns = 3;
        PageKit.PlaceInGrid(grid, cards, 3);
        return new VerticalStackLayout { Spacing = 14, Margin = new Thickness(0, 8, 0, 0), Children = { title, grid } };
    }

    private View ReportCard(ReportDefinition r, string tile, string iconColor)
    {
        var open = new Label { Text = "Open →", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), Margin = new Thickness(0, 6, 0, 0) };
        var card = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(20),
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    Tile(r.Icon, tile, iconColor, 40),
                    new Label { Text = r.Name, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(0, 6, 0, 0) },
                    new Label { Text = r.Description, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap },
                    open
                }
            }
        };
        DashboardKit.Clickable(card, () => ReportRequested?.Invoke(this, r.Type), "Open " + r.Name);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => card.BackgroundColor = ThemeResources.Color("RowHover");
        hover.PointerExited += (s, e) => card.BackgroundColor = ThemeResources.Color("Surface");
        card.GestureRecognizers.Add(hover);
        return card;
    }

    private static View Tile(string icon, string background, string color, double size) => new Border
    {
        WidthRequest = size,
        HeightRequest = size,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        BackgroundColor = ThemeResources.Color(background),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Center,
        Content = new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(color), WidthRequest = size * 0.5, HeightRequest = size * 0.5, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
    };
}
