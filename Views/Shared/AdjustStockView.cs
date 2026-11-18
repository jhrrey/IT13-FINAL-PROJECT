using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Adjust stock (side panel, 420px): Count correction (add or remove), Expired or Damaged (remove),
/// a quantity and a required reason (rule 9). The amber box shows the new stock live, red below 0.
/// </summary>
public sealed class AdjustStockView : ContentView
{
    public const double PanelWidth = 420;

    private readonly Medicine _medicine;
    private readonly List<(string Type, Border Button, Label Text)> _types = new();
    private readonly List<(bool Add, Border Button, Label Text)> _signs = new();
    private readonly View _signRow;
    private readonly Label _quantityLabel;
    private readonly Border _quantityBox, _reasonBox, _previewBox;
    private readonly Entry _quantity;
    private readonly Editor _reason;
    private readonly Label _typeError, _quantityError, _reasonError, _preview, _message;
    private readonly HoverButton _save;
    private string _type;
    private bool _add;

    public AdjustStockView(Medicine medicine, string startType = null)
    {
        _medicine = medicine;

        var typeRow = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < StockChangeData.AdjustTypes.Count; i++)
        {
            typeRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(i == 0 ? 1.45 : 1, GridUnitType.Star)));
            string type = StockChangeData.AdjustTypes[i];
            var (button, text) = Toggle(type, () => SelectType(type));
            _types.Add((type, button, text));
            typeRow.Add(button, i, 0);
        }
        _typeError = PharmacyKit.ErrorLabel();

        var signGrid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        foreach (var (add, label, icon, column) in new[] { (false, "Remove", "minus", 0), (true, "Add", "plus", 1) })
        {
            var (button, text) = Toggle(label, () => SelectSign(add), icon);
            _signs.Add((add, button, text));
            signGrid.Add(button, column, 0);
        }
        _signRow = new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Add or remove"), signGrid } };

        _quantityLabel = PharmacyKit.FieldLabel("Quantity *");
        (_quantityBox, _quantity) = PharmacyKit.TextInput("e.g. 4", Keyboard.Numeric, 7);
        _quantityError = PharmacyKit.ErrorLabel();
        _reason = new Editor { Placeholder = "e.g. Monthly count: 4 capsules missing", MaxLength = 255, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 90, FontFamily = "DMSans", FontSize = 15, TextColor = ThemeResources.Color("Navy"), PlaceholderColor = ThemeResources.Color("Placeholder"), BackgroundColor = Colors.Transparent };
        _reasonBox = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = -1, MinimumHeightRequest = 96, Padding = new Thickness(8, 4), Content = _reason };
        _reasonError = PharmacyKit.ErrorLabel();
        (_previewBox, _preview) = PharmacyKit.InfoBox("WarningBox", "ConsultText");
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        _quantity.TextChanged += (s, e) => { PharmacyKit.SetError(_quantityError, null, _quantityBox); UpdatePreview(); };
        _reason.TextChanged += (s, e) => PharmacyKit.SetError(_reasonError, null, _reasonBox);

        var form = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 18,
            Children =
            {
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Type *"), typeRow, _typeError } },
                _signRow,
                new VerticalStackLayout { Children = { _quantityLabel, _quantityBox, _quantityError } },
                new VerticalStackLayout { Children = { PharmacyKit.FieldLabel("Reason *"), _reasonBox, _reasonError } },
                _previewBox,
                _message
            }
        };

        _save = new HoverButton { Text = "Save adjustment", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        Content = PharmacyKit.PanelFrame("Adjust stock", medicine.MedicineName + " · " + medicine.Unit, form,
            () => CancelRequested?.Invoke(this, EventArgs.Empty), _save);

        SelectSign(false);
        SelectType(startType ?? (medicine.IsExpired ? "Expired" : StockChangeData.CountCorrection));
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    public StockChangeData Read() => new() { Type = _type, Add = _add, Quantity = _quantity.Text, Remarks = _reason.Text };

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Get(string key) => errors.TryGetValue(key, out var m) ? m : null;
        PharmacyKit.SetError(_typeError, Get(StockChangeData.TypeField), null);
        PharmacyKit.SetError(_quantityError, Get(StockChangeData.QuantityField), _quantityBox);
        PharmacyKit.SetError(_reasonError, Get(StockChangeData.RemarksField), _reasonBox);
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy;
        _save.Text = busy ? "Saving…" : "Save adjustment";
    }

    private void SelectType(string type)
    {
        _type = type;
        foreach (var (t, button, text) in _types) Paint(button, text, t == type);
        bool correction = type == StockChangeData.CountCorrection;
        _signRow.IsVisible = correction;
        _quantityLabel.Text = correction ? "Quantity *" : "Quantity to remove *";
        PharmacyKit.SetError(_typeError, null, null);
        UpdatePreview();
    }

    private void SelectSign(bool add)
    {
        _add = add;
        foreach (var (a, button, text) in _signs) Paint(button, text, a == add);
        UpdatePreview();
    }

    /// <summary>"Current stock: 24 → New stock: 20"; red with "Stock can't go below 0" when it would.</summary>
    private void UpdatePreview()
    {
        if (_preview == null) return;
        int quantity = PharmacyKit.ParseCount(_quantity.Text) ?? 0;
        bool adds = _type == StockChangeData.CountCorrection && _add;
        long result = _medicine.StockQty + (adds ? quantity : -(long)quantity);
        bool below = result < 0;
        _previewBox.BackgroundColor = ThemeResources.Color(below ? "DangerSoft" : "WarningBox");
        _preview.TextColor = ThemeResources.Color(below ? "DangerHover" : "ConsultText");
        _preview.Text = "Current stock: " + _medicine.StockQty.ToString("N0") + "  →  New stock: " + result.ToString("N0") +
                        (below ? "\nStock can't go below 0" : string.Empty);
    }

    /// <summary>A toggle button; selected = cyan background, bold navy.</summary>
    private static (Border Button, Label Text) Toggle(string text, Action select, string icon = null)
    {
        var label = new Label { Text = text, FontSize = 14, FontFamily = "DMSans", LineBreakMode = LineBreakMode.NoWrap, VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center };
        label.HorizontalOptions = icon == null ? LayoutOptions.Fill : LayoutOptions.Center;
        View content = label;
        if (icon != null)
            content = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center }, label } };
        var button = new Border
        {
            HeightRequest = 44,
            Padding = new Thickness(4, 0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = content
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => select();
        button.GestureRecognizers.Add(tap);
        HandCursor.Apply(button);
        SemanticProperties.SetDescription(button, text);
        return (button, label);
    }

    private static void Paint(Border button, Label text, bool selected)
    {
        button.BackgroundColor = ThemeResources.Color(selected ? "Cyan" : "Surface");
        button.Stroke = ThemeResources.Color(selected ? "Cyan" : "BorderColor");
        text.FontFamily = selected ? "DMSansBold" : "DMSans";
        text.TextColor = ThemeResources.Color(selected ? "Navy" : "BodyText");
    }
}
