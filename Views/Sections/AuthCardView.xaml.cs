using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Sections;

/// <summary>
/// The sign-in card in the hero. Displays whatever the controller tells it to and reports
/// user intent via events. Contains no validation or account logic.
/// The "Create account" tab does not switch the card; it asks for the registration modal.
/// </summary>
public partial class AuthCardView : ContentView, IAuthFormView
{
    private readonly List<AuthTab> _tabs = new();
    private readonly InputFieldSet _fields = new();

    public AuthCardView()
    {
        InitializeComponent();

        _fields.SubmitRequested += (s, e) => RaiseSubmit();
        _fields.ValueChanged += (s, e) => ShowStatus(null);
    }

    /// <summary>The user clicked a tab.</summary>
    public event EventHandler<AuthModeEventArgs> ModeRequested;

    /// <summary>The user pressed the submit button or Enter in a field.</summary>
    public event EventHandler<AuthSubmitEventArgs> SubmitRequested;

    /// <summary>The user clicked the first-time Super Admin setup link.</summary>
    public event EventHandler<AuthModeEventArgs> SetupRequested;

    public void ShowSetupLink(bool visible) => SetupLink.IsVisible = visible;

    private void OnSetupClicked(object sender, EventArgs e) =>
        SetupRequested?.Invoke(this, new AuthModeEventArgs(AuthMode.SetupSuperAdmin, SetupLink));

    public void SetEyebrow(string text) => EyebrowLabel.Text = text;

    /// <summary>Tighter padding for narrow windows.</summary>
    public void SetCompact(bool compact)
    {
        Card.Padding = compact ? new Thickness(20, 28) : new Thickness(40);
        TitleLabel.FontSize = compact ? 32 : 38;
    }

    /// <summary>Shows <paramref name="form"/> in the card, with one tab per form in <paramref name="tabs"/>.</summary>
    public void ShowMode(AuthModeDefinition form, IEnumerable<AuthModeDefinition> tabs)
    {
        EnsureTabs(tabs);
        foreach (var tab in _tabs) tab.IsActive = tab.Mode == form.Mode;

        TitleLabel.Text = form.Heading;
        SubtextLabel.Text = form.Subtext;
        _submitText = form.SubmitLabel + "  →";
        SubmitButton.Text = _submitText;
        FootnoteLabel.Text = form.Footnote;
        ShowStatus(null);

        _fields.Build(form.Fields, _ => FieldsStack);
    }

    private string _submitText;

    public void SetBusy(bool busy, string busyText = null)
    {
        SubmitButton.IsEnabled = !busy;
        SubmitButton.Text = busy ? busyText ?? "Please wait…" : _submitText;
    }

    public void SetFieldValue(string fieldName, string value) => _fields.SetValue(fieldName, value);

    /// <summary>Highlights a tab without changing the form (e.g. "Create account" while its modal is open).</summary>
    public void SetActiveTab(AuthMode mode)
    {
        foreach (var tab in _tabs) tab.IsActive = tab.Mode == mode;
    }

    /// <summary>Shows or hides a tab (e.g. "Create account" when clinic sign-up is turned off); the others fill the row.</summary>
    public void SetTabVisible(AuthMode mode, bool visible)
    {
        var tab = _tabs.FirstOrDefault(t => t.Mode == mode);
        if (tab == null) return;
        tab.IsVisible = visible;
        foreach (var other in _tabs.Where(t => t != tab)) Grid.SetColumnSpan(other, visible ? 1 : _tabs.Count);
    }

    public void ShowErrors(IDictionary<string, string> errors) => _fields.ShowErrors(errors);

    /// <summary>
    /// A clear warning above the form when sign-in is blocked (e.g. "This computer is blocked"),
    /// so it isn't mistaken for a typing mistake. Null hides it.
    /// </summary>
    public void ShowWarning(string title, string message, string icon = "triangle-alert")
    {
        WarningBox.IsVisible = !string.IsNullOrEmpty(message);
        WarningTitle.Text = title ?? string.Empty;
        WarningTitle.IsVisible = !string.IsNullOrEmpty(title);
        WarningText.Text = message ?? string.Empty;
        if (WarningBox.IsVisible) StatusLabel.IsVisible = false;
    }

    public void ShowStatus(string message)
    {
        if (!string.IsNullOrEmpty(message)) WarningBox.IsVisible = false;
        StatusLabel.Text = message ?? string.Empty;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void ClearInputs() => _fields.ClearInputs();

    public void FocusFirstInput() => _fields.FocusFirst();

    private void EnsureTabs(IEnumerable<AuthModeDefinition> tabs)
    {
        foreach (var definition in tabs)
        {
            if (_tabs.Any(t => t.Mode == definition.Mode)) continue;

            var tab = new AuthTab(definition.Mode, definition.TabLabel);
            tab.Clicked += (s, e) => ModeRequested?.Invoke(this, new AuthModeEventArgs(tab.Mode, tab));
            Grid.SetColumn(tab, _tabs.Count);
            _tabs.Add(tab);
            TabsGrid.Children.Add(tab);
        }
    }

    private void OnSubmitClicked(object sender, EventArgs e) => RaiseSubmit();

    private void RaiseSubmit() =>
        SubmitRequested?.Invoke(this, new AuthSubmitEventArgs(_fields.ReadValues()));
}
