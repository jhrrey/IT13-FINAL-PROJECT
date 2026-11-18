namespace VetCare.Views.Controls;

/// <summary>Shows the hand (link) cursor over clickable views on Windows.</summary>
internal static class HandCursor
{
    public static void Apply(View view)
    {
        view.HandlerChanged += (s, e) => Set(view);
        Set(view);
    }

    private static void Set(View view)
    {
#if WINDOWS
        if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element) return;

        // UIElement.ProtectedCursor is the only cursor API in WinUI and it is protected.
        var property = typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
            "ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        try
        {
            property?.SetValue(element,
                Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand));
        }
        catch (Exception)
        {
            // Purely cosmetic; ignore if the platform refuses.
        }
#endif
    }
}
