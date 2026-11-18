namespace VetCare.Views.Sections;

public partial class QuoteBandView : SectionView
{
    public QuoteBandView()
    {
        InitializeComponent();
    }

    public override void ApplyWidth(double width)
    {
        bool compact = IsCompact(width);
        double content = ContentWidth(width);

        Container.WidthRequest = content;
        Divider.IsVisible = !compact;
        QuoteLabel.FontSize = compact ? 21 : 26;
        QuoteLabel.MaximumWidthRequest = compact ? content : content - 29;
    }
}
