using Microsoft.Maui.Controls.Shapes;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>A table column: header text and fixed width.</summary>
public readonly record struct TableColumn(string Title, double Width);

/// <summary>
/// Building blocks shared by the workspace module pages (Users & roles, Owners, …):
/// summary cards, table rows, avatars, icon buttons and empty/error states.
/// </summary>
public static class PageKit
{
    // Cards ----------------------------------------------------------------------------------

    /// <summary>Summary card: 44px icon circle, big number (28px Nunito 800) and a gray label.</summary>
    public static View SummaryCard(string icon, string circleKey, string value, string label, string iconColorKey = "Navy")
    {
        // A Grid (not a horizontal stack) so long labels wrap inside the card instead of being cut off.
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 14
        };
        grid.Add(new IconBadge
        {
            Icon = icon,
            Size = 44,
            IconSize = 22,
            CircleColor = ThemeResources.Color(circleKey),
            IconColor = ThemeResources.Color(iconColorKey),
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = value, FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap }
            }
        }, 1, 0);

        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(18),
            Content = grid
        };
    }

    /// <summary>Lays <paramref name="items"/> out in a grid with <paramref name="columns"/> equal columns.</summary>
    public static void PlaceInGrid(Grid grid, IReadOnlyList<View> items, int columns)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        int rows = (items.Count + columns - 1) / columns;
        for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < items.Count; i++) grid.Add(items[i], i % columns, i / columns);
    }

    /// <summary>Re-flows a grid's existing children into <paramref name="columns"/> columns.</summary>
    public static void Reflow(Grid grid, int columns)
    {
        var items = grid.Children.OfType<View>().ToList();
        if (items.Count > 0) PlaceInGrid(grid, items, columns);
    }

    // Tables ---------------------------------------------------------------------------------

    public static Grid RowGrid(IReadOnlyList<TableColumn> columns, double height)
    {
        var grid = new Grid { HeightRequest = height, Padding = new Thickness(20, 0), ColumnSpacing = 0 };
        foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(column.Width)));
        return grid;
    }

    /// <summary>Header row: uppercase 12px bold labels on the input background, with a bottom line.</summary>
    public static View HeaderRow(IReadOnlyList<TableColumn> columns)
    {
        var grid = RowGrid(columns, 44);
        grid.BackgroundColor = ThemeResources.Color("InputBackground");
        for (int i = 0; i < columns.Count; i++)
        {
            grid.Add(new Label
            {
                Text = columns[i].Title.ToUpperInvariant(),
                FontFamily = "DMSansBold",
                FontSize = 12,
                CharacterSpacing = 0.67, // 0.5px at 12px (see the letter-spacing note in app.css)
                TextColor = ThemeResources.Color("SectionLabel"),
                VerticalOptions = LayoutOptions.Center
            }, i, 0);
        }

        return WithBottomLine(grid);
    }

    public static View WithBottomLine(View content) => new Grid
    {
        Children =
        {
            content,
            new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End }
        }
    };

    /// <summary>Background #F2F8FB while the pointer is over the row.</summary>
    public static void AddRowHover(Grid row)
    {
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => row.BackgroundColor = ThemeResources.Color("RowHover");
        hover.PointerExited += (s, e) =>
        {
            // Exits bubble up from child elements; only clear when the pointer really left the row.
            var position = e.GetPosition(row);
            if (position is Point p && p.X > 0 && p.Y > 0 && p.X < row.Width && p.Y < row.Height) return;
            row.BackgroundColor = Colors.Transparent;
        };
        row.GestureRecognizers.Add(hover);
    }

    public static Label Cell(string text, bool muted = false) => new()
    {
        Text = text,
        FontSize = muted ? 13 : 14,
        TextColor = ThemeResources.Color(muted ? "Muted" : "Navy"),
        LineBreakMode = LineBreakMode.TailTruncation,
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 0, 12, 0)
    };

    /// <summary>Rounded-square avatar with initials on light cyan.</summary>
    public static View Avatar(string initials, double size = 40, double radius = 8, double fontSize = 14) => new Border
    {
        WidthRequest = size,
        HeightRequest = size,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = radius },
        BackgroundColor = ThemeResources.Color("Cyan"),
        VerticalOptions = LayoutOptions.Center,
        Content = new Label
        {
            Text = initials,
            FontFamily = "DMSansBold",
            FontSize = fontSize,
            TextColor = ThemeResources.Color("Navy"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }
    };

    /// <summary>Avatar + bold name + gray second line (the first column of a table).</summary>
    public static View PersonCell(string initials, string name, string secondLine) => new HorizontalStackLayout
    {
        Spacing = 12,
        Children =
        {
            Avatar(initials),
            new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = name, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 220 },
                    new Label { Text = secondLine ?? string.Empty, FontSize = 13, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 220, IsVisible = !string.IsNullOrEmpty(secondLine) }
                }
            }
        }
    };

    /// <summary>34px square icon button (e.g. the three-dot actions button).</summary>
    public static Border IconButton(string icon, string description, Action onTap)
    {
        var button = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new LucideIcon
            {
                Icon = icon,
                StrokeColor = ThemeResources.Color("NavyMuted"),
                WidthRequest = 18,
                HeightRequest = 18,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        SemanticProperties.SetDescription(button, description);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        button.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => button.BackgroundColor = ThemeResources.Color("Cyan");
        hover.PointerExited += (s, e) => button.BackgroundColor = Colors.Transparent;
        button.GestureRecognizers.Add(hover);
        HandCursor.Apply(button);
        return button;
    }

    // States ---------------------------------------------------------------------------------

    /// <summary>A centered message, optionally with an icon circle (e.g. loading or an error).</summary>
    public static View StateMessage(string text, string icon = null, string circleKey = null, string iconColorKey = "Danger")
    {
        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(24, 40), HorizontalOptions = LayoutOptions.Center };
        if (icon != null)
            stack.Children.Add(new IconBadge
            {
                Icon = icon,
                Size = 44,
                IconSize = 22,
                CircleColor = ThemeResources.Color(circleKey),
                IconColor = ThemeResources.Color(iconColorKey),
                HorizontalOptions = LayoutOptions.Center
            });
        stack.Children.Add(new Label
        {
            Text = text,
            FontSize = 15,
            TextColor = ThemeResources.Color("BodyText"),
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center
        });
        return stack;
    }

    /// <summary>Empty state: icon in a cyan circle, title, optional subtext and optional orange button.</summary>
    public static View EmptyState(string icon, string title, string subtitle = null, string buttonText = null, Action onButton = null,
        string buttonIcon = "plus")
    {
        var stack = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
        stack.Children.Add(new IconBadge
        {
            Icon = icon,
            Size = 56,
            IconSize = 26,
            CircleColor = ThemeResources.Color("Cyan"),
            IconColor = ThemeResources.Color("Navy"),
            HorizontalOptions = LayoutOptions.Center
        });
        stack.Children.Add(new Label
        {
            Text = title,
            FontFamily = "DMSansBold",
            FontSize = 18,
            TextColor = ThemeResources.Color("Navy"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });
        if (!string.IsNullOrEmpty(subtitle))
            stack.Children.Add(new Label
            {
                Text = subtitle,
                FontSize = 14,
                TextColor = ThemeResources.Color("BodyText"),
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            });
        if (buttonText != null && onButton != null)
        {
            var button = new IconTextButton
            {
                Icon = buttonIcon,
                Text = buttonText,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            button.Clicked += (s, e) => onButton();
            stack.Children.Add(button);
        }
        return stack;
    }
}
