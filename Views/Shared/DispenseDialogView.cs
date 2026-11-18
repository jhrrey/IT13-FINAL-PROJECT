using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// "Dispense medicine" (centered, 480px). One prescription: pet + owner, medicine, quantity, dosage and the
/// instructions in a cyan box, "Stock: 24 → 10", an expiry warning, the label checkbox and the bill note.
/// Several ("Dispense all"): one compact line per medicine with the same stock line.
/// </summary>
public sealed class DispenseDialogView : ContentView
{
    private readonly BoxView _dim;
    private readonly Border _card;
    private readonly VerticalStackLayout _body;
    private readonly Border _checkBox;
    private readonly LucideIcon _checkIcon;
    private readonly HorizontalStackLayout _labelRow;
    private bool _printLabel;
    private readonly Label _error;
    private readonly HoverButton _dispense;
    private string _actionText;

    public DispenseDialogView()
    {
        IsVisible = false;
        _dim = new BoxView { Color = ThemeResources.Color("Dim") };
        var dimTap = new TapGestureRecognizer();
        dimTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _dim.GestureRecognizers.Add(dimTap);

        _body = new VerticalStackLayout { Spacing = 14 };
        _checkIcon = new LucideIcon { Icon = "check", StrokeColor = Colors.White, StrokeWidth = 3, WidthRequest = 14, HeightRequest = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _checkBox = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            VerticalOptions = LayoutOptions.Center,
            Content = _checkIcon
        };
        _labelRow = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Start,
            Children =
            {
                _checkBox,
                new Label { Text = "Print medicine label", FontFamily = "DMSansMedium", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
            }
        };
        var labelTap = new TapGestureRecognizer();
        labelTap.Tapped += (s, e) => SetPrintLabel(!_printLabel);
        _labelRow.GestureRecognizers.Add(labelTap);
        HandCursor.Apply(_labelRow);
        SemanticProperties.SetDescription(_labelRow, "Print medicine label");
        SetPrintLabel(true);
        _error = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        var cancel = new HoverButton { Text = "Cancel", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
        cancel.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _dispense = new HoverButton { Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _dispense.Clicked += (s, e) => { if (_dispense.IsEnabled) Confirmed?.Invoke(this, EventArgs.Empty); };
        var buttons = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        buttons.Add(cancel, 0, 0);
        buttons.Add(_dispense, 1, 0);

        var scroll = new ScrollView { Content = _body, MaximumHeightRequest = 460 };
        _card = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            WidthRequest = 480,
            Padding = new Thickness(28, 24),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new IconBadge { Icon = "package-check", Size = 44, IconSize = 22, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText") },
                    new Label { Text = "Dispense medicine", FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy") },
                    scroll,
                    _labelRow,
                    _error,
                    buttons
                }
            }
        };
        Content = new Grid { Children = { _dim, _card } };
    }

    /// <summary>A drawn 20px checkbox (WinUI's native one reserves a wide frame that pushes the text away).</summary>
    private void SetPrintLabel(bool value)
    {
        _printLabel = value;
        _checkBox.BackgroundColor = value ? ThemeResources.Color("Orange") : ThemeResources.Color("Surface");
        _checkBox.Stroke = ThemeResources.Color(value ? "Orange" : "BorderColor");
        _checkIcon.IsVisible = value;
    }

    public event EventHandler Confirmed;
    public event EventHandler CancelRequested;

    public bool PrintLabel => _printLabel;

    public Task ShowAsync(IReadOnlyList<PharmacyPrescription> items)
    {
        _body.Children.Clear();
        var first = items[0];
        _body.Children.Add(new Label
        {
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = first.PetName, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") },
                    new Span { Text = "  ·  " + first.OwnerName + "  ·  " + first.VetDisplay, FontSize = 15, TextColor = ThemeResources.Color("BodyText") }
                }
            }
        });

        if (items.Count == 1) AddSingle(first);
        else foreach (var p in items) _body.Children.Add(CompactLine(p));

        decimal total = items.Sum(p => p.Amount);
        _body.Children.Add(new Label
        {
            Text = PharmacyKit.Peso(total) + " will be charged on the visit's bill.",
            FontSize = 14,
            TextColor = ThemeResources.Color("BodyText")
        });

        _actionText = items.Count == 1 ? "Dispense" : "Dispense " + items.Count;
        _dispense.Text = _actionText;
        _dispense.IsEnabled = true;
        SetPrintLabel(true);
        ShowError(null);
        return OpenAsync();
    }

    public void ShowError(string message)
    {
        _error.Text = message ?? string.Empty;
        _error.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void SetBusy(bool busy)
    {
        _dispense.IsEnabled = !busy;
        _dispense.Text = busy ? "Dispensing…" : _actionText;
    }

    public async Task HideAsync()
    {
        if (!IsVisible) return;
        await Task.WhenAll(this.FadeTo(0, 140, Easing.CubicIn), _card.ScaleTo(0.97, 140, Easing.CubicIn));
        IsVisible = false;
    }

    private async Task OpenAsync()
    {
        Opacity = 0;
        _card.Scale = 0.97;
        IsVisible = true;
        await Task.WhenAll(this.FadeTo(1, 160, Easing.CubicOut), _card.ScaleTo(1, 160, Easing.CubicOut));
    }

    private void AddSingle(PharmacyPrescription p)
    {
        var info = new Grid
        {
            ColumnSpacing = 16,
            RowSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(90)), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        AddInfo(info, 0, "Medicine", new Label { Text = p.MedicineName, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
        AddInfo(info, 1, "Quantity", new Label { Text = p.QuantityText, FontSize = 15, TextColor = ThemeResources.Color("Navy") });
        AddInfo(info, 2, "Dosage", new Label { Text = p.Dosage ?? "—", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
        _body.Children.Add(info);

        if (!string.IsNullOrWhiteSpace(p.Instructions))
        {
            var (box, text) = PharmacyKit.InfoBox("CyanSoft", "Navy");
            text.Text = p.Instructions;
            text.FontSize = 15.5;
            _body.Children.Add(box);
        }
        _body.Children.Add(StockLine(p));
        if (ExpiryWarning(p) is View warning) _body.Children.Add(warning);
    }

    private static void AddInfo(Grid grid, int row, string label, View value)
    {
        grid.Add(new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, 0, row);
        grid.Add(value, 1, row);
    }

    private static View CompactLine(PharmacyPrescription p)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(14, 10),
            Children =
            {
                new Label { Text = p.MedicineName + " × " + p.QuantityText, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = string.Join(" · ", new[] { p.Dosage, p.Instructions }.Where(t => !string.IsNullOrWhiteSpace(t))), FontSize = 13, TextColor = ThemeResources.Color("BodyText"), IsVisible = !string.IsNullOrWhiteSpace(p.Dosage) || !string.IsNullOrWhiteSpace(p.Instructions) },
                StockLine(p)
            }
        };
        if (ExpiryWarning(p) is View warning) stack.Children.Add(warning);
        return new Border
        {
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = stack
        };
    }

    /// <summary>"Stock: 24 → 10 Capsule", amber when the new stock will be low.</summary>
    private static Label StockLine(PharmacyPrescription p)
    {
        int after = p.StockQty - p.Quantity;
        bool low = after <= p.ReorderLevel;
        return new Label
        {
            Text = "Stock: " + p.StockQty.ToString("N0") + " → " + after.ToString("N0") + " " + p.Unit + (low ? "  (low stock)" : string.Empty),
            FontFamily = "DMSansBold",
            FontSize = 14,
            TextColor = ThemeResources.Color(low ? "ConsultText" : "CompletedText")
        };
    }

    private static View ExpiryWarning(PharmacyPrescription p)
    {
        if (StockStatusHelper.StatusOf(p.AsMedicine(), DateTime.Today) != StockStatus.ExpiringSoon || p.ExpiryDate == null) return null;
        var (box, text) = PharmacyKit.InfoBox("WarningBox", "ConsultText");
        text.Text = "Expires on " + p.ExpiryDate.Value.ToString("MMM dd, yyyy") + ".";
        return box;
    }
}
