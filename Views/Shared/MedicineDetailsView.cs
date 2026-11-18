using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>Medicine details (side panel, 480px): header, stock / reorder / expiry chips, actions and the stock history.</summary>
public sealed class MedicineDetailsView : ContentView
{
    public const double PanelWidth = 480;

    public MedicineDetailsView(Medicine m, IReadOnlyList<StockLogEntry> history, int totalHistory, bool canManage)
    {
        var status = StockStatusHelper.StatusOf(m, DateTime.Today);
        var (_, statusBackground, statusText) = StockStatusHelper.Style(status);

        var header = new Grid { Padding = new Thickness(24, 20), ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(PharmacyKit.MedicineTile(m.IsVaccine, 52), 0, 0);
        var meta = new HorizontalStackLayout { Spacing = 8, Children = { Badges.Pill(m.Category, "BodyText", "LightGray") } };
        meta.Children.Add(new Label { Text = m.PerUnitText + " · " + PharmacyKit.Peso(m.UnitPrice), FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center });
        header.Add(new VerticalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = m.MedicineName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap },
                meta
            }
        }, 1, 0);
        header.Add(PageKit.IconButton("x", "Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)), 2, 0);

        // Chips are coloured by the status they describe.
        bool stockWarning = status is StockStatus.LowStock or StockStatus.OutOfStock;
        bool expiryWarning = status is StockStatus.ExpiringSoon or StockStatus.Expired;
        var chips = new WrapLayout();
        chips.Children.Add(Chip("package", "Stock: " + m.StockQty.ToString("N0") + " " + m.Unit, stockWarning ? statusBackground : "Mint", stockWarning ? statusText : "CompletedText"));
        chips.Children.Add(Chip("trending-down", "Reorder at: " + m.ReorderLevel.ToString("N0"), "CyanSoft", "Navy"));
        chips.Children.Add(Chip("calendar-clock", m.ExpiryDate == null ? "No expiry date" : (m.IsExpired ? "Expired: " : "Expires: ") + m.ExpiryDate.Value.ToString("MMM dd, yyyy"),
            expiryWarning ? statusBackground : "CyanSoft", expiryWarning ? statusText : "Navy"));

        var body = new VerticalStackLayout { Padding = new Thickness(24, 4, 24, 24), Spacing = 18, Children = { chips } };

        if (canManage)
        {
            var edit = new HoverButton { Text = "Edit", Style = ThemeResources.Get<Style>("NavyOutlineButton"), Padding = new Thickness(18, 0) };
            edit.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
            var adjust = new HoverButton { Text = "Adjust stock", Style = ThemeResources.Get<Style>("NavyOutlineButton"), Padding = new Thickness(18, 0) };
            adjust.Clicked += (s, e) => AdjustRequested?.Invoke(this, EventArgs.Empty);
            var stockIn = new IconTextButton { Icon = "plus", Text = "Stock in" };
            stockIn.Clicked += (s, e) => StockInRequested?.Invoke(this, EventArgs.Empty);
            var buttons = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            buttons.Add(edit, 0, 0);
            buttons.Add(adjust, 1, 0);
            buttons.Add(stockIn, 2, 0);
            body.Children.Add(buttons);
        }

        var historyHeader = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        historyHeader.Add(new Label { Text = "History", FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        if (totalHistory > history.Count)
        {
            var all = new TextLink { Text = "View all " + totalHistory };
            all.Clicked += (s, e) => AllHistoryRequested?.Invoke(this, EventArgs.Empty);
            historyHeader.Add(all, 1, 0);
        }
        body.Children.Add(historyHeader);

        if (history.Count == 0)
            body.Children.Add(new Label { Text = "No stock changes yet.", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        var list = new VerticalStackLayout();
        foreach (var entry in history) list.Children.Add(HistoryRow(entry));
        body.Children.Add(list);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        layout.Add(header, 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        Content = layout;
    }

    public event EventHandler CloseRequested;
    public event EventHandler EditRequested;
    public event EventHandler AdjustRequested;
    public event EventHandler StockInRequested;
    public event EventHandler AllHistoryRequested;

    private static View Chip(string icon, string text, string backgroundKey, string colorKey) => new Border
    {
        Padding = new Thickness(12, 7),
        Margin = new Thickness(0, 0, 8, 8),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 16 },
        BackgroundColor = ThemeResources.Color(backgroundKey),
        Content = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(colorKey), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13, TextColor = ThemeResources.Color(colorKey), VerticalOptions = LayoutOptions.Center }
            }
        }
    };

    /// <summary>Date, type pill, ± quantity, who and remarks.</summary>
    private static View HistoryRow(StockLogEntry e)
    {
        var grid = new Grid
        {
            Padding = new Thickness(0, 12),
            ColumnSpacing = 12,
            RowSpacing = 4,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                PharmacyKit.TransactionPill(e.TransactionType),
                new Label { Text = e.Date.ToString("MMM dd, yyyy · h:mm tt", System.Globalization.CultureInfo.InvariantCulture), FontSize = 13, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
            }
        }, 0, 0);
        grid.Add(PharmacyKit.SignedQuantity(e.Quantity, e.QuantityText), 1, 0);
        var who = new Label
        {
            Text = e.DoneBy + (string.IsNullOrEmpty(e.Remarks) ? string.Empty : " · " + e.Remarks),
            FontSize = 13,
            TextColor = ThemeResources.Color("Muted"),
            LineBreakMode = LineBreakMode.WordWrap
        };
        grid.Add(who, 0, 1);
        Grid.SetColumnSpan(who, 2);
        return PageKit.WithBottomLine(grid);
    }
}
