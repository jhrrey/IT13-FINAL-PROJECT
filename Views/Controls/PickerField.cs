namespace VetCare.Views.Controls;

/// <summary>
/// Label + dropdown in the same rounded box as <see cref="InputField"/>, with an inline error.
/// Items are (text, value) pairs; <see cref="SelectedValue"/> returns the chosen value.
/// </summary>
public sealed class PickerField : ContentView
{
    private readonly Label _label;
    private readonly Picker _picker;
    private readonly Border _box;
    private readonly Label _error;
    private readonly List<object> _values = new();

    public PickerField(string fieldName, string label)
    {
        FieldName = fieldName;

        _label = new Label { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        _label.StyleClass = new[] { "field-label" };

        _picker = new Picker
        {
            FontFamily = "DMSans",
            FontSize = 16,
            TextColor = ThemeResources.Color("Navy"),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center
        };
        _picker.SelectedIndexChanged += (s, e) =>
        {
            SetError(null);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        _box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Padding = new Thickness(8, 0), Content = _picker };

        _error = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _error.StyleClass = new[] { "field-error" };

        Content = new VerticalStackLayout { Children = { _label, _box, _error } };
    }

    public event EventHandler SelectionChanged;

    public string FieldName { get; }

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>The text of the chosen option, or null.</summary>
    public string SelectedText => _picker.SelectedIndex >= 0 ? _picker.Items[_picker.SelectedIndex] : null;

    public object SelectedValue =>
        _picker.SelectedIndex >= 0 && _picker.SelectedIndex < _values.Count ? _values[_picker.SelectedIndex] : null;

    /// <summary>Replaces the options. Keeps <paramref name="selected"/> selected if it is among them.</summary>
    public void SetItems(IEnumerable<(string Text, object Value)> items, object selected = null)
    {
        _picker.Items.Clear();
        _values.Clear();
        foreach (var (text, value) in items)
        {
            _picker.Items.Add(text);
            _values.Add(value);
        }

        int index = selected == null ? -1 : _values.FindIndex(v => Equals(v, selected));
        _picker.SelectedIndex = index >= 0 ? index : (_values.Count == 1 ? 0 : -1);
    }

    public void Select(object value)
    {
        int index = _values.FindIndex(v => Equals(v, value));
        if (index >= 0) _picker.SelectedIndex = index;
    }

    public bool IsLocked
    {
        get => !_picker.IsEnabled;
        set
        {
            _picker.IsEnabled = !value;
            _box.Opacity = value ? 0.7 : 1;
        }
    }

    public void SetError(string message)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        _error.Text = message ?? string.Empty;
        _error.IsVisible = invalid;
        _box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }
}
