using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// What a dashboard can ask for. The dashboard is read-only (rule 3): these open the real module page or panel,
/// where the module's own rules apply. Set by DashboardController.
/// </summary>
public sealed class DashboardActions
{
    /// <summary>Open a module (menu key), optionally on a record and with a shortcut ("status:LowStock", "pay", "add" …).</summary>
    public Action<string, int?, string> OpenModule { get; set; } = (_, _, _) => { };

    public Action<Appointment> OpenAppointment { get; set; } = _ => { };
    public Action<Appointment> StartConsultation { get; set; } = _ => { };
    public Action<int> CreateBill { get; set; } = _ => { };
    public Action<int> ReceivePayment { get; set; } = _ => { };
    public Action<int> Dispense { get; set; } = _ => { };
    public Action<RecordVaccination> BookFollowUp { get; set; } = _ => { };
    public Action BookAppointment { get; set; } = () => { };
    public Action WalkIn { get; set; } = () => { };
    public Action<int> OpenRecord { get; set; } = _ => { };
    public Action SyncNow { get; set; } = () => { };
}

/// <summary>A quick-action button: text, icon, and whether it is the orange primary one.</summary>
public sealed record QuickAction(string Text, string Icon, Action OnTap, bool Primary = false);

/// <summary>Building blocks shared by the three dashboards (section 2 and the UI prompt's shared styles).</summary>
public static class DashboardKit
{
    // Header ------------------------------------------------------------------------------------------------------

    /// <summary>"Good morning, Ana" + "Tuesday, Nov 17, 2026 · Clinic", with the sync pill on the right.</summary>
    public static View Header(string name, string clinic, SyncState sync, Action syncNow)
    {
        var now = DateTime.Now;
        var titles = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = GreetingHelper.Greeting(now) + ", " + name, FontFamily = "NunitoExtraBold", FontSize = 30, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = now.ToString("dddd, MMM d, yyyy") + (string.IsNullOrEmpty(clinic) ? string.Empty : " · " + clinic), FontSize = 15, TextColor = ThemeResources.Color("BodyText") }
            }
        };
        var grid = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(titles, 0, 0);
        grid.Add(SyncPill(sync, syncNow), 1, 0);
        var stack = new VerticalStackLayout { Spacing = 14, Children = { grid } };
        if (!sync.Online)
        {
            stack.Children.Add(new Border
            {
                Padding = new Thickness(16, 10),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = ThemeResources.Color("LightGray"),
                Content = new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new LucideIcon { Icon = "wifi-off", StrokeColor = ThemeResources.Color("BodyText"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                        new Label { Text = "You're offline. Numbers only include data saved on this computer.", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
                    }
                }
            });
        }
        return stack;
    }

    /// <summary>Mint "Online · Synced", amber "Online · N changes pending" + Sync now, or gray "Offline · …".</summary>
    public static View SyncPill(SyncState sync, Action syncNow)
    {
        string icon, text, background, color;
        if (!sync.Online) (icon, text, background, color) = ("cloud-off", "Offline · Showing data on this computer", "LightGray", "BodyText");
        else if (sync.Pending > 0) (icon, text, background, color) = ("refresh-cw", "Online · " + sync.Pending + (sync.Pending == 1 ? " change" : " changes") + " pending", "WarningBox", "ConsultText");
        else (icon, text, background, color) = ("cloud-check", "Online · Synced", "Mint", "CompletedText");

        var content = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(color), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13.5, TextColor = ThemeResources.Color(color), VerticalOptions = LayoutOptions.Center }
            }
        };
        if (sync.Online)
        {
            var link = new TextLink { Text = sync.Pending > 0 ? "Sync now" : "Refresh", FontSize = 13.5, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0) };
            link.Clicked += (s, e) => syncNow();
            ToolTipProperties.SetText(link, "Reload the dashboard numbers");
            content.Children.Add(link);
        }
        return new Border
        {
            HeightRequest = 36,
            Padding = new Thickness(14, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = ThemeResources.Color(background),
            VerticalOptions = LayoutOptions.Center,
            Content = content
        };
    }

    // Quick actions -------------------------------------------------------------------------------------------------

    public static View QuickActions(IEnumerable<QuickAction> actions)
    {
        var wrap = new WrapLayout();
        foreach (var a in actions)
        {
            var button = new IconTextButton { Icon = a.Icon, Text = a.Text, Outline = !a.Primary, Margin = new Thickness(0, 0, 10, 10) };
            button.Clicked += (s, e) => a.OnTap();
            wrap.Children.Add(button);
        }
        return wrap;
    }

    // Stat cards -------------------------------------------------------------------------------------------------------

    /// <summary>Icon circle, big number, gray label and an optional comparison line; the whole card opens the module.</summary>
    public static View StatCard(string icon, string circleKey, string iconColorKey, string value, string label,
        string sub = null, string subColorKey = "Muted", Action onTap = null)
    {
        var grid = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new IconBadge { Icon = icon, Size = 44, IconSize = 22, CircleColor = ThemeResources.Color(circleKey), IconColor = ThemeResources.Color(iconColorKey), VerticalOptions = LayoutOptions.Center }, 0, 0);
        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = value, FontFamily = "NunitoExtraBold", FontSize = (value?.Length ?? 0) > 11 ? 20 : (value?.Length ?? 0) > 8 ? 22 : 26, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.NoWrap },
                new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap }
            }
        };
        if (!string.IsNullOrEmpty(sub))
            text.Children.Add(new Label { Text = sub, FontSize = 12.5, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color(subColorKey), LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Add(text, 1, 0);
        var card = new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(18), Content = grid };
        if (onTap != null) Clickable(card, onTap, label);
        return card;
    }

    /// <summary>Hover border #9FD4E3 and a hand cursor; a tap runs <paramref name="onTap"/>.</summary>
    public static void Clickable(Border card, Action onTap, string description)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        card.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => card.Stroke = ThemeResources.Color("CardHoverBorder");
        hover.PointerExited += (s, e) => card.Stroke = ThemeResources.Color("BorderColor");
        card.GestureRecognizers.Add(hover);
        HandCursor.Apply(card);
        SemanticProperties.SetDescription(card, description);
    }

    // List cards ------------------------------------------------------------------------------------------------------

    /// <summary>Icon + title, "View all →" on the right, and the rows (or the empty text).</summary>
    public static Border ListCard(string icon, string title, IReadOnlyList<View> rows, string emptyText, Action viewAll = null, string emptyIcon = "circle-check")
    {
        var head = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center }, 0, 0);
        head.Add(new Label { Text = title, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        if (viewAll != null)
        {
            var link = new TextLink { Text = "View all →", FontSize = 14, VerticalOptions = LayoutOptions.Center };
            link.StyleClass = new[] { "link-accent" };
            link.Clicked += (s, e) => viewAll();
            head.Add(link, 2, 0);
        }
        var stack = new VerticalStackLayout { Spacing = 0, Children = { head } };
        stack.Children.Add(new BoxView { HeightRequest = 12, Color = Colors.Transparent });
        if (rows.Count == 0)
        {
            var empty = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            empty.Add(new LucideIcon { Icon = emptyIcon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0) }, 0, 0);
            empty.Add(new Label { Text = emptyText, FontSize = 14, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap }, 1, 0);
            stack.Children.Add(empty);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) stack.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            stack.Children.Add(rows[i]);
        }
        return new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20), VerticalOptions = LayoutOptions.Fill, Content = stack };
    }

    /// <summary>A 56px list row: left content (two lines) and right content (pill / amount / button).</summary>
    public static Grid Row(View left, View right = null, Action onTap = null)
    {
        var grid = new Grid { MinimumHeightRequest = 56, Padding = new Thickness(0, 8), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(left, 0, 0);
        if (right != null) grid.Add(right, 1, 0);
        if (onTap != null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (right != null && e.GetPosition(grid) is Point p && p.X > grid.Width - right.Width - 6) return;
                onTap();
            };
            grid.GestureRecognizers.Add(tap);
            HandCursor.Apply(grid);
        }
        return grid;
    }

    public static View TwoLines(string first, string second, bool bold = true, View leading = null)
    {
        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = first, FontFamily = bold ? "DMSansBold" : "DMSans", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = second ?? string.Empty, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, IsVisible = !string.IsNullOrEmpty(second) }
            }
        };
        if (leading == null) return text;
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(leading, 0, 0);
        grid.Add(text, 1, 0);
        return grid;
    }

    /// <summary>A stock-alert row: medicine, "Stock: 18 Tablet" (or the expiry), and its status pill.</summary>
    public static View StockRow(Medicine m, DateTime today, Action onTap)
    {
        var status = StockStatusHelper.StatusOf(m, today);
        string second = status switch
        {
            StockStatus.ExpiringSoon => "Expires " + m.ExpiryDate?.ToString("MMM dd"),
            StockStatus.Expired => "Expired " + m.ExpiryDate?.ToString("MMM dd"),
            _ => "Stock: " + m.StockQty.ToString("N0") + " " + m.Unit
        };
        return Row(TwoLines(m.MedicineName, second, leading: PharmacyKit.MedicineTile(m.IsVaccine, 34)), PharmacyKit.StatusPill(status), onTap);
    }

    // Layout --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Columns that become rows when the page is narrow: the cards sit side by side at <paramref name="weights"/>
    /// (e.g. 3:2) from <paramref name="minWidth"/> up, otherwise stacked.
    /// </summary>
    public static Grid Columns(double minWidth, double[] weights, params View[] cards)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        void Arrange(bool wide)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (wide)
            {
                foreach (double w in weights) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(w, GridUnitType.Star)));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                for (int i = 0; i < cards.Length; i++) grid.Add(cards[i], i, 0);
            }
            else
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                for (int i = 0; i < cards.Length; i++)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    grid.Add(cards[i], 0, i);
                }
            }
        }
        bool? current = null;
        grid.SizeChanged += (s, e) =>
        {
            if (grid.Width <= 0) return;
            bool wide = grid.Width >= minWidth;
            if (current == wide) return;
            current = wide;
            Arrange(wide);
        };
        Arrange(true);
        current = true;
        return grid;
    }

    /// <summary>Stat cards in a grid: <paramref name="wideColumns"/> per row when there's room, fewer when narrow.</summary>
    public static Grid StatGrid(IReadOnlyList<View> cards, int wideColumns)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        int current = 0;
        void Place(double width)
        {
            int columns = width <= 0 ? wideColumns
                : wideColumns >= 6 ? (width >= 1300 ? 6 : width >= 700 ? 3 : 2)
                : wideColumns >= 5 ? (width >= 1150 ? 5 : width >= 700 ? 3 : 2)
                : (width >= 900 ? wideColumns : 2);
            if (columns == current) return;
            current = columns;
            PageKit.PlaceInGrid(grid, cards, columns);
        }
        grid.SizeChanged += (s, e) => Place(grid.Width);
        Place(0);
        return grid;
    }

    /// <summary>The page body: padding, spacing, scrolling (focus doesn't scroll it).</summary>
    public static ScrollView Page(params View[] sections)
    {
        var stack = new VerticalStackLayout { Padding = new Thickness(40, 32, 40, 40), Spacing = 20 };
        foreach (var s in sections) if (s != null) stack.Children.Add(s);
        var scroll = new ScrollView { Content = stack };
        ScrollFocus.DontScrollOnFocus(scroll);
        // Cards re-flow when the width is known; WinUI's scroll anchoring would then move the page. Turn it off.
        scroll.HandlerChanged += (s, e) =>
        {
#if WINDOWS
            if (scroll.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer viewer) viewer.VerticalAnchorRatio = double.NaN;
#endif
        };
        return scroll;
    }

    /// <summary>Small action button for list rows (orange filled or navy outline).</summary>
    public static View SmallButton(string text, string icon, bool filled, Action onTap) => PharmacyKit.SmallButton(text, icon, filled, onTap);
}
