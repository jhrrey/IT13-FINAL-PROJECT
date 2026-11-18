using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

public partial class ServicesView : SectionView
{
    private readonly List<ServiceCardView> _cards = new();
    private int _columns;

    public ServicesView()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is not ServicesContent content) return;

        _cards.Clear();
        foreach (var item in content.Items)
        {
            var card = new ServiceCardView(item, content.LinkLabel);
            card.LearnMoreClicked += (s, e) => Navigate(NavigationTarget.OurCare);
            _cards.Add(card);
        }

        _columns = 0;
        BuildGrid(3);
    }

    public override void ApplyWidth(double width)
    {
        bool stacked = IsStacked(width);
        bool compact = IsCompact(width);

        Container.WidthRequest = ContentWidth(width);
        Container.Padding = new Thickness(0, compact ? 72 : stacked ? 88 : 120);
        TitleLabel.FontSize = compact ? 38 : IsNarrow(width) ? 48 : 56;

        // Header: two columns, or heading above the intro when stacked.
        if (stacked)
        {
            HeaderGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(IntroLabel, 0);
            Grid.SetRow(IntroLabel, 1);
            IntroLabel.HorizontalOptions = LayoutOptions.Start;
            HeaderGrid.Margin = new Thickness(0, 0, 0, 44);
        }
        else
        {
            HeaderGrid.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(IntroLabel, 1);
            Grid.SetRow(IntroLabel, 0);
            IntroLabel.HorizontalOptions = LayoutOptions.End;
            HeaderGrid.Margin = new Thickness(0, 0, 0, 60);
        }

        foreach (var card in _cards) card.SetCompact(compact);
        BuildGrid(compact ? 1 : stacked ? 2 : 3);
    }

    /// <summary>
    /// Lays the cards out like a table: each card draws its right and bottom lines,
    /// and the grid adds one top and one left line. Empty cells get no border.
    /// </summary>
    private void BuildGrid(int columns)
    {
        if (columns == _columns || _cards.Count == 0) return;
        _columns = columns;

        int rows = (_cards.Count + columns - 1) / columns;

        CardsGrid.Children.Clear();
        CardsGrid.ColumnDefinitions = new ColumnDefinitionCollection(
            Enumerable.Range(0, columns).Select(_ => new ColumnDefinition(GridLength.Star)).ToArray());
        CardsGrid.RowDefinitions = new RowDefinitionCollection(
            Enumerable.Range(0, rows).Select(_ => new RowDefinition(GridLength.Auto)).ToArray());

        for (int i = 0; i < _cards.Count; i++)
            CardsGrid.Add(_cards[i], i % columns, i / columns);

        var border = ThemeResources.Color("BorderColor");

        var top = new BoxView { Color = border, HeightRequest = 1, VerticalOptions = LayoutOptions.Start };
        CardsGrid.Add(top, 0, 0);
        Grid.SetColumnSpan(top, columns);

        var left = new BoxView { Color = border, WidthRequest = 1, HorizontalOptions = LayoutOptions.Start };
        CardsGrid.Add(left, 0, 0);
        Grid.SetRowSpan(left, rows);
    }
}
