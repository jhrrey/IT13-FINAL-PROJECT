using VetCare.Models;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// A centered dialog: either a yes/no confirmation (e.g. "Deactivate Maria Santos?")
/// or the Reset password form (new temporary password + confirm).
/// </summary>
public partial class ConfirmDialogView : ContentView
{
    private readonly InputFieldSet _fields = new();
    private string _actionText;

    public ConfirmDialogView()
    {
        InitializeComponent();

        CancelButton.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        ActionButton.Clicked += (s, e) => RaiseConfirm();
        _fields.SubmitRequested += (s, e) => RaiseConfirm();
        _fields.ValueChanged += (s, e) => ShowMessage(null);

        var dimTap = new TapGestureRecognizer();
        dimTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        Dim.GestureRecognizers.Add(dimTap);
    }

    /// <summary>The action button was pressed. For the reset form, the values hold the passwords.</summary>
    public event EventHandler<AuthSubmitEventArgs> Confirmed;

    public event EventHandler CancelRequested;

    public bool IsOpen => IsVisible;

    /// <param name="showCancel">False shows a single button (e.g. "OK" for a message that only informs).</param>
    /// <param name="cancelText">The second button's text (e.g. "Keep it").</param>
    public Task ShowConfirmAsync(string title, string message, string actionText, bool danger, string icon = "user-x", bool showCancel = true,
        string cancelText = "Cancel")
    {
        Configure(title, message, actionText, danger, icon);
        CancelButton.Text = cancelText;
        CancelButton.IsVisible = showCancel;
        Grid.SetColumn(ActionButton, showCancel ? 1 : 0);
        Grid.SetColumnSpan(ActionButton, showCancel ? 1 : 2);
        _fields.Build(Array.Empty<AuthField>(), _ => FieldsStack);
        FieldsStack.IsVisible = false;
        return OpenAsync();
    }

    /// <summary>A confirmation with one optional multi-line note (its value comes back as <paramref name="fieldName"/>).</summary>
    public Task ShowConfirmWithNoteAsync(string title, string message, string actionText, bool danger, string icon,
        string fieldName, string noteLabel, string notePlaceholder, int maxLength, string cancelText = "Cancel")
    {
        Configure(title, message, actionText, danger, icon);
        CancelButton.Text = cancelText;
        CancelButton.IsVisible = true;
        Grid.SetColumn(ActionButton, 1);
        Grid.SetColumnSpan(ActionButton, 1);

        _fields.Build(new[] { new AuthField(fieldName, noteLabel, notePlaceholder, AuthFieldKind.TextArea, maxLength: maxLength) }, _ => FieldsStack);
        FieldsStack.IsVisible = true;
        return OpenAsync();
    }

    public Task ShowResetPasswordAsync(string userName)
    {
        CancelButton.Text = "Cancel";
        Configure("Reset password",
            "Set a new temporary password for " + userName + ". Share it with them; they should change it after signing in.",
            "Reset password", danger: false, icon: "key-round");
        CancelButton.IsVisible = true;
        Grid.SetColumn(ActionButton, 1);
        Grid.SetColumnSpan(ActionButton, 1);

        _fields.Build(new[]
        {
            new AuthField(UserFormData.PasswordField, "New temporary password", VetCare.Helpers.Validator.PasswordHint, AuthFieldKind.Password),
            new AuthField(UserFormData.ConfirmPasswordField, "Confirm password", "Re-enter the password", AuthFieldKind.Password)
        }, _ => FieldsStack);
        FieldsStack.IsVisible = true;

        return OpenAsync();
    }

    public void ShowResult(OperationResult result)
    {
        _fields.ShowErrors(result.FieldErrors);
        ShowMessage(result.Message);
    }

    public void SetBusy(bool busy, bool offline = false)
    {
        ActionButton.IsEnabled = !busy && !offline;
        ActionButton.Text = busy ? "Please wait…" : _actionText;
    }

    public async Task HideAsync()
    {
        if (!IsVisible) return;
        await Task.WhenAll(this.FadeTo(0, 140, Easing.CubicIn), Card.ScaleTo(0.97, 140, Easing.CubicIn));
        IsVisible = false;
    }

    private void Configure(string title, string message, string actionText, bool danger, string icon)
    {
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        _actionText = actionText;
        ActionButton.Text = actionText;
        ActionButton.IsEnabled = true;
        ActionButton.Style = ThemeResources.Get<Style>(danger ? "DangerButton" : "WorkspaceButton");

        Icon.Icon = icon;
        Icon.IconColor = ThemeResources.Color(danger ? "Danger" : "Navy");
        Icon.CircleColor = ThemeResources.Color(danger ? "DangerSoft" : "Cyan");
        ShowMessage(null);
    }

    private async Task OpenAsync()
    {
        Opacity = 0;
        Card.Scale = 0.97;
        IsVisible = true;
        await Task.WhenAll(this.FadeTo(1, 160, Easing.CubicOut), Card.ScaleTo(1, 160, Easing.CubicOut));
        if (FieldsStack.IsVisible) _fields.FocusFirst();
    }

    private void RaiseConfirm()
    {
        if (!ActionButton.IsEnabled) return;
        Confirmed?.Invoke(this, new AuthSubmitEventArgs(_fields.ReadValues()));
    }

    private void ShowMessage(string message)
    {
        ErrorLabel.Text = message ?? string.Empty;
        ErrorLabel.IsVisible = !string.IsNullOrEmpty(message);
    }
}
