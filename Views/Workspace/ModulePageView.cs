using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Navigation;
using VetCare.Views.Controls;

namespace VetCare.Views.Workspace;

/// <summary>
/// The page shown for a module. For now each module shows its title and purpose with a
/// "coming next" note; the real screens (lists, forms) replace this one module at a time.
/// </summary>
public sealed class ModulePageView : ContentView
{
    public ModulePageView(ModuleItem item, string sectionTitle, ModuleAccess access, string clinicName)
    {
        // Same header as the built module pages: icon tile centred on the title, description below.
        var headerRow = new PageHeader { Icon = item.Icon, Title = item.Label, Subtitle = item.Description };

        var badges = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 20, 0, 0) };
        if (!string.IsNullOrEmpty(clinicName)) badges.Children.Add(Badge(clinicName, "PaleBlue"));
        if (access == ModuleAccess.ViewOnly) badges.Children.Add(Badge("View only", "Mint"));

        var placeholder = new Border
        {
            Margin = new Thickness(0, 28, 0, 0),
            Padding = new Thickness(32, 28),
            BackgroundColor = ThemeResources.Color("Surface"),
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = item.Label + " is coming next.",
                        FontFamily = "DMSansBold",
                        FontSize = 18,
                        TextColor = ThemeResources.Color("Navy")
                    },
                    new Label
                    {
                        Text = "This screen will show your clinic’s " + item.Label.ToLowerInvariant() +
                               " from the VetCare database.",
                        FontSize = 15,
                        TextColor = ThemeResources.Color("Muted")
                    }
                }
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(40, 36),
                Children = { headerRow, badges, placeholder }
            }
        };
    }

    private static View Badge(string text, string colorKey) => new Border
    {
        Padding = new Thickness(12, 5),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        BackgroundColor = ThemeResources.Color(colorKey),
        Content = new Label
        {
            Text = text,
            FontFamily = "DMSansSemiBold",
            FontSize = 13,
            TextColor = ThemeResources.Color("Navy")
        }
    };
}
