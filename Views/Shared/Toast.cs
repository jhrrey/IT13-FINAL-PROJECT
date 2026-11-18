using Microsoft.Maui.Controls.Shapes;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>A small green message at the bottom of a module ("Dispensed. Added to …'s bill."), gone after ~3 seconds.</summary>
public sealed class Toast : ContentView
{
    private readonly Border _card;
    private readonly Label _text;
    private int _number;

    public Toast()
    {
        IsVisible = false;
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.End;
        _text = new Label { FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("CompletedText"), VerticalOptions = LayoutOptions.Center };
        _card = new Border
        {
            Padding = new Thickness(18, 12),
            Margin = new Thickness(0, 0, 0, 28),
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("SuccessDot"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color("Mint"),
            Shadow = new Shadow { Brush = ThemeResources.Color("Navy"), Offset = new Point(0, 6), Radius = 18, Opacity = 0.15f },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new LucideIcon { Icon = "circle-check", StrokeColor = ThemeResources.Color("CompletedText"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    _text
                }
            }
        };
        Content = _card;
    }

    public async void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        int mine = ++_number;
        _text.Text = message;
        Opacity = 0;
        IsVisible = true;
        await this.FadeTo(1, 160);
        await Task.Delay(3200);
        if (mine != _number) return;
        await this.FadeTo(0, 220);
        if (mine == _number) IsVisible = false;
    }
}
