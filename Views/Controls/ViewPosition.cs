namespace VetCare.Views.Controls;

/// <summary>Finds where a view sits on screen relative to one of its ancestors (scroll offsets included).</summary>
internal static class ViewPosition
{
    /// <summary>Centre of <paramref name="element"/> in the coordinates of <paramref name="ancestor"/>.</summary>
    public static Point CenterIn(VisualElement element, Element ancestor)
    {
        double x = element.Width / 2, y = element.Height / 2;

        Element current = element;
        while (current is VisualElement view && current != ancestor)
        {
            x += view.X + view.TranslationX;
            y += view.Y + view.TranslationY;

            if (view.Parent is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }

            current = view.Parent;
        }

        return new Point(x, y);
    }
}
