using Microsoft.Maui.Controls.Shapes;

namespace VetCare.Views.Controls;

/// <summary>
/// A drawn on/off switch (40×22) with an optional label. WinUI's native ToggleSwitch adds wide On/Off text,
/// so this keeps the look compact. <see cref="AskFirst"/> raises <see cref="TapRequested"/> without changing.
/// </summary>
public sealed class ToggleSwitch : ContentView
{
    private readonly Border _track;
    private readonly Ellipse _knob;
    private readonly Label _label;
    private bool _isOn;

    public ToggleSwitch(string label = null, bool isOn = false, string onColorKey = "Cyan", string offColorKey = "NeutralDot")
    {
        OnColorKey = onColorKey;
        OffColorKey = offColorKey;
        _knob = new Ellipse { WidthRequest = 16, HeightRequest = 16, Fill = Colors.White, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(3, 0) };
        _track = new Border
        {
            WidthRequest = 40,
            HeightRequest = 22,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid { Children = { _knob } }
        };
        _label = new Label { Text = label, FontSize = 14.5, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, IsVisible = !string.IsNullOrEmpty(label) };
        var row = new HorizontalStackLayout { Spacing = 10, Children = { _track, _label } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (!IsEnabled) return;
            if (AskFirst) { TapRequested?.Invoke(this, EventArgs.Empty); return; }
            IsOn = !IsOn;
            Toggled?.Invoke(this, IsOn);
        };
        row.GestureRecognizers.Add(tap);
        HandCursor.Apply(row);
        SemanticProperties.SetDescription(row, label ?? "Switch");
        Content = row;
        IsOn = isOn;
    }

    public event EventHandler<bool> Toggled;

    /// <summary>Raised instead of toggling when <see cref="AskFirst"/> is true.</summary>
    public event EventHandler TapRequested;

    public bool AskFirst { get; set; }

    public string OnColorKey { get; }
    public string OffColorKey { get; }

    public string Text
    {
        get => _label.Text;
        set { _label.Text = value; _label.IsVisible = !string.IsNullOrEmpty(value); }
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            _isOn = value;
            _track.BackgroundColor = ThemeResources.Color(value ? OnColorKey : OffColorKey);
            _knob.HorizontalOptions = value ? LayoutOptions.End : LayoutOptions.Start;
            _knob.Fill = value && OnColorKey == "Cyan" ? ThemeResources.Color("Navy") : Colors.White;
        }
    }
}
