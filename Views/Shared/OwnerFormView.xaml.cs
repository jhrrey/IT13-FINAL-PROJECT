using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// The Add / Edit owner form. Returns what was typed and shows the controller's validation
/// results and duplicate warning. Contains no rules itself.
/// </summary>
public partial class OwnerFormView : ContentView
{
    private readonly OwnerFormData _initial;
    private readonly InputFieldSet _fields = new();

    /// <summary>The contact number (digits) the duplicate warning was shown for, or null.</summary>
    private string _warnedContact;
    private int _duplicateOwnerId;

    public OwnerFormView(OwnerFormData initial)
    {
        InitializeComponent();
        ScrollFocus.DontScrollOnFocusWhileOpening(FormScroll);
        _initial = initial;

        TitleLabel.Text = initial.IsNew ? "Add owner" : "Edit owner";

        // Max lengths match the Owner table (names 50, contact 20, email 100, address 255).
        _fields.Build(new[]
        {
            new AuthField(OwnerFormData.FirstNameField, "First name *", "e.g. Juan", AuthFieldKind.Text, maxLength: 50),
            new AuthField(OwnerFormData.LastNameField, "Last name *", "e.g. Dela Cruz", AuthFieldKind.Text, maxLength: 50),
            new AuthField(OwnerFormData.ContactNoField, "Contact no. *", "09XX XXX XXXX", AuthFieldKind.Phone, maxLength: 20),
            new AuthField(OwnerFormData.EmailField, "Email", "name@example.com", AuthFieldKind.Email, maxLength: 100),
            new AuthField(OwnerFormData.AddressField, "Address", "e.g. Matina, Davao City", AuthFieldKind.TextArea, maxLength: 255)
        }, _ => FieldsStack);

        _fields.SetValue(OwnerFormData.FirstNameField, initial.FirstName);
        _fields.SetValue(OwnerFormData.LastNameField, initial.LastName);
        _fields.SetValue(OwnerFormData.ContactNoField, string.IsNullOrEmpty(initial.ContactNo) ? null : Validator.FormatPhMobile(initial.ContactNo));
        _fields.SetValue(OwnerFormData.EmailField, initial.Email);
        _fields.SetValue(OwnerFormData.AddressField, initial.Address);

        _fields.SubmitRequested += (s, e) => SaveRequested?.Invoke(this, false);
        _fields.ValueChanged += (s, e) =>
        {
            ShowMessage(null);
            // Changing the contact number makes the duplicate warning stale.
            if (_warnedContact != null && Validator.Digits(CurrentContact()) != _warnedContact) HideDuplicate();
        };

        SaveButton.Clicked += (s, e) => SaveRequested?.Invoke(this, false);
        SaveAndAddPetButton.Clicked += (s, e) => SaveRequested?.Invoke(this, true);
        CancelButton.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        ViewDuplicateLink.Clicked += (s, e) => ViewDuplicateRequested?.Invoke(this, _duplicateOwnerId);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);
        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);

        UpdateButtonText();
    }

    /// <summary>Save pressed; the argument is true for "Save and add pet".</summary>
    public event EventHandler<bool> SaveRequested;

    public event EventHandler CancelRequested;

    /// <summary>The "View owner" link in the duplicate warning (argument: that owner's ID).</summary>
    public event EventHandler<int> ViewDuplicateRequested;

    public OwnerFormData Read()
    {
        var values = _fields.ReadValues();
        string Get(string name) => values.TryGetValue(name, out var v) ? v : null;

        return new OwnerFormData
        {
            OwnerId = _initial.OwnerId,
            FirstName = Get(OwnerFormData.FirstNameField),
            LastName = Get(OwnerFormData.LastNameField),
            ContactNo = Get(OwnerFormData.ContactNoField),
            Email = Get(OwnerFormData.EmailField),
            Address = Get(OwnerFormData.AddressField),
            DuplicateConfirmed = _warnedContact != null && Validator.Digits(Get(OwnerFormData.ContactNoField)) == _warnedContact
        };
    }

    public void ShowResult(OperationResult result)
    {
        _fields.ShowErrors(result.FieldErrors);
        ShowMessage(result.Message);
    }

    /// <summary>Shows the amber warning; the next save with the same number goes through ("Save anyway").</summary>
    public void ShowDuplicate(Owner existing)
    {
        _warnedContact = Validator.Digits(CurrentContact());
        _duplicateOwnerId = existing.OwnerId;
        DuplicateLabel.Text = "An owner with this contact number already exists: " + existing.FullName + ".";
        DuplicateBox.IsVisible = true;
        UpdateButtonText();
    }

    public void SetBusy(bool busy)
    {
        SaveButton.IsEnabled = !busy;
        SaveAndAddPetButton.IsEnabled = !busy;
        if (busy) SaveButton.Text = "Saving…";
        else UpdateButtonText();
    }

    private string CurrentContact() =>
        _fields.ReadValues().TryGetValue(OwnerFormData.ContactNoField, out var v) ? v : null;

    private void HideDuplicate()
    {
        _warnedContact = null;
        DuplicateBox.IsVisible = false;
        UpdateButtonText();
    }

    private void UpdateButtonText()
    {
        bool warned = _warnedContact != null;
        SaveButton.Text = warned ? "Save anyway" : "Save owner";
        SaveAndAddPetButton.Text = warned ? "Save anyway and add pet" : "Save and add pet";
    }

    private void ShowMessage(string message)
    {
        MessageLabel.Text = message ?? string.Empty;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(message);
    }
}
