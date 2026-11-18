using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>Building blocks shared by the Pharmacy pages and panels (Medicines &amp; stock, Prescriptions).</summary>
public static class PharmacyKit
{
    /// <summary>40px rounded tile: syringe on mint for vaccines, pill on cyan for everything else.</summary>
    public static View MedicineTile(bool vaccine, double size = 40) => new Border
    {
        WidthRequest = size,
        HeightRequest = size,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        BackgroundColor = ThemeResources.Color(vaccine ? "Mint" : "Cyan"),
        VerticalOptions = LayoutOptions.Center,
        Content = new LucideIcon
        {
            Icon = vaccine ? "syringe" : "pill",
            StrokeColor = ThemeResources.Color(vaccine ? "CompletedText" : "Navy"),
            WidthRequest = size * 0.48,
            HeightRequest = size * 0.48,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }
    };

    public static View StatusPill(StockStatus status)
    {
        var (text, background, color) = StockStatusHelper.Style(status);
        return Badges.Pill(text, color, background);
    }

    public static View TransactionPill(string type)
    {
        var (background, color) = StockStatusHelper.TransactionStyle(type);
        return Badges.Pill(type, color, background);
    }

    /// <summary>"+100" in green or "−14" in red.</summary>
    public static Label SignedQuantity(int quantity, string text) => new()
    {
        Text = text,
        FontFamily = "DMSansBold",
        FontSize = 14,
        TextColor = ThemeResources.Color(quantity >= 0 ? "Success" : "Danger"),
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>The stock number with a thin 80px bar under it (green / amber / red).</summary>
    public static View StockCell(Medicine m)
    {
        var (fraction, colorKey) = StockStatusHelper.Bar(m);
        var bar = new Grid
        {
            WidthRequest = 80,
            HeightRequest = 5,
            HorizontalOptions = LayoutOptions.Start,
            Children =
            {
                new BoxView { Color = ThemeResources.Color("LightGray"), CornerRadius = 3 },
                new BoxView { Color = ThemeResources.Color(colorKey), CornerRadius = 3, WidthRequest = 80 * fraction, HorizontalOptions = LayoutOptions.Start, IsVisible = fraction > 0 }
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = m.StockQty.ToString("N0"), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                bar
            }
        };
    }

    /// <summary>The expiry date (struck through and gray when expired), or "—".</summary>
    public static Label ExpiryLabel(Medicine m) => new()
    {
        Text = m.ExpiryDate?.ToString("MMM dd, yyyy") ?? "—",
        FontSize = 14,
        TextColor = ThemeResources.Color(m.IsExpired ? "Muted" : "Navy"),
        TextDecorations = m.IsExpired ? TextDecorations.Strikethrough : TextDecorations.None,
        VerticalOptions = LayoutOptions.Center
    };

    public static string Peso(decimal amount) => CurrencyHelper.Peso(amount);

    /// <summary>Small (34px) button: orange filled, or navy outline.</summary>
    public static Border SmallButton(string text, string icon, bool filled, Action onTap, string description = null)
    {
        string normal = filled ? "Orange" : "Surface", hover = filled ? "OrangeHover" : "RowHover";
        var color = filled ? Colors.White : ThemeResources.Color("Navy");
        var button = new Border
        {
            HeightRequest = 34,
            Padding = new Thickness(12, 0),
            StrokeThickness = filled ? 0 : 1,
            Stroke = ThemeResources.Color("Navy"),
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = ThemeResources.Color(normal),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = color, WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center, IsVisible = icon != null },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13.5, TextColor = color, VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        button.GestureRecognizers.Add(tap);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => button.BackgroundColor = ThemeResources.Color(hover);
        pointer.PointerExited += (s, e) => button.BackgroundColor = ThemeResources.Color(normal);
        button.GestureRecognizers.Add(pointer);
        HandCursor.Apply(button);
        SemanticProperties.SetDescription(button, description ?? text);
        return button;
    }

    /// <summary>A gray disabled button ("Not enough stock" style).</summary>
    public static Border DisabledButton(string text, string icon) => new()
    {
        HeightRequest = 34,
        Padding = new Thickness(12, 0),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 6 },
        BackgroundColor = ThemeResources.Color("LightGray"),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Center,
        Content = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        }
    };

    /// <summary>A colored info box (cyan / amber / red) with one line of text.</summary>
    public static (Border Box, Label Text) InfoBox(string backgroundKey, string textColorKey)
    {
        var text = new Label { FontFamily = "DMSansMedium", FontSize = 14.5, TextColor = ThemeResources.Color(textColorKey), LineBreakMode = LineBreakMode.WordWrap };
        var box = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color(backgroundKey),
            Content = text
        };
        return (box, text);
    }

    // Side-panel frame ---------------------------------------------------------------------------------

    /// <summary>Header (title, optional subtitle, close), scrolling body and a Cancel / action footer.</summary>
    public static View PanelFrame(string title, string subtitle, View body, Action close, HoverButton action, string cancelText = "Cancel")
    {
        var titles = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = title, FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy") } }
        };
        if (!string.IsNullOrEmpty(subtitle))
            titles.Children.Add(new Label { Text = subtitle, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.TailTruncation });
        var header = new Grid { Padding = new Thickness(24, 18), ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(titles, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", close), 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        var scroll = new ScrollView { Content = body };
        ScrollFocus.DontScrollOnFocusWhileOpening(scroll);
        layout.Add(scroll, 0, 1);

        if (action != null)
        {
            var cancel = new HoverButton { Text = cancelText, Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
            cancel.Clicked += (s, e) => close();
            var footer = new Grid { Padding = new Thickness(24, 16), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            footer.Add(cancel, 0, 0);
            footer.Add(action, 1, 0);
            layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        }
        return layout;
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };

    // Form fields ----------------------------------------------------------------------------------------

    public static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };
        return label;
    }

    public static Label Hint(string text) => new() { Text = text, FontSize = 13, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(0, 6, 0, 0) };

    public static Label ErrorLabel()
    {
        var label = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        label.StyleClass = new[] { "field-error" };
        return label;
    }

    public static void SetError(Label error, string message, Border box)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        error.Text = message ?? string.Empty;
        error.IsVisible = invalid;
        if (box != null) box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }

    /// <summary>An input box with an Entry (and an optional "₱" prefix).</summary>
    public static (Border Box, Entry Entry) TextInput(string placeholder = null, Keyboard keyboard = null, int maxLength = 100, string prefix = null)
    {
        var entry = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Placeholder = placeholder, Keyboard = keyboard ?? Keyboard.Default, MaxLength = maxLength };
        View content = entry;
        if (prefix != null)
        {
            var grid = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            grid.Add(new Label { Text = prefix, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(entry, 1, 0);
            content = grid;
        }
        return (new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = content }, entry);
    }

    /// <summary>Whole number 0 or more from the text, or null.</summary>
    public static int? ParseCount(string text) =>
        int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;
}
