using Microsoft.Maui.Graphics;

namespace VetCare.Views.Controls;

/// <summary>
/// Draws a Lucide outline icon (stroke 1.75, round caps/joins) at any size.
/// Set <see cref="View.WidthRequest"/> / <see cref="View.HeightRequest"/> to size it.
/// </summary>
public sealed class LucideIcon : GraphicsView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(LucideIcon), null, propertyChanged: Redraw);

    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(nameof(StrokeColor), typeof(Color), typeof(LucideIcon), Colors.Black, propertyChanged: Redraw);

    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(nameof(StrokeWidth), typeof(float), typeof(LucideIcon), 1.75f, propertyChanged: Redraw);

    public LucideIcon()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        WidthRequest = 24;
        HeightRequest = 24;
        Drawable = new IconDrawable(this);
    }

    /// <summary>Lucide icon name, e.g. "paw-print".</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    /// <summary>Stroke width in 24×24 icon units (Lucide default here: 1.75).</summary>
    public float StrokeWidth
    {
        get => (float)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((LucideIcon)bindable).Invalidate();

    private sealed class IconDrawable : IDrawable
    {
        private const float ViewBox = 24f;
        private readonly LucideIcon _owner;

        public IconDrawable(LucideIcon owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var path = LucideIconData.Get(_owner.Icon);
            float width = (float)_owner.Width, height = (float)_owner.Height;
            if (path == null || width <= 0 || height <= 0) return;

            float size = Math.Min(width, height);
            float scale = size / ViewBox;

            canvas.SaveState();
            canvas.Antialias = true;
            canvas.Translate((width - size) / 2f, (height - size) / 2f);
            canvas.Scale(scale, scale);
            canvas.StrokeColor = _owner.StrokeColor;
            canvas.StrokeSize = _owner.StrokeWidth;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(path);
            canvas.RestoreState();
        }
    }
}
