using Microsoft.Maui.Controls.Shapes;

namespace VetCare.Views.Controls;

/// <summary>
/// The header of a workspace page: the module's icon in a cyan tile, centred on the title
/// (Nunito 800, 26px), with the subtitle under the title. Used by every module page so they match.
/// </summary>
public sealed class PageHeader : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(PageHeader), null,
            propertyChanged: (b, o, n) => ((PageHeader)b)._icon.Icon = (string)n);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(PageHeader), string.Empty,
            propertyChanged: (b, o, n) => ((PageHeader)b)._title.Text = (string)n);

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(PageHeader), string.Empty,
            propertyChanged: (b, o, n) =>
            {
                var header = (PageHeader)b;
                header._subtitle.Text = (string)n;
                header._subtitle.IsVisible = !string.IsNullOrEmpty((string)n);
            });

    private readonly LucideIcon _icon;
    private readonly Label _title;
    private readonly Label _subtitle;

    public PageHeader()
    {
        _icon = new LucideIcon
        {
            StrokeColor = ThemeResources.Color("Navy"),
            WidthRequest = 22,
            HeightRequest = 22,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var tile = new Border
        {
            WidthRequest = 46,
            HeightRequest = 46,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color("Cyan"),
            VerticalOptions = LayoutOptions.Center,
            Content = _icon
        };

        _title = new Label
        {
            FontFamily = "NunitoExtraBold",
            FontSize = 26,
            CharacterSpacing = -0.25,
            TextColor = ThemeResources.Color("Navy"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        _subtitle = new Label
        {
            FontSize = 15,
            TextColor = ThemeResources.Color("BodyText"),
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = false
        };

        // The tile shares the first row with the title, centred on it; the subtitle sits under the title only.
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 16
        };
        grid.Add(tile, 0, 0);
        grid.Add(_title, 1, 0);
        grid.Add(_subtitle, 1, 1);
        Content = grid;
    }

    /// <summary>Lucide icon name (the module's sidebar icon).</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
