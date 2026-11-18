using Microsoft.Maui.Layouts;

namespace VetCare.Views.Controls;

/// <summary>
/// Places children left to right and wraps to a new line when the next one doesn't fit
/// (chips, tags). Measures exactly the lines it uses; the wrapping FlexLayout reserved
/// extra height inside a Grid row. Spacing comes from the children's own margins.
/// </summary>
public sealed class WrapLayout : Layout
{
    protected override ILayoutManager CreateLayoutManager() => new WrapLayoutManager(this);

    private sealed class WrapLayoutManager : ILayoutManager
    {
        private readonly WrapLayout _layout;

        public WrapLayoutManager(WrapLayout layout) => _layout = layout;

        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var padding = _layout.Padding;
            double available = widthConstraint - padding.HorizontalThickness;
            double x = 0, y = 0, lineHeight = 0, widest = 0;

            foreach (var child in _layout)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var size = child.Measure(available, double.PositiveInfinity); // includes the child's margin
                if (x > 0 && x + size.Width > available)
                {
                    y += lineHeight;
                    x = 0;
                    lineHeight = 0;
                }
                x += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
                widest = Math.Max(widest, x);
            }

            return new Size(widest + padding.HorizontalThickness, y + lineHeight + padding.VerticalThickness);
        }

        public Size ArrangeChildren(Rect bounds)
        {
            var padding = _layout.Padding;
            double left = bounds.Left + padding.Left, top = bounds.Top + padding.Top;
            double available = bounds.Width - padding.HorizontalThickness;
            double x = 0, y = 0, lineHeight = 0;

            foreach (var child in _layout)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > available)
                {
                    y += lineHeight;
                    x = 0;
                    lineHeight = 0;
                }
                child.Arrange(new Rect(left + x, top + y, size.Width, size.Height));
                x += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
            }

            return bounds.Size;
        }
    }
}
