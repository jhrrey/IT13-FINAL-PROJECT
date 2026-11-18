using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

/// <summary>
/// One cell of the services grid. Draws its own right and bottom border lines
/// (the grid draws the outer top and left lines) so neighbouring cells share 1px lines.
/// </summary>
public sealed class ServiceCardView : ContentView
{
    private readonly Grid _layout;

    public ServiceCardView(ServiceItem item, string linkLabel)
    {
        var icon = new IconBadge
        {
            Icon = item.Icon,
            Size = 50,
            IconColor = ThemeResources.Color(item.IconTone == AccentTone.Orange ? "Orange" : "Navy"),
            CircleColor = ThemeResources.Color(item.Badge switch
            {
                BadgeTone.Mint => "Mint",
                BadgeTone.PaleBlue => "PaleBlue",
                _ => "Cyan"
            }),
            Margin = new Thickness(0, 0, 0, 26)
        };

        var title = new Label { Text = item.Title, Margin = new Thickness(0, 0, 0, 12) };
        title.StyleClass = new[] { "card-title" };

        var description = new Label { Text = item.Description, Margin = new Thickness(0, 0, 0, 24) };
        description.StyleClass = new[] { "body" };

        var link = new TextLink { Text = linkLabel + " →", HorizontalOptions = LayoutOptions.Start };
        link.StyleClass = new[] { "link-accent" };
        link.Clicked += (s, e) => LearnMoreClicked?.Invoke(this, EventArgs.Empty);

        _layout = new Grid
        {
            Padding = 36,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star), // description grows so links line up across a row
                new RowDefinition(GridLength.Auto)
            }
        };
        _layout.Add(icon, 0, 0);
        _layout.Add(title, 0, 1);
        _layout.Add(description, 0, 2);
        _layout.Add(link, 0, 3);

        var border = ThemeResources.Color("BorderColor");
        var right = new BoxView { Color = border, WidthRequest = 1, HorizontalOptions = LayoutOptions.End };
        var bottom = new BoxView { Color = border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };

        var root = new Grid { Children = { _layout, right, bottom } };

        // Faint light-cyan background while hovered.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => root.BackgroundColor = ThemeResources.Color("CardHover");
        pointer.PointerExited += (s, e) =>
        {
            // Exits bubble up from child elements; only clear when the pointer really left the card.
            var position = e.GetPosition(root);
            if (position is Point p && p.X > 0 && p.Y > 0 && p.X < root.Width && p.Y < root.Height) return;
            root.BackgroundColor = Colors.Transparent;
        };
        root.GestureRecognizers.Add(pointer);

        Content = root;
    }

    public event EventHandler LearnMoreClicked;

    public void SetCompact(bool compact) =>
        _layout.Padding = compact ? new Thickness(24, 28) : new Thickness(36);
}
