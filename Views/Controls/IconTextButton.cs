using Microsoft.Maui.Controls.Shapes;

namespace VetCare.Views.Controls;

/// <summary>
/// Orange page-action button with an icon before the text (e.g. "Add user", "Book appointment"):
/// 44px tall, 6px radius, white bold text, darker on hover, hand cursor.
/// </summary>
public sealed class IconTextButton : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(IconTextButton), string.Empty,
            propertyChanged: (b, o, n) => ((IconTextButton)b)._label.Text = (string)n);

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconTextButton), null,
            propertyChanged: (b, o, n) => ((IconTextButton)b)._icon.Icon = (string)n);

    /// <summary>True: white with a navy border and navy text (secondary action, e.g. "Walk-in").</summary>
    public static readonly BindableProperty OutlineProperty =
        BindableProperty.Create(nameof(Outline), typeof(bool), typeof(IconTextButton), false,
            propertyChanged: (b, o, n) => ((IconTextButton)b).Apply());

    private readonly Border _box;
    private readonly LucideIcon _icon;
    private readonly Label _label;
    private bool _hover;

    public IconTextButton()
    {
        _icon = new LucideIcon { StrokeColor = Colors.White, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center };
        _label = new Label { FontFamily = "DMSansBold", FontSize = 15, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };

        _box = new Border
        {
            HeightRequest = 44,
            Padding = new Thickness(20, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { _icon, _label }
            }
        };
        Content = _box;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => { if (IsEnabled) Clicked?.Invoke(this, EventArgs.Empty); };
        _box.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => { _hover = true; Apply(); };
        pointer.PointerExited += (s, e) => { _hover = false; Apply(); };
        _box.GestureRecognizers.Add(pointer);

        HandCursor.Apply(_box);
        PropertyChanged += (s, e) => { if (e.PropertyName == nameof(IsEnabled)) Apply(); };
        Apply();
    }

    public event EventHandler Clicked;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Lucide icon name, e.g. "user-plus".</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool Outline
    {
        get => (bool)GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    private void Apply()
    {
        if (_box == null) return;
        if (Outline)
        {
            _box.BackgroundColor = ThemeResources.Color(_hover && IsEnabled ? "RowHover" : "Surface");
            _box.Stroke = ThemeResources.Color("Navy");
            _box.StrokeThickness = 1;
            _icon.StrokeColor = ThemeResources.Color("Navy");
            _label.TextColor = ThemeResources.Color("Navy");
            _box.Opacity = IsEnabled ? 1 : 0.6;
            SemanticProperties.SetDescription(this, Text);
            return;
        }

        _box.StrokeThickness = 0;
        _icon.StrokeColor = Colors.White;
        _label.TextColor = Colors.White;
        _box.BackgroundColor = ThemeResources.Color(_hover && IsEnabled ? "OrangeHover" : "Orange");
        _box.Opacity = IsEnabled ? 1 : 0.6;
        SemanticProperties.SetDescription(this, Text);
    }
}
