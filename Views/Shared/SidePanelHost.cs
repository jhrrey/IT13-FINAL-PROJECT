using Microsoft.Maui.Controls.Shapes;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// A white panel (440px by default) that slides in from the right over a dimmed background.
/// Hosts the Add/Edit forms and the user details view.
/// </summary>
public sealed class SidePanelHost : ContentView
{
    public const double PanelWidth = 440;

    private readonly BoxView _dim;
    private readonly Border _panel;
    private bool _open;

    public SidePanelHost()
    {
        IsVisible = false;

        _dim = new BoxView { Color = ThemeResources.Color("Dim") };
        var dimTap = new TapGestureRecognizer();
        dimTap.Tapped += (s, e) => DismissRequested?.Invoke(this, EventArgs.Empty);
        _dim.GestureRecognizers.Add(dimTap);

        _panel = new Border
        {
            WidthRequest = PanelWidth,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = ThemeResources.Color("Surface"),
            StrokeThickness = 0,
            StrokeShape = new Rectangle(),
            Shadow = new Shadow
            {
                Brush = ThemeResources.Color("Navy"),
                Offset = new Point(-8, 0),
                Radius = 32,
                Opacity = 0.18f
            }
        };

        Content = new Grid { Children = { _dim, _panel } };
    }

    /// <summary>The dimmed background was clicked.</summary>
    public event EventHandler DismissRequested;

    public bool IsOpen => _open;

    /// <summary>Shows <paramref name="content"/> in the panel, sliding it in if the panel was closed.</summary>
    public async Task ShowAsync(View content, double panelWidth = PanelWidth)
    {
        _panel.Content = content;
        double width = Math.Min(panelWidth, Width > 0 ? Width : panelWidth);
        if (_open)
        {
            _panel.WidthRequest = width;
            return;
        }
        _open = true;

        _panel.WidthRequest = width;
        _panel.TranslationX = width;
        _dim.Opacity = 0;
        IsVisible = true;

        await Task.WhenAll(
            _dim.FadeTo(1, 200, Easing.CubicOut),
            _panel.TranslateTo(0, 0, 260, Easing.CubicOut));
    }

    public async Task HideAsync()
    {
        if (!_open) return;
        _open = false;

        await Task.WhenAll(
            _dim.FadeTo(0, 180, Easing.CubicIn),
            _panel.TranslateTo(_panel.Width > 0 ? _panel.Width : PanelWidth, 0, 200, Easing.CubicIn));

        IsVisible = false;
        _panel.Content = null;
    }
}
