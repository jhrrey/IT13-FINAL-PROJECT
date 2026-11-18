using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;

namespace VetCare.Views.Controls;

/// <summary>
/// One account-card tab. Active: orange text, faint orange outline and a 2px rounded
/// orange underline. Inactive: grey-navy text over a thin grey line.
/// </summary>
public sealed class AuthTab : ContentView
{
    private readonly Border _background;
    private readonly Label _label;
    private readonly BoxView _bar;
    private bool _active;
    private bool _hover;

    public AuthTab(AuthMode mode, string text)
    {
        Mode = mode;
        HeightRequest = 48;

        _background = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6, 6, 0, 0) },
            StrokeThickness = 1
        };

        _label = new Label
        {
            Text = text,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _label.StyleClass = new[] { "tab-label" };

        _bar = new BoxView { VerticalOptions = LayoutOptions.End };

        Content = new Grid { Children = { _background, _label, _bar } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            _ = PulseAsync();
            Clicked?.Invoke(this, EventArgs.Empty);
        };
        GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => { _hover = true; Apply(); };
        pointer.PointerExited += (s, e) => { _hover = false; Apply(); };
        GestureRecognizers.Add(pointer);

        HandCursor.Apply(this);
        SemanticProperties.SetDescription(this, text + " tab");
        Apply();
    }

    public event EventHandler Clicked;

    public AuthMode Mode { get; }

    /// <summary>A quick press-in-and-back so the click is felt before anything else happens.</summary>
    private async Task PulseAsync()
    {
        await this.ScaleTo(0.96, 70, Easing.CubicOut);
        await this.ScaleTo(1, 110, Easing.CubicOut);
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            Apply();
        }
    }

    private void Apply()
    {
        if (_active)
        {
            _background.BackgroundColor = ThemeResources.Color("TabActiveFill");
            _background.Stroke = ThemeResources.Color("TabActiveOutline");
            _label.TextColor = ThemeResources.Color("Orange");
            _bar.Color = ThemeResources.Color("Orange");
            _bar.HeightRequest = 2;
            _bar.CornerRadius = 1;
        }
        else
        {
            _background.BackgroundColor = _hover ? ThemeResources.Color("InputBackground") : Colors.Transparent;
            _background.Stroke = Colors.Transparent;
            _label.TextColor = ThemeResources.Color(_hover ? "Navy" : "NavyMuted");
            _bar.Color = ThemeResources.Color("BorderColor");
            _bar.HeightRequest = 1;
            _bar.CornerRadius = 0;
        }
    }
}
