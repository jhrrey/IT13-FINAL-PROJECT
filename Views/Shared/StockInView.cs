using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>Stock in (side panel, 420px): quantity received, a new expiry date and the supplier / reference.</summary>
public sealed class StockInView : ContentView
{
    public const double PanelWidth = 420;

    private readonly Medicine _medicine;
    private readonly Border _quantityBox, _remarksBox;
    private readonly Entry _quantity, _remarks;
    private readonly DateBox _expiry;
    private readonly Label _quantityError, _expiryError, _remarksError, _preview, _message;
    private readonly HoverButton _save;

    public StockInView(Medicine medicine)
    {
        _medicine = medicine;
        (_quantityBox, _quantity) = PharmacyKit.TextInput("e.g. 100", Keyboard.Numeric, 7);
        _quantityError = PharmacyKit.ErrorLabel();
        _expiry = new DateBox { Placeholder = "Keep the current date", MinimumDate = DateTime.Today, MaximumDate = DateTime.Today.AddYears(15) };
        _expiryError = PharmacyKit.ErrorLabel();
        (_remarksBox, _remarks) = PharmacyKit.TextInput("e.g. PetMed Supply, DR-10234", maxLength: 255);
        _remarksError = PharmacyKit.ErrorLabel();
        var (previewBox, preview) = PharmacyKit.InfoBox("CyanSoft", "Navy");
        _preview = preview;
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        _quantity.TextChanged += (s, e) => { PharmacyKit.SetError(_quantityError, null, _quantityBox); UpdatePreview(); };
        _expiry.DateChanged += (s, e) => PharmacyKit.SetError(_expiryError, null, _expiry.Box);
        UpdatePreview();

        string current = medicine.ExpiryDate == null ? "No expiry date saved yet." : "Current expiry: " + medicine.ExpiryDate.Value.ToString("MMM dd, yyyy") + ".";
        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children =
            {
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Quantity received *"), _quantityBox, _quantityError } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("New expiry date"), _expiry, PharmacyKit.Hint(current), _expiryError } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Supplier / reference"), _remarksBox, _remarksError } },
                previewBox,
                _message
            }
        };

        _save = new HoverButton { Text = "Add stock", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame("Stock in", medicine.MedicineName + " · " + medicine.Unit, form,
            () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public StockChangeData Read() => new() { Quantity = _quantity.Text, NewExpiry = _expiry.Date, Remarks = _remarks.Text };

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_quantityError, Get(StockChangeData.QuantityField), _quantityBox);
        PharmacyKit.SetError(_expiryError, Get(StockChangeData.ExpiryField), _expiry.Box);
        PharmacyKit.SetError(_remarksError, Get(StockChangeData.RemarksField), _remarksBox);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Add stock";
    }

    /// <summary>"Current stock: 12 → New stock: 112".</summary>
    private void UpdatePreview()
    {
        int added = PharmacyKit.ParseCount(_quantity.Text) ?? 0;
        _preview.Text = "Current stock: " + _medicine.StockQty.ToString("N0") + "  →  New stock: " + (_medicine.StockQty + (long)added).ToString("N0");
    }
}
