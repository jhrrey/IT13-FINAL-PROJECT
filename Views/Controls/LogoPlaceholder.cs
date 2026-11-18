namespace VetCare.Views.Controls;

/// <summary>
/// Solid black rounded block that stands in for the clinic logo.
/// Set <see cref="Source"/> (e.g. "logo.png") to show the real logo instead.
/// </summary>
public sealed class LogoPlaceholder : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(LogoPlaceholder), null,
            propertyChanged: (b, o, n) => ((LogoPlaceholder)b).Apply());

    private readonly Border _block;
    private readonly Image _image;

    public LogoPlaceholder()
    {
        _image = new Image { Aspect = Aspect.AspectFit, IsVisible = false };
        _block = new Border { Style = ThemeResources.Get<Style>("LogoBlock"), Content = _image };
        Content = _block;
    }

    public ImageSource Source
    {
        get => (ImageSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void Apply()
    {
        bool hasLogo = Source != null;
        _image.Source = Source;
        _image.IsVisible = hasLogo;
        _block.BackgroundColor = hasLogo ? Colors.Transparent : Colors.Black;
    }
}
