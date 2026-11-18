using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

public partial class AboutView : SectionView
{
    public AboutView()
    {
        InitializeComponent();
    }

    /// <summary>About-section logo slot. Set <c>Logo.Source</c> to show an image.</summary>
    public LogoPlaceholder LogoSlot => Logo;

    public override void ApplyWidth(double width)
    {
        bool stacked = IsStacked(width);
        bool compact = IsCompact(width);
        double content = ContentWidth(width);

        Container.WidthRequest = content;
        Container.Padding = compact ? new Thickness(0, 72, 0, 120) : new Thickness(0, 96);
        TitleLabel.FontSize = compact ? 38 : IsNarrow(width) ? 46 : 54;

        if (stacked)
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(Copy, 0);
            Grid.SetRow(Copy, 1);
        }
        else
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(new GridLength(40, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(60, GridUnitType.Star)));
            Grid.SetColumn(Copy, 1);
            Grid.SetRow(Copy, 0);
        }

        double leftColumn = stacked ? content : (content - 56) * 0.4;
        Media.WidthRequest = Math.Min(370, leftColumn);
        Badge.Margin = new Thickness(0, 28, compact ? 24 : 40, 0);
        Decor.Margin = stacked ? new Thickness(0, 0, 24, 24) : new Thickness(0, 0, 48, 40);
    }

    private void OnCtaClicked(object sender, EventArgs e)
    {
        if (BindingContext is AboutContent content) Navigate(content.Cta.Target);
    }
}
