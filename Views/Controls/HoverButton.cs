namespace VetCare.Views.Controls;

/// <summary>
/// Button that darkens slightly while the pointer is over it and shows the hand cursor.
/// Orange (#D4421C) hovers to #B83715; set <see cref="HoverBackgroundColor"/> for other colours
/// (otherwise they get about 13% darker). Styled with the button styles in Styles.xaml.
/// </summary>
public class HoverButton : Button
{
    public static readonly BindableProperty HoverBackgroundColorProperty =
        BindableProperty.Create(nameof(HoverBackgroundColor), typeof(Color), typeof(HoverButton));

    private static readonly Color Orange = Color.FromArgb("#D4421C");
    private static readonly Color OrangeHover = Color.FromArgb("#B83715");

    private Color _normal;
    private bool _hovering;

    public HoverButton()
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => SetHover(true);
        pointer.PointerExited += (s, e) => SetHover(false);
        GestureRecognizers.Add(pointer);

        HandCursor.Apply(this);
    }

    /// <summary>Background while hovered. Null = automatic.</summary>
    public Color HoverBackgroundColor
    {
        get => (Color)GetValue(HoverBackgroundColorProperty);
        set => SetValue(HoverBackgroundColorProperty, value);
    }

    private void SetHover(bool hovering)
    {
        if (hovering == _hovering || !IsEnabled) return;
        _hovering = hovering;

        if (hovering)
        {
            _normal = BackgroundColor;
            if (_normal != null) BackgroundColor = HoverBackgroundColor ?? HoverFor(_normal);
        }
        else if (_normal != null)
        {
            BackgroundColor = _normal;
        }
    }

    /// <summary>The design's hover orange, or about 13% darker for any other colour.</summary>
    private static Color HoverFor(Color color) =>
        color.ToArgbHex() == Orange.ToArgbHex()
            ? OrangeHover
            : new Color(color.Red * 0.87f, color.Green * 0.87f, color.Blue * 0.87f, color.Alpha);
}
