using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>"Clinics with their own value" (System Settings, section 4.B): each clinic's value with Remove override.</summary>
public sealed class SettingOverridesView : ContentView
{
    public const double PanelWidth = 400;

    public SettingOverridesView(SettingDefinition def, IReadOnlyList<SystemSetting> overrides, string defaultValue)
    {
        var list = new VerticalStackLayout { Spacing = 0 };
        if (overrides.Count == 0)
            list.Children.Add(new Label { Text = "No clinic uses its own value.", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        for (int i = 0; i < overrides.Count; i++)
        {
            var o = overrides[i];
            if (i > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            var remove = new TextLink { Text = "Remove override", FontSize = 13.5, FontFamily = "DMSansBold", TextColor = ThemeResources.Color("Danger"), HorizontalOptions = LayoutOptions.End };
            int clinicId = o.ClinicId ?? 0;
            remove.Clicked += (s, e) => RemoveRequested?.Invoke(this, clinicId);
            var row = new Grid { Padding = new Thickness(0, 10), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = o.ClinicName ?? "Clinic", FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = SettingCatalog.Display(o.SettingKey, o.SettingValue), FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
                }
            }, 0, 0);
            row.Add(remove, 1, 0);
            list.Children.Add(row);
        }
        list.Children.Add(new Label
        {
            Text = "Other clinics use the default value (" + SettingCatalog.Display(def.Key, defaultValue) + ").",
            FontSize = 13,
            TextColor = ThemeResources.Color("Muted"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        Content = PharmacyKit.PanelFrame("Clinics with their own value", def.Label, new VerticalStackLayout { Padding = new Thickness(24, 16), Children = { list } },
            () => CloseRequested?.Invoke(this, EventArgs.Empty), null);
    }

    public event EventHandler<int> RemoveRequested;
    public event EventHandler CloseRequested;
}
