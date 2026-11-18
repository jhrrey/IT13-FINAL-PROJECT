using System.Globalization;

namespace VetCare.Views.Controls;

/// <summary>
/// A date input in the same rounded box as the text inputs: shows the picked date (or a gray
/// placeholder) with a calendar icon. An invisible DatePicker lies on top, so a click anywhere
/// opens the Windows calendar. Unlike DatePicker, it can be empty.
/// </summary>
public sealed class DateBox : ContentView
{
    private readonly Label _text;
    private readonly DatePicker _picker;
    private bool _picked;

    public DateBox()
    {
        _text = new Label { FontSize = 16, VerticalOptions = LayoutOptions.Center };
        _picker = new DatePicker
        {
            Format = "MMM dd, yyyy",
            Date = DateTime.Today,
            Opacity = 0.01, // invisible, but still opens the calendar
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _picker.DateSelected += (s, e) =>
        {
            _picked = true;
            UpdateText();
            DateChanged?.Invoke(this, EventArgs.Empty);
        };

        var face = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        face.Add(_text, 0, 0);
        face.Add(new LucideIcon { Icon = "calendar", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center }, 1, 0);

        Box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Content = new Grid { Children = { face, _picker } } };
        HandCursor.Apply(Box);
        Content = Box;
        UpdateText();
    }

    /// <summary>A date was picked.</summary>
    public event EventHandler DateChanged;

    /// <summary>The rounded box (for the orange invalid border).</summary>
    public Border Box { get; }

    private string _placeholder = "Select date";

    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            UpdateText();
        }
    }

    /// <summary>The picked date, or null.</summary>
    public DateTime? Date
    {
        get => _picked ? _picker.Date.Date : null;
        set
        {
            _picked = value != null;
            if (value != null) _picker.Date = value.Value.Date;
            UpdateText();
        }
    }

    public DateTime MinimumDate
    {
        get => _picker.MinimumDate;
        set => _picker.MinimumDate = value;
    }

    public DateTime MaximumDate
    {
        get => _picker.MaximumDate;
        set => _picker.MaximumDate = value;
    }

    private void UpdateText()
    {
        _text.Text = _picked ? _picker.Date.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) : Placeholder;
        _text.TextColor = ThemeResources.Color(_picked ? "Navy" : "Placeholder");
    }
}
