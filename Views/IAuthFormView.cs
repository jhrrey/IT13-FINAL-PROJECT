namespace VetCare.Views;

/// <summary>
/// A view that shows an account form (the sign-in card or the registration modal).
/// Lets the controller report results the same way for both.
/// </summary>
public interface IAuthFormView
{
    /// <summary>Shows field-name → message errors (empty clears them) and focuses the first invalid field.</summary>
    void ShowErrors(IDictionary<string, string> errors);

    /// <summary>Shows a short message under the submit button; null or empty hides it.</summary>
    void ShowStatus(string message);

    /// <summary>While busy the submit button is disabled and shows <paramref name="busyText"/>.</summary>
    void SetBusy(bool busy, string busyText = null);

    void SetFieldValue(string fieldName, string value);

    void ClearInputs();

    void FocusFirstInput();
}
