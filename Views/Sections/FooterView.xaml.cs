using VetCare.Models;

namespace VetCare.Views.Sections;

public partial class FooterView : SectionView
{
    public FooterView()
    {
        InitializeComponent();
    }

    public override void ApplyWidth(double width)
    {
        Container.WidthRequest = ContentWidth(width);

        if (IsStacked(width))
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Place(BrandLabel, 0, LayoutOptions.Center);
            Place(MottoLabel, 1, LayoutOptions.Center);
            Place(BackToTop, 2, LayoutOptions.Center);
        }
        else
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star));
            Place(BrandLabel, 0, LayoutOptions.Start, column: 0);
            Place(MottoLabel, 0, LayoutOptions.Fill, column: 1);
            Place(BackToTop, 0, LayoutOptions.End, column: 2);
        }
    }

    private static void Place(View view, int row, LayoutOptions horizontal, int column = 0)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        view.HorizontalOptions = horizontal;
    }

    private void OnBackToTopClicked(object sender, EventArgs e) => Navigate(NavigationTarget.Top);
}
