using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Navigation;
using VetCare.Views.Controls;

namespace VetCare.Views.Workspace;

/// <summary>
/// One sidebar entry: 48px tall, icon + 16px gap + label, 8px radius.
/// Hover: #EAF6FA. Active: #B8F0F2 with a bold navy label and navy icon.
/// </summary>
public sealed class SidebarMenuItem : ContentView
{
    private readonly Border _root;
    private readonly LucideIcon _icon;
    private readonly Label _label;
    private bool _active;
    private bool _hover;

    public SidebarMenuItem(ModuleItem item)
    {
        Item = item;

        _icon = new LucideIcon
        {
            Icon = item.Icon,
            WidthRequest = 20,
            HeightRequest = 20,
            VerticalOptions = LayoutOptions.Center
        };

        _label = new Label
        {
            Text = item.Label,
            FontSize = 17,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        _root = new Border
        {
            HeightRequest = 48,
            Padding = new Thickness(12, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                Spacing = 16,
                VerticalOptions = LayoutOptions.Center,
                Children = { _icon, _label }
            }
        };
        Content = _root;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Clicked?.Invoke(this, EventArgs.Empty);
        _root.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => { _hover = true; Apply(); };
        pointer.PointerExited += (s, e) => { _hover = false; Apply(); };
        _root.GestureRecognizers.Add(pointer);

        HandCursor.Apply(_root);
        SemanticProperties.SetDescription(this, item.Label);
        Apply();
    }

    public event EventHandler Clicked;

    public ModuleItem Item { get; }

    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            Apply();
        }
    }

    private void Apply()
    {
        var textColor = ThemeResources.Color(_active ? "Navy" : "MenuText");

        _root.BackgroundColor = _active
            ? ThemeResources.Color("Cyan")
            : _hover ? ThemeResources.Color("MenuHover") : Colors.Transparent;

        _label.TextColor = textColor;
        _label.FontFamily = _active ? "DMSansBold" : "DMSans";
        _icon.StrokeColor = textColor;
    }
}
