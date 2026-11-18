using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

public partial class NavbarView : SectionView
{
    public NavbarView()
    {
        InitializeComponent();

        var logoTap = new TapGestureRecognizer();
        logoTap.Tapped += (s, e) => Navigate(NavigationTarget.Top);
        Logo.GestureRecognizers.Add(logoTap);
        HandCursor.Apply(Logo);
    }

    /// <summary>Navbar logo slot. Set <c>Logo.Source</c> to show an image.</summary>
    public LogoPlaceholder LogoSlot => Logo;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is not NavbarContent content) return;

        LinksStack.Children.Clear();
        foreach (var link in content.Links)
        {
            var target = link.Target;
            var view = new TextLink { Text = link.Label, VerticalOptions = LayoutOptions.Center };
            view.StyleClass = new[] { "nav-link" };
            view.Clicked += (s, e) => Navigate(target);
            LinksStack.Children.Add(view);
        }
    }

    public override void ApplyWidth(double width)
    {
        bool compact = IsCompact(width);

        Container.WidthRequest = ContentWidth(width);
        Container.HeightRequest = compact ? 72 : 90;
        Logo.WidthRequest = compact ? 150 : 250;
        Logo.HeightRequest = compact ? 48 : 80;

        // The text links need roughly 560px alongside the logo.
        LinksStack.IsVisible = width >= 560;
    }
}
