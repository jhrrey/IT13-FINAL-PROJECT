using Microsoft.Maui.Controls.Shapes;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>Page tabs: the active tab has orange bold text and a 2px orange underline; a tab can show a count pill.</summary>
public sealed class TabStrip : ContentView
{
    private readonly HorizontalStackLayout _row = new() { Spacing = 28 };
    private readonly List<(Label Label, BoxView Line, Border Count)> _tabs = new();

    public TabStrip()
    {
        var line = new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End };
        Content = new Grid { Children = { line, _row } };
    }

    public event EventHandler<int> TabChanged;

    public int Current { get; private set; } = -1;

    /// <param name="countColors">Background and text color keys of the count pill (null = no pill).</param>
    public TabStrip Add(string text, (string Background, string Text)? countColors = null)
    {
        int index = _tabs.Count;
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        Border count = null;
        var content = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { label } };
        if (countColors is var (bg, fg))
        {
            count = new Border
            {
                IsVisible = false,
                Padding = new Thickness(8, 1),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = ThemeResources.Color(bg),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label { FontFamily = "DMSansBold", FontSize = 12, TextColor = ThemeResources.Color(fg) }
            };
            content.Children.Add(count);
        }
        var underline = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End, Color = Colors.Transparent };
        var tab = new Grid { HeightRequest = 42, Children = { content, underline } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Select(index, raise: true);
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);
        SemanticProperties.SetDescription(tab, text);
        _tabs.Add((label, underline, count));
        _row.Children.Add(tab);
        if (Current < 0) Select(0, raise: false);
        else Paint();
        return this;
    }

    public void SetCount(int index, int count)
    {
        var pill = _tabs[index].Count;
        if (pill == null) return;
        pill.IsVisible = count > 0;
        ((Label)pill.Content).Text = count.ToString();
    }

    public void Select(int index, bool raise)
    {
        bool changed = Current != index;
        Current = index;
        Paint();
        if (raise && changed) TabChanged?.Invoke(this, index);
    }

    private void Paint()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == Current;
            _tabs[i].Label.FontFamily = active ? "DMSansBold" : "DMSansMedium";
            _tabs[i].Label.TextColor = ThemeResources.Color(active ? "Orange" : "BodyText");
            _tabs[i].Line.Color = active ? ThemeResources.Color("Orange") : Colors.Transparent;
        }
    }
}
