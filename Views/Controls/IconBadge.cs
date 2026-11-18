using Microsoft.Maui.Controls.Shapes;

namespace VetCare.Views.Controls;

/// <summary>A Lucide icon centred in a filled and/or outlined circle.</summary>
public sealed class IconBadge : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconBadge), null, propertyChanged: Refresh);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(IconBadge), Colors.Black, propertyChanged: Refresh);

    public static readonly BindableProperty CircleColorProperty =
        BindableProperty.Create(nameof(CircleColor), typeof(Color), typeof(IconBadge), null, propertyChanged: Refresh);

    public static readonly BindableProperty OutlineColorProperty =
        BindableProperty.Create(nameof(OutlineColor), typeof(Color), typeof(IconBadge), null, propertyChanged: Refresh);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(IconBadge), 50d, propertyChanged: Refresh);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconBadge), 24d, propertyChanged: Refresh);

    private readonly Border _circle;
    private readonly LucideIcon _icon;

    public IconBadge()
    {
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;

        _icon = new LucideIcon
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _circle = new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            Padding = 0,
            Content = _icon
        };

        Content = _circle;
        Apply();
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    /// <summary>Circle fill; null for none.</summary>
    public Color CircleColor
    {
        get => (Color)GetValue(CircleColorProperty);
        set => SetValue(CircleColorProperty, value);
    }

    /// <summary>1px circle outline; null for none.</summary>
    public Color OutlineColor
    {
        get => (Color)GetValue(OutlineColorProperty);
        set => SetValue(OutlineColorProperty, value);
    }

    /// <summary>Circle diameter.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private static void Refresh(BindableObject bindable, object oldValue, object newValue) =>
        ((IconBadge)bindable).Apply();

    private void Apply()
    {
        if (_circle == null) return;

        _circle.WidthRequest = Size;
        _circle.HeightRequest = Size;
        _circle.BackgroundColor = CircleColor ?? Colors.Transparent;
        _circle.Stroke = OutlineColor == null ? null : new SolidColorBrush(OutlineColor);
        _circle.StrokeThickness = OutlineColor == null ? 0 : 1;

        _icon.Icon = Icon;
        _icon.StrokeColor = IconColor;
        _icon.WidthRequest = IconSize;
        _icon.HeightRequest = IconSize;
    }
}
