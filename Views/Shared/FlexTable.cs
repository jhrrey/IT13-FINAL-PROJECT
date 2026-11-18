using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>A table column: fixed width, or flexible (shares the spare width, never narrower than <see cref="Width"/>).</summary>
public readonly record struct FlexColumn(string Title, double Width, bool Flex = false);

/// <summary>
/// A table whose flexible columns stretch to fill the card; when the card is narrower than the columns need,
/// the table scrolls sideways instead of squeezing them. Header row on the input background, rows with a bottom line.
/// </summary>
public sealed class FlexTable : ContentView
{
    private const double SidePadding = 20;

    private readonly FlexColumn[] _columns;
    private readonly VerticalStackLayout _stack = new();
    private readonly ScrollView _scroll;
    private readonly double _minWidth;

    public FlexTable(params FlexColumn[] columns)
    {
        _columns = columns;
        _minWidth = columns.Sum(c => c.Width) + SidePadding * 2;
        _scroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _stack };
        _scroll.SizeChanged += (s, e) => Fit();
        Content = _scroll;
        Clear();
    }

    /// <summary>Removes the rows (keeps the header).</summary>
    public void Clear()
    {
        _stack.Children.Clear();
        var header = NewRow(44);
        header.BackgroundColor = ThemeResources.Color("InputBackground");
        for (int i = 0; i < _columns.Length; i++)
        {
            header.Add(new Label
            {
                Text = _columns[i].Title.ToUpperInvariant(),
                FontFamily = "DMSansBold",
                FontSize = 12,
                CharacterSpacing = 0.67,
                TextColor = ThemeResources.Color("SectionLabel"),
                LineBreakMode = LineBreakMode.NoWrap,
                VerticalOptions = LayoutOptions.Center
            }, i, 0);
        }
        _stack.Children.Add(PageKit.WithBottomLine(header));
    }

    /// <summary>An empty row grid with this table's columns.</summary>
    public Grid NewRow(double height)
    {
        var grid = new Grid { HeightRequest = height, Padding = new Thickness(SidePadding, 0), ColumnSpacing = 0 };
        foreach (var c in _columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition(c.Flex ? new GridLength(c.Width, GridUnitType.Star) : new GridLength(c.Width)));
        return grid;
    }

    /// <summary>Adds a row (with a bottom line). <paramref name="open"/> runs on a tap outside the last (actions) column.</summary>
    public void AddRow(Grid row, Action open = null, bool lastColumnIsActions = true)
    {
        if (open != null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (lastColumnIsActions && e.GetPosition(row) is Point p && p.X > row.Width - _columns[^1].Width - SidePadding) return;
                open();
            };
            row.GestureRecognizers.Add(tap);
            HandCursor.Apply(row);
            PageKit.AddRowHover(row);
        }
        _stack.Children.Add(PageKit.WithBottomLine(row));
    }

    /// <summary>Adds a full-width view under the header (an empty state or a message).</summary>
    public void AddMessage(View view) => _stack.Children.Add(view);

    private void Fit()
    {
        if (_scroll.Width <= 0) return;
        _stack.WidthRequest = Math.Max(_minWidth, _scroll.Width);
    }
}
