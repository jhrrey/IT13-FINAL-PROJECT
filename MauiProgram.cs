using Microsoft.Maui.Handlers;

namespace VetCare;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Nunito-ExtraBold.ttf", "NunitoExtraBold");
                fonts.AddFont("DMSans-Regular.ttf", "DMSans");
                fonts.AddFont("DMSans-Medium.ttf", "DMSansMedium");
                fonts.AddFont("DMSans-SemiBold.ttf", "DMSansSemiBold");
                fonts.AddFont("DMSans-Bold.ttf", "DMSansBold");
            });

        // Reports module: PDF export with QuestPDF (free Community license).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        RemoveNativeEntryChrome();
        RemoveNativePickerChrome();
        MakeIconsNonFocusable();
        UseBlurBackdrops();

        return builder.Build();
    }

    /// <summary>
    /// Dropdowns sit inside the same rounded box as text inputs (Views/Controls/PickerField),
    /// so the native Windows ComboBox border and background are switched off.
    /// </summary>
    private static void RemoveNativePickerChrome()
    {
        PickerHandler.Mapper.AppendToMapping("VetCareBorderless", (handler, view) =>
        {
#if WINDOWS
            // Only plain properties: WinUI refuses writes to a ComboBox's resource dictionary here
            // (COMException 0x800F0902), which crashed the Users & roles page.
            var comboBox = handler.PlatformView;
            comboBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            comboBox.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            comboBox.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
#endif
        });

        // The pet form lays a transparent DatePicker over its own date box; it must fill that box
        // so a click anywhere in the box opens the calendar.
        DatePickerHandler.Mapper.AppendToMapping("VetCareBorderless", (handler, view) =>
        {
#if WINDOWS
            var picker = handler.PlatformView;
            picker.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            picker.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            picker.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            picker.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
#endif
        });
    }

    /// <summary>
    /// Gives <see cref="Views.Controls.BlurBackdrop"/> its blurred acrylic background on Windows.
    /// Runs after MAUI applies the normal background, so the acrylic brush wins.
    /// </summary>
    private static void UseBlurBackdrops()
    {
        LayoutHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, view) =>
        {
#if WINDOWS
            if (view is Views.Controls.BlurBackdrop)
                Views.Controls.BlurBackdrop.ApplyTo(handler.PlatformView);
#endif
        });
    }

    /// <summary>
    /// Icons are decoration. On Windows a GraphicsView is focusable by default, and when focus
    /// lands on one the page jumps to show it, so they are taken out of the tab order.
    /// </summary>
    private static void MakeIconsNonFocusable()
    {
        GraphicsViewHandler.Mapper.AppendToMapping("VetCareNoFocus", (handler, view) =>
        {
#if WINDOWS
            handler.PlatformView.IsTabStop = false;
#endif
        });
    }

    /// <summary>
    /// The rounded input box is drawn by a MAUI <c>Border</c> (see Views/Controls/InputField),
    /// so the native Windows TextBox border, background and focus underline are switched off.
    /// </summary>
    private static void RemoveNativeEntryChrome()
    {
        // Single-line inputs (Entry) and multi-line inputs (Editor, e.g. the owner's address).
        EntryHandler.Mapper.AppendToMapping("VetCareBorderless", (handler, view) =>
        {
#if WINDOWS
            MakeBorderless(handler.PlatformView);
#endif
        });
        EditorHandler.Mapper.AppendToMapping("VetCareBorderless", (handler, view) =>
        {
#if WINDOWS
            MakeBorderless(handler.PlatformView);
#endif
        });
    }

#if WINDOWS
    private static void MakeBorderless(Microsoft.UI.Xaml.Controls.TextBox textBox)
    {
        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var noBorder = new Microsoft.UI.Xaml.Thickness(0);

        textBox.BorderThickness = noBorder;
        textBox.Background = transparent;
        textBox.Padding = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 6);

        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"
        })
        {
            textBox.Resources[key] = transparent;
        }

        textBox.Resources["TextControlBorderThemeThickness"] = noBorder;
        textBox.Resources["TextControlBorderThemeThicknessFocused"] = noBorder;
    }
#endif
}
