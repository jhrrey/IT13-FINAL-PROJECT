using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Add / Edit medicine (side panel, 460px). The stock can't be typed here when editing (rule 5):
/// it shows read-only with a note to use Stock in or Adjust stock.
/// </summary>
public sealed class MedicineFormView : ContentView
{
    public const double PanelWidth = 460;

    private readonly bool _adding;
    private readonly Border _nameBox, _priceBox, _reorderBox, _startBox;
    private readonly Entry _name, _price, _reorder, _start;
    private readonly PickerField _category, _unit;
    private readonly DateBox _expiry;
    private readonly Label _nameError, _priceError, _reorderError, _startError, _expiryError, _message;
    private readonly HoverButton _save;

    /// <param name="medicine">Null to add a new medicine.</param>
    public MedicineFormView(Medicine medicine)
    {
        _adding = medicine == null;

        (_nameBox, _name) = PharmacyKit.TextInput("e.g. Amoxicillin 250 mg", maxLength: MedicineFormData.NameMaxLength);
        _nameError = PharmacyKit.ErrorLabel();
        _category = new PickerField(MedicineFormData.CategoryField, "Category *");
        _category.SetItems(Medicine.Categories.Select(c => (c, (object)c)), medicine?.Category);
        _unit = new PickerField(MedicineFormData.UnitField, "Unit *");
        _unit.SetItems(Medicine.Units.Select(u => (u, (object)u)), medicine?.Unit);
        (_priceBox, _price) = PharmacyKit.TextInput("0.00", Keyboard.Numeric, 12, "₱");
        _priceError = PharmacyKit.ErrorLabel();
        (_reorderBox, _reorder) = PharmacyKit.TextInput("e.g. 20", Keyboard.Numeric, 7);
        _reorderError = PharmacyKit.ErrorLabel();
        _expiry = new DateBox { Placeholder = "No expiry date", MinimumDate = DateTime.Today.AddYears(-5), MaximumDate = DateTime.Today.AddYears(15), Date = medicine?.ExpiryDate };
        _expiryError = PharmacyKit.ErrorLabel();
        (_startBox, _start) = PharmacyKit.TextInput("0", Keyboard.Numeric, 7);
        _startError = PharmacyKit.ErrorLabel();
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        if (medicine != null)
        {
            _name.Text = medicine.MedicineName;
            _price.Text = medicine.UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            _reorder.Text = medicine.ReorderLevel.ToString();
        }

        _name.TextChanged += (s, e) => PharmacyKit.SetError(_nameError, null, _nameBox);
        _price.TextChanged += (s, e) => PharmacyKit.SetError(_priceError, null, _priceBox);
        _reorder.TextChanged += (s, e) => PharmacyKit.SetError(_reorderError, null, _reorderBox);
        _start.TextChanged += (s, e) => PharmacyKit.SetError(_startError, null, _startBox);
        _expiry.DateChanged += (s, e) => PharmacyKit.SetError(_expiryError, null, _expiry.Box);
        _category.SelectionChanged += (s, e) => _category.SetError(null);
        _unit.SelectionChanged += (s, e) => _unit.SetError(null);

        var pair = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        pair.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Unit price *"), _priceBox, _priceError } }, 0, 0);
        pair.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Reorder level *"), _reorderBox, _reorderError } }, 1, 0);

        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children =
            {
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Medicine name *"), _nameBox, _nameError } },
                _category,
                _unit,
                pair,
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Expiry date"), _expiry, PharmacyKit.Hint("The nearest expiry date of the stock on the shelf."), _expiryError } }
            }
        };

        if (_adding)
        {
            form.Children.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Starting stock"), _startBox, PharmacyKit.Hint("Saved as the first “Stock In” in the history."), _startError } });
        }
        else
        {
            form.Children.Add(new VerticalStackLayout
            {
                Children =
                {
                    PharmacyKit.FieldLabel("Current stock"),
                    new Label { Text = medicine.StockQty.ToString("N0") + " " + medicine.Unit, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                    PharmacyKit.Hint("Use Stock in or Adjust stock to change the quantity.")
                }
            });
        }
        form.Children.Add(_message);

        _save = new HoverButton { Text = "Save medicine", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame(_adding ? "Add medicine" : "Edit medicine", null, form, () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public MedicineFormData Read() => new()
    {
        Name = _name.Text,
        Category = _category.SelectedValue as string,
        Unit = _unit.SelectedValue as string,
        Price = _price.Text,
        ReorderLevel = _reorder.Text,
        ExpiryDate = _expiry.Date,
        StartingStock = _adding ? _start.Text : null
    };

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_nameError, Get(MedicineFormData.NameField), _nameBox);
        _category.SetError(Get(MedicineFormData.CategoryField));
        _unit.SetError(Get(MedicineFormData.UnitField));
        PharmacyKit.SetError(_priceError, Get(MedicineFormData.PriceField), _priceBox);
        PharmacyKit.SetError(_reorderError, Get(MedicineFormData.ReorderField), _reorderBox);
        PharmacyKit.SetError(_expiryError, Get(MedicineFormData.ExpiryField), _expiry.Box);
        PharmacyKit.SetError(_startError, Get(MedicineFormData.StartingStockField), _startBox);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Save medicine";
    }
}
