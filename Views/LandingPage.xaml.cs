using VetCare.Models;
using VetCare.Views.Controls;
using VetCare.Views.Sections;

namespace VetCare.Views;

/// <summary>
/// The landing window's page. Hands each section its slice of <see cref="LandingContent"/>,
/// re-flows the sections when the window is resized, and performs smooth scrolling.
/// </summary>
public partial class LandingPage : ContentPage
{
    private readonly SectionView[] _sections;
    private double _lastWidth = -1;

    public LandingPage(LandingContent content)
    {
        InitializeComponent();

        Navbar.BindingContext = content.Navbar;
        Hero.BindingContext = content.Hero;
        Quote.BindingContext = content.Quote;
        Services.BindingContext = content.Services;
        About.BindingContext = content.About;
        Footer.BindingContext = content;

        AccountModalPanel.SetEyebrow(content.Hero.CardEyebrow);

        _sections = new SectionView[] { Navbar, Hero, Quote, Services, About, Footer };
        foreach (var section in _sections)
            section.NavigateRequested += (s, e) => NavigateRequested?.Invoke(this, e);

        SizeChanged += (s, e) =>
        {
            ApplyWidth(Width);
            AccountModalPanel.ApplySize(Width, Height);
        };
    }

    /// <summary>A section asked to go somewhere on the page.</summary>
    public event EventHandler<PageNavigateEventArgs> NavigateRequested;

    public AuthCardView AuthCard => Hero.Card;

    /// <summary>The "Create account" modal shown over the blurred page.</summary>
    public AccountModalView AccountModal => AccountModalPanel;

    /// <summary>Navbar logo slot (set <c>.Source</c> to show an image).</summary>
    public LogoPlaceholder NavbarLogo => Navbar.LogoSlot;

    /// <summary>About-section logo slot (set <c>.Source</c> to show an image).</summary>
    public LogoPlaceholder AboutLogo => About.LogoSlot;

    /// <summary>Smoothly scrolls to a section (or to the account card for <see cref="NavigationTarget.SignIn"/>).</summary>
    public Task ScrollToAsync(NavigationTarget target)
    {
        switch (target)
        {
            case NavigationTarget.OurCare:
                return Scroller.ScrollToAsync(Services, ScrollToPosition.Start, animated: true);
            case NavigationTarget.About:
                return Scroller.ScrollToAsync(About, ScrollToPosition.Start, animated: true);
            case NavigationTarget.SignIn:
                var card = Scroller.GetScrollPositionForElement(AuthCard, ScrollToPosition.Start);
                return Scroller.ScrollToAsync(0, Math.Max(0, card.Y - 24), animated: true);
            default:
                return Scroller.ScrollToAsync(0, 0, animated: true);
        }
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _lastWidth) < 0.5) return;
        _lastWidth = width;

        foreach (var section in _sections) section.ApplyWidth(width);
    }
}
