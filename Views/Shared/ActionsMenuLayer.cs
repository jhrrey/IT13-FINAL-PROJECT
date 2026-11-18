using Microsoft.Maui.Controls.Shapes;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>One entry of a row's actions menu.</summary>
public sealed record MenuEntry(string Text, string Icon, Action OnSelected, bool Danger = false, bool Enabled = true, string DisabledHint = null);

/// <summary>
/// The three-dot actions menu: a small card that opens next to the clicked button.
/// Place it last in the page's root Grid so it draws above the content; clicking outside closes it.
/// </summary>
public sealed class ActionsMenuLayer : ContentView
{
    private const double MenuWidth = 210;
    private readonly Border _menu;
    private readonly VerticalStackLayout _items;

    public ActionsMenuLayer()
    {
        IsVisible = false;

        var dismiss = new BoxView { Color = Colors.Transparent };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Hide();
        dismiss.GestureRecognizers.Add(tap);

        _items = new VerticalStackLayout { Spacing = 2 };
        _menu = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(6),
            WidthRequest = MenuWidth,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = _items
        };

        Content = new Grid { Children = { dismiss, _menu } };
    }

    /// <summary>Opens the menu below (or above, near the bottom) <paramref name="anchor"/>.</summary>
    public void Show(View anchor, IReadOnlyList<MenuEntry> entries)
    {
        _items.Children.Clear();
        foreach (var entry in entries) _items.Children.Add(Item(entry));

        var root = Parent as VisualElement ?? this;
        var center = ViewPosition.CenterIn(anchor, root);
        double menuHeight = entries.Count * 40 + 12;
        double top = center.Y + 20;
        if (top + menuHeight > root.Height - 8) top = Math.Max(8, center.Y - 20 - menuHeight);
        double left = Math.Max(8, center.X - MenuWidth + 17);

        _menu.Margin = new Thickness(left, top, 0, 0);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    private View Item(MenuEntry entry)
    {
        var color = ThemeResources.Color(!entry.Enabled ? "Muted" : entry.Danger ? "Danger" : "Navy");
        var item = new Border
        {
            HeightRequest = 38,
            Padding = new Thickness(10, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = Colors.Transparent,
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = entry.Icon, StrokeColor = color, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = entry.Text, FontSize = 14, FontFamily = "DMSansMedium", TextColor = color, VerticalOptions = LayoutOptions.Center }
                }
            }
        };

        if (!entry.Enabled)
        {
            item.Opacity = 0.6;
            if (entry.DisabledHint != null) ToolTipProperties.SetText(item, entry.DisabledHint);
            return item;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            Hide();
            entry.OnSelected();
        };
        item.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => item.BackgroundColor = ThemeResources.Color(entry.Danger ? "DangerSoft" : "RowHover");
        hover.PointerExited += (s, e) => item.BackgroundColor = Colors.Transparent;
        item.GestureRecognizers.Add(hover);
        HandCursor.Apply(item);
        return item;
    }
}
