using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

public partial class HeroView : SectionView
{
    public HeroView()
    {
        InitializeComponent();
    }

    public AuthCardView Card => AuthCard;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is HeroContent content) AuthCard.SetEyebrow(content.CardEyebrow);
    }

    public override void ApplyWidth(double width)
    {
        bool stacked = IsStacked(width);
        bool compact = IsCompact(width);
        double content = ContentWidth(width);

        Container.WidthRequest = content;
        Container.Padding = stacked ? new Thickness(0, 32, 0, 72) : new Thickness(0, 48, 0, 96);

        double titleSize = compact ? 44 : IsNarrow(width) ? 56 : 64;
        TitleLabel.FontSize = titleSize;
        TitleLabel.CharacterSpacing = compact ? -0.36 : -0.375; // see the letter-spacing note in app.css
        // On Windows the spans keep the size they were drawn with when the label's size changes later
        // (the line keeps the big height but the text shrinks), so size each span too.
        if (TitleLabel.FormattedText is FormattedString title)
            foreach (var span in title.Spans) span.FontSize = titleSize;
        LeadLabel.FontSize = compact ? 17 : 19;

        // Two columns (45 / 55) on wide windows; card below the copy when stacked.
        if (stacked)
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(AuthCard, 0);
            Grid.SetRow(AuthCard, 1);
            AuthCard.HorizontalOptions = LayoutOptions.Center;
        }
        else
        {
            Container.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(new GridLength(45, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(55, GridUnitType.Star)));
            Grid.SetColumn(AuthCard, 1);
            Grid.SetRow(AuthCard, 0);
            AuthCard.HorizontalOptions = LayoutOptions.End;
        }

        double rightColumn = stacked ? content : (content - 40) * 0.55;
        AuthCard.WidthRequest = Math.Min(490, rightColumn);
        AuthCard.SetCompact(AuthCard.WidthRequest < 420);

        // On phones the button goes full width and the text link wraps underneath.
        if (compact)
        {
            ActionsGrid.HorizontalOptions = LayoutOptions.Fill;
            ActionsGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            PrimaryButton.WidthRequest = -1;
            Grid.SetColumn(SecondaryLink, 0);
            Grid.SetRow(SecondaryLink, 1);
            SecondaryLink.HorizontalOptions = LayoutOptions.Start;
        }
        else
        {
            ActionsGrid.HorizontalOptions = LayoutOptions.Start;
            ActionsGrid.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto));
            PrimaryButton.WidthRequest = 240;
            Grid.SetColumn(SecondaryLink, 1);
            Grid.SetRow(SecondaryLink, 0);
        }

        NoteLabel.MaximumWidthRequest = Math.Min(358, content - 62);
    }

    private void OnPrimaryClicked(object sender, EventArgs e)
    {
        if (BindingContext is HeroContent content) Navigate(content.PrimaryCta.Target);
    }

    private void OnSecondaryClicked(object sender, EventArgs e)
    {
        if (BindingContext is HeroContent content) Navigate(content.SecondaryCta.Target);
    }
}
