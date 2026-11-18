namespace VetCare.Views.Controls;

/// <summary>Looks up colours and styles defined in Resources/Styles (Colors.xaml, Styles.xaml).</summary>
internal static class ThemeResources
{
    public static T Get<T>(string key) =>
        Application.Current != null && Application.Current.Resources.TryGetValue(key, out var value)
            ? (T)value
            : default;

    public static Color Color(string key) => Get<Color>(key);
}
