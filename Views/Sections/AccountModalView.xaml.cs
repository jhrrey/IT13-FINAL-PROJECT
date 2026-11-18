using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

/// <summary>
/// The account modal: clinic registration ("Create account") or the one-time Super Admin setup.
/// Displays the form the controller gives it and reports user intent via events.
/// Contains no validation or account logic.
/// </summary>
public partial class AccountModalView : ContentView, IAuthFormView
{
    /// <summary>Below this window width the two columns stack into one.</summary>
    private const double TwoColumnMinWidth = 760;

    private readonly InputFieldSet _fields = new();
    private bool _open;

    public AccountModalView()
    {
        InitializeComponent();

        _fields.SubmitRequested += (s, e) => RaiseSubmit();
        _fields.ValueChanged += (s, e) => ShowStatus(null);

        Backdrop.Tapped += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);

        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);
    }

    /// <summary>The user pressed "Create account" or Enter in a field.</summary>
    public event EventHandler<AuthSubmitEventArgs> SubmitRequested;

    /// <summary>The user clicked the close button or the blurred background.</summary>
    public event EventHandler CloseRequested;

    public bool IsOpen => _open;

    public void SetEyebrow(string text) => EyebrowLabel.Text = text;

    /// <summary>The form currently in the modal.</summary>
    public AuthMode? Mode { get; private set; }

    /// <summary>
    /// Fills the modal from a form definition (fields with Column 1 go on the right).
    /// Showing the form that is already there keeps whatever the user typed.
    /// </summary>
    public void ShowForm(AuthModeDefinition form)
    {
        if (Mode == form.Mode) return;
        Mode = form.Mode;

        TitleLabel.Text = form.Heading;
        SubtextLabel.Text = form.Subtext;
        _submitText = form.SubmitLabel + "  →";
        SubmitButton.Text = _submitText;
        FootnoteLabel.Text = form.Footnote;
        ShowStatus(null);

        _fields.Build(form.Fields, field => field.Column == 1 ? RightFields : LeftColumn);
    }

    private string _submitText;

    public void SetBusy(bool busy, string busyText = null)
    {
        SubmitButton.IsEnabled = !busy;
        SubmitButton.Text = busy ? busyText ?? "Please wait…" : _submitText;
    }

    public void SetFieldValue(string fieldName, string value) => _fields.SetValue(fieldName, value);

    /// <summary>
    /// Opens the modal. If <paramref name="origin"/> is given (the "Create account" tab), the dialog
    /// grows out of it into the centre while the page behind blurs in, so the click flows into the modal.
    /// </summary>
    public async Task OpenAsync(VisualElement origin = null)
    {
        if (_open) return;
        _open = true;
        _origin = origin;

        Backdrop.Opacity = 0;
        Dialog.Opacity = 0;
        Dialog.TranslationX = 0;
        Dialog.TranslationY = 0;
        Dialog.Scale = 1;
        IsVisible = true;

        await WaitForLayoutAsync();
        var start = StartPoint();
        Dialog.TranslationX = start.X;
        Dialog.TranslationY = start.Y;
        Dialog.Scale = start.Scale;

        await Task.WhenAll(
            Backdrop.FadeTo(1, 280, Easing.CubicOut),
            Dialog.FadeTo(1, 180, Easing.CubicOut),
            Dialog.TranslateTo(0, 0, OpenDuration, Easing.CubicOut),
            Dialog.ScaleTo(1, OpenDuration, Easing.CubicOut));

        _fields.FocusFirst();
    }

    /// <summary>Closes the modal, shrinking the dialog back into the tab it came from.</summary>
    public async Task CloseAsync()
    {
        if (!_open) return;
        _open = false;

        var end = StartPoint();
        await Task.WhenAll(
            Backdrop.FadeTo(0, CloseDuration, Easing.CubicIn),
            Dialog.FadeTo(0, CloseDuration, Easing.CubicIn),
            Dialog.TranslateTo(end.X, end.Y, CloseDuration, Easing.CubicIn),
            Dialog.ScaleTo(end.Scale, CloseDuration, Easing.CubicIn));

        IsVisible = false;
        Dialog.TranslationX = 0;
        Dialog.TranslationY = 0;
        Dialog.Scale = 1;

        // Keep what was typed (in case the modal was closed by accident), but drop old messages.
        _fields.ClearErrors();
        ShowStatus(null);
    }

    private const uint OpenDuration = 340;
    private const uint CloseDuration = 220;

    private VisualElement _origin;

    /// <summary>Where the dialog starts (and ends): over the origin tab, scaled down to roughly its width.</summary>
    private (double X, double Y, double Scale) StartPoint()
    {
        if (_origin == null || _origin.Width <= 0 || Dialog.Width <= 0 || Parent == null)
            return (0, 24, 0.96); // no origin: a small rise from below

        var root = Parent;
        var from = ViewPosition.CenterIn(_origin, root);
        var to = ViewPosition.CenterIn(Dialog, root);

        // CenterIn includes the current translation; measure from the resting position.
        to = new Point(to.X - Dialog.TranslationX, to.Y - Dialog.TranslationY);

        double scale = Math.Clamp(_origin.Width / Dialog.Width, 0.15, 1);
        return (from.X - to.X, from.Y - to.Y, scale);
    }

    /// <summary>The dialog has no size until it has been laid out once after becoming visible.</summary>
    private async Task WaitForLayoutAsync()
    {
        for (int i = 0; i < 20 && Dialog.Width <= 0; i++)
            await Task.Delay(16);
    }

    /// <summary>Fits the dialog to the window: two columns when there is room, one otherwise.</summary>
    public void ApplySize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        bool twoColumns = width >= TwoColumnMinWidth;

        Dialog.WidthRequest = Math.Min(880, width - 32);
        Dialog.MaximumHeightRequest = Math.Max(320, height - 48);
        Body.Padding = twoColumns ? new Thickness(40) : new Thickness(24, 28);

        if (twoColumns)
        {
            ColumnsGrid.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(RightColumn, 1);
            Grid.SetRow(RightColumn, 0);
        }
        else
        {
            ColumnsGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(RightColumn, 0);
            Grid.SetRow(RightColumn, 1);
        }
    }

    public void ShowErrors(IDictionary<string, string> errors) => _fields.ShowErrors(errors);

    public void ShowStatus(string message)
    {
        StatusLabel.Text = message ?? string.Empty;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void ClearInputs() => _fields.ClearInputs();

    public void FocusFirstInput() => _fields.FocusFirst();

    private void OnSubmitClicked(object sender, EventArgs e) => RaiseSubmit();

    private void RaiseSubmit() =>
        SubmitRequested?.Invoke(this, new AuthSubmitEventArgs(_fields.ReadValues()));
}
