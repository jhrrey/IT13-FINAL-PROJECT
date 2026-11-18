using VetCare.Models;

namespace VetCare.Views.Sections;

/// <summary>
/// A full-width band of the landing page. Sections only display content and report
/// what the user asked for (via <see cref="NavigateRequested"/>); controllers decide what happens.
/// </summary>
public class SectionView : ContentView
{
    public const double MaxContentWidth = 1200;

    public event EventHandler<PageNavigateEventArgs> NavigateRequested;

    /// <summary>Re-flows the section for the current window width (responsive breakpoints).</summary>
    public virtual void ApplyWidth(double width)
    {
    }

    /// <summary>Two-column layouts collapse to one column below this width.</summary>
    protected static bool IsStacked(double width) => width < 900;

    /// <summary>Phone-sized widths.</summary>
    protected static bool IsCompact(double width) => width < 640;

    /// <summary>Small desktop: big headings step down a size.</summary>
    protected static bool IsNarrow(double width) => width < 1100;

    /// <summary>Width of the centred content column: max 1200 with 24px (16px compact) gutters.</summary>
    protected static double ContentWidth(double width)
    {
        double gutter = IsCompact(width) ? 16 : 24;
        return Math.Max(0, Math.Min(MaxContentWidth, width - gutter * 2));
    }

    protected void Navigate(NavigationTarget target) =>
        NavigateRequested?.Invoke(this, new PageNavigateEventArgs(target));
}
