using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>The values typed in the Receive payment panel (raw text; BillingController validates).</summary>
public sealed record PaymentInput(string Method, string Amount, string AmountReceived, string ReferenceNo);

/// <summary>
/// Receive payment (section 3.C, side panel 440px): the balance due, the method chips, the amount (defaults to the
/// whole balance), the cash received with the live change, or the reference no. for e-payments.
/// </summary>
public sealed class ReceivePaymentView : ContentView
{
    public const double PanelWidth = 440;
    public const string AmountField = "amount";
    public const string ReceivedField = "received";
    public const string ReferenceField = "reference";

    private readonly Bill _bill;
    private readonly List<(string Method, Border Chip, Label Text, LucideIcon Icon)> _chips = new();
    private readonly Border _amountBox, _receivedBox, _referenceBox, _changeBox;
    private readonly Entry _amount, _received, _reference;
    private readonly Label _amountError, _receivedError, _referenceError, _change, _message;
    private readonly View _cashSection, _referenceSection;
    private readonly HoverButton _confirm;
    private string _method = PaymentMethods.Cash;

    public ReceivePaymentView(Bill bill)
    {
        _bill = bill;

        // Summary: total, paid and the balance due (large).
        var summary = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        summary.Add(Small("Total"), 0, 0);
        summary.Add(Small(CurrencyHelper.Peso(bill.TotalAmount), end: true), 1, 0);
        summary.Add(Small("Paid"), 0, 1);
        summary.Add(Small(CurrencyHelper.Peso(bill.Paid), end: true), 1, 1);
        summary.Add(new Label { Text = "Balance due", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 2);
        summary.Add(new Label { Text = CurrencyHelper.Peso(bill.Balance), FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.End }, 1, 2);
        var summaryBox = new Border { Padding = new Thickness(16, 14), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, BackgroundColor = ThemeResources.Color("CyanSoft"), Content = summary };

        // Method chips (wrap to two rows in 440px).
        var chips = new WrapLayout();
        foreach (string method in PaymentMethods.All) chips.Children.Add(Chip(method));

        (_amountBox, _amount) = PharmacyKit.TextInput("0.00", Keyboard.Numeric, 12, "₱");
        _amount.Text = bill.Balance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _amountError = PharmacyKit.ErrorLabel();
        (_receivedBox, _received) = PharmacyKit.TextInput("0.00", Keyboard.Numeric, 12, "₱");
        _receivedError = PharmacyKit.ErrorLabel();
        (_referenceBox, _reference) = PharmacyKit.TextInput("e.g. 1029384756", maxLength: 50);
        _referenceError = PharmacyKit.ErrorLabel();
        (_changeBox, _change) = PharmacyKit.InfoBox("Mint", "CompletedText");
        _change.FontFamily = "DMSansBold";
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        _amount.TextChanged += (s, e) => { PharmacyKit.SetError(_amountError, null, _amountBox); UpdateChange(); };
        _received.TextChanged += (s, e) => { PharmacyKit.SetError(_receivedError, null, _receivedBox); UpdateChange(); };
        _reference.TextChanged += (s, e) => PharmacyKit.SetError(_referenceError, null, _referenceBox);

        _cashSection = new VerticalStackLayout { Spacing = 12, Children = { new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Amount received"), _receivedBox, _receivedError } }, _changeBox } };
        _referenceSection = new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Reference no. *"), _referenceBox, _referenceError } };

        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children =
            {
                summaryBox,
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Payment method"), chips } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Amount to pay *"), _amountBox, PharmacyKit.Hint("Leave a balance for partial payment."), _amountError } },
                _cashSection,
                _referenceSection,
                _message
            }
        };

        _confirm = new HoverButton { Text = "Confirm payment", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _confirm.Clicked += (s, e) => ConfirmRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame("Receive payment", bill.NumberText + " · " + bill.OwnerName, form, () => CancelRequested?.Invoke(this, EventArgs.Empty), _confirm);
        SelectMethod(PaymentMethods.Cash);
    }

    public event EventHandler ConfirmRequested;
    public event EventHandler CancelRequested;

    public PaymentInput Read() => new(_method, _amount.Text, _received.Text, _reference.Text);

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_amountError, Get(AmountField), _amountBox);
        PharmacyKit.SetError(_receivedError, Get(ReceivedField), _receivedBox);
        PharmacyKit.SetError(_referenceError, Get(ReferenceField), _referenceBox);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _confirm.IsEnabled = !busy;
        _confirm.Text = busy ? "Saving…" : "Confirm payment";
    }

    private void SelectMethod(string method)
    {
        _method = method;
        foreach (var (m, chip, text, icon) in _chips)
        {
            bool on = m == method;
            chip.BackgroundColor = ThemeResources.Color(on ? "Cyan" : "Surface");
            chip.Stroke = ThemeResources.Color(on ? "Navy" : "BorderColor");
            text.FontFamily = on ? "DMSansBold" : "DMSans";
            text.TextColor = ThemeResources.Color(on ? "Navy" : "BodyText");
            icon.StrokeColor = ThemeResources.Color(on ? "Navy" : "BodyText");
        }
        bool cash = method == PaymentMethods.Cash;
        _cashSection.IsVisible = cash;
        _referenceSection.IsVisible = !cash;
        PharmacyKit.SetError(_referenceError, null, _referenceBox);
        PharmacyKit.SetError(_receivedError, null, _receivedBox);
        UpdateChange();
    }

    /// <summary>Rule 9: for cash, the change is shown (not saved).</summary>
    private void UpdateChange()
    {
        if (_change == null) return;
        bool hasAmount = CurrencyHelper.TryParse(_amount.Text, out decimal amount);
        bool hasReceived = CurrencyHelper.TryParse(_received.Text, out decimal received);
        _changeBox.IsVisible = hasAmount && hasReceived && received >= amount && amount > 0;
        _change.Text = "Change: " + CurrencyHelper.Peso(Math.Round(received, 2) - Math.Round(amount, 2));
    }

    private View Chip(string method)
    {
        var icon = new LucideIcon { Icon = PaymentMethods.IconFor(method), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
        var text = new Label { Text = method, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        var chip = new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 8, 8),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new HorizontalStackLayout { Spacing = 6, Children = { icon, text } }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectMethod(method);
        chip.GestureRecognizers.Add(tap);
        HandCursor.Apply(chip);
        SemanticProperties.SetDescription(chip, method);
        _chips.Add((method, chip, text, icon));
        return chip;
    }

    private static Label Small(string text, bool end = false) => new()
    {
        Text = text,
        FontSize = 14.5,
        TextColor = ThemeResources.Color("BodyText"),
        HorizontalOptions = end ? LayoutOptions.End : LayoutOptions.Start
    };
}

/// <summary>Add / edit a service (or the consultation fee) on an Unpaid bill: description, quantity and unit price.</summary>
public sealed class ServiceItemView : ContentView
{
    public const double PanelWidth = 420;
    public const string DescriptionField = "description";
    public const string QuantityField = "quantity";
    public const string PriceField = "price";

    private readonly Border _descriptionBox, _quantityBox, _priceBox;
    private readonly Entry _description, _quantity, _price;
    private readonly Label _descriptionError, _quantityError, _priceError, _message;
    private readonly HoverButton _save;

    public ServiceItemView(BillItem item)
    {
        Item = item;
        (_descriptionBox, _description) = PharmacyKit.TextInput("e.g. Wound dressing", maxLength: 100);
        (_quantityBox, _quantity) = PharmacyKit.TextInput("1", Keyboard.Numeric, 5);
        (_priceBox, _price) = PharmacyKit.TextInput("0.00", Keyboard.Numeric, 12, "₱");
        _descriptionError = PharmacyKit.ErrorLabel();
        _quantityError = PharmacyKit.ErrorLabel();
        _priceError = PharmacyKit.ErrorLabel();
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };
        _description.Text = item?.Description;
        _quantity.Text = (item?.Quantity ?? 1).ToString();
        _price.Text = item?.UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _description.TextChanged += (s, e) => PharmacyKit.SetError(_descriptionError, null, _descriptionBox);
        _quantity.TextChanged += (s, e) => PharmacyKit.SetError(_quantityError, null, _quantityBox);
        _price.TextChanged += (s, e) => PharmacyKit.SetError(_priceError, null, _priceBox);

        var pair = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        pair.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Quantity *"), _quantityBox, _quantityError } }, 0, 0);
        pair.Add(new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Unit price *"), _priceBox, _priceError } }, 1, 0);
        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children = { new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Description *"), _descriptionBox, _descriptionError } }, pair, _message }
        };
        _save = new HoverButton { Text = "Save", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        string title = item == null ? "Add service" : item.ItemType == BillItemTypes.Consultation ? "Edit consultation fee" : "Edit service";
        Content = PharmacyKit.PanelFrame(title, null, form, () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);
    }

    /// <summary>The item being edited, or null when adding.</summary>
    public BillItem Item { get; }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public (string Description, string Quantity, string Price) Read() => (_description.Text, _quantity.Text, _price.Text);

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_descriptionError, Get(DescriptionField), _descriptionBox);
        PharmacyKit.SetError(_quantityError, Get(QuantityField), _quantityBox);
        PharmacyKit.SetError(_priceError, Get(PriceField), _priceBox);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Save";
    }
}
