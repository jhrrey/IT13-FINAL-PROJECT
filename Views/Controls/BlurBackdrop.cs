namespace VetCare.Views.Controls;

/// <summary>
/// Full-size layer that blurs whatever is behind it, with a light navy tint.
/// On Windows this uses the WinUI in-app acrylic brush (see MauiProgram.UseBlurBackdrops).
/// If Windows transparency effects are turned off, it falls back to a plain navy tint.
/// </summary>
public sealed class BlurBackdrop : Grid
{
    /// <summary>Raised when the backdrop itself (not the dialog on top of it) is clicked.</summary>
    public event EventHandler Tapped;

    public BlurBackdrop()
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
    }

#if WINDOWS
    /// <summary>Applies the acrylic blur to the native panel.</summary>
    internal static void ApplyTo(Microsoft.UI.Xaml.Controls.Panel panel)
    {
        panel.Background = new Microsoft.UI.Xaml.Media.AcrylicBrush
        {
            TintColor = Windows.UI.Color.FromArgb(255, 0x0A, 0x2E, 0x4F),  // navy
            TintOpacity = 0.25,
            TintLuminosityOpacity = 0.35,
            FallbackColor = Windows.UI.Color.FromArgb(150, 0x0A, 0x2E, 0x4F)
        };
    }
#endif
}
