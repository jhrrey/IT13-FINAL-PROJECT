using VetCare.Models;

namespace VetCare.Views.Controls;

/// <summary>
/// Label + rounded input + inline error for one <see cref="AuthField"/>.
/// Single-line fields are 52px tall; <see cref="AuthFieldKind.TextArea"/> fields show 3 rows.
/// The box turns white with an orange border while focused, and orange-bordered when invalid.
/// </summary>
public sealed class InputField : ContentView
{
    private readonly Border _box;
    private readonly InputView _input;
    private readonly Label _error;
    private bool _invalid;

    public InputField(AuthField field)
    {
        FieldName = field.Name;

        var label = new Label { Text = field.Label, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };

        if (field.Kind == AuthFieldKind.TextArea)
        {
            var editor = new Editor
            {
                FontFamily = "DMSans",
                FontSize = 16,
                TextColor = ThemeResources.Color("Navy"),
                PlaceholderColor = ThemeResources.Color("Placeholder"),
                BackgroundColor = Colors.Transparent,
                Placeholder = field.Placeholder,
                HeightRequest = 84,
                AutoSize = EditorAutoSizeOption.Disabled
            };
            _input = editor;
        }
        else
        {
            var entry = new Entry
            {
                Style = ThemeResources.Get<Style>("InputEntry"),
                Placeholder = field.Placeholder,
                IsPassword = field.Kind == AuthFieldKind.Password,
                Keyboard = field.Kind switch
                {
                    AuthFieldKind.Email => Keyboard.Email,
                    AuthFieldKind.Phone => Keyboard.Telephone,
                    _ => Keyboard.Default
                }
            };
            entry.Completed += (s, e) => SubmitRequested?.Invoke(this, EventArgs.Empty);
            _input = entry;
        }

        // Stop typing at the database column's length (phone numbers are checked by digit count instead).
        if (field.MaxLength > 0 && field.Kind != AuthFieldKind.Phone) _input.MaxLength = field.MaxLength;

        _input.Focused += (s, e) => UpdateVisual();
        _input.Unfocused += (s, e) => UpdateVisual();
        _input.TextChanged += (s, e) =>
        {
            SetError(null);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };

        _box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = _input };
        if (field.Kind == AuthFieldKind.TextArea)
        {
            _box.HeightRequest = 100;
            _box.Padding = new Thickness(12, 6);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => _input.Focus();
        _box.GestureRecognizers.Add(tap);

        _error = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _error.StyleClass = new[] { "field-error" };

        Content = new VerticalStackLayout { Children = { label, _box, _error } };
    }

    public event EventHandler ValueChanged;

    /// <summary>Raised when Enter is pressed in a single-line input.</summary>
    public event EventHandler SubmitRequested;

    public string FieldName { get; }

    public string Value
    {
        get => _input.Text ?? string.Empty;
        set => _input.Text = value ?? string.Empty;
    }

    public void FocusInput() => _input.Focus();

    public bool IsInputFocused => _input.IsFocused;

    public void SetError(string message)
    {
        _invalid = !string.IsNullOrEmpty(message);
        _error.Text = message ?? string.Empty;
        _error.IsVisible = _invalid;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        bool focused = _input.IsFocused;
        _box.BackgroundColor = ThemeResources.Color(focused ? "Surface" : "InputBackground");
        _box.Stroke = ThemeResources.Color(focused || _invalid ? "Orange" : "BorderColor");
    }
}
