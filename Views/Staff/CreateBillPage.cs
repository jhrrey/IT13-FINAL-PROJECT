using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>One editable bill line as typed (Consultation or Service); the controller validates it.</summary>
public sealed record BillLineInput(string ItemType, string Description, string Quantity, string UnitPrice);

/// <summary>
/// Create bill (section 3.B): the visit summary, the items (consultation fee and services editable; vaccines and
/// dispensed medicines read-only with a lock, rule 6), "+ Add service", the totals, and the save buttons.
/// </summary>
public sealed class CreateBillPage : ContentView
{
    private sealed class Line
    {
        public string ItemType;
        public Entry Description, Quantity, Price;
        public Label Subtotal, Error;
        public View Row;
    }

    private static readonly TableColumn[] Columns =
    {
        new("Type", 125), new("Description", 300), new("Qty", 80), new("Unit price", 120), new("Subtotal", 115), new("", 50)
    };

    private readonly BillDraft _draft;
    private readonly List<Line> _lines = new();
    private readonly VerticalStackLayout _rows;
    private readonly Label _subtotal, _total, _message;
    private readonly HoverButton _saveUnpaid, _savePay;

    public CreateBillPage(BillDraft draft)
    {
        _draft = draft;

        var back = new TextLink { Text = "← Bills & payments", FontSize = 14 };
        back.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        var title = new Label { Text = "Create bill", FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy") };

        _rows = new VerticalStackLayout();
        var table = new VerticalStackLayout { Children = { PageKit.HeaderRow(Columns), _rows } };

        // Fixed items first (from Vaccination and Pharmacy), then the consultation fee.
        AddEditableLine(BillItemTypes.Consultation, "Consultation fee", "1", draft.ConsultationFee.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var item in draft.FixedItems) _rows.Children.Add(FixedRow(item));

        var addService = new Border
        {
            Margin = new Thickness(20, 14, 20, 16),
            Padding = new Thickness(14, 10),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            HorizontalOptions = LayoutOptions.Start,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new LucideIcon { Icon = "plus", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Add service", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var addTap = new TapGestureRecognizer();
        addTap.Tapped += (s, e) => AddEditableLine(BillItemTypes.Service, string.Empty, "1", string.Empty, focus: true);
        addService.GestureRecognizers.Add(addTap);
        HandCursor.Apply(addService);
        SemanticProperties.SetDescription(addService, "Add service");

        var itemsCard = new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = "Bill items", FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(20, 16, 20, 12) },
                    new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = table },
                    addService
                }
            }
        };

        var body = new VerticalStackLayout { Spacing = 16, Children = { VisitCard(draft), itemsCard } };
        if (draft.NotDispensed > 0)
        {
            var (box, text) = PharmacyKit.InfoBox("WarningBox", "ConsultText");
            text.Text = draft.NotDispensed + (draft.NotDispensed == 1 ? " prescription isn't" : " prescriptions aren't") +
                        " dispensed yet. They'll be added to this bill when dispensed.";
            body.Children.Add(box);
        }

        // Totals and buttons.
        _subtotal = new Label { FontSize = 15, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.End };
        _total = new Label { FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.End };
        var totals = new Grid { ColumnSpacing = 12, RowSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        totals.Add(new Label { Text = "Subtotal", FontSize = 15, TextColor = ThemeResources.Color("BodyText") }, 0, 0);
        totals.Add(_subtotal, 1, 0);
        var divider = new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") };
        totals.Add(divider, 0, 1);
        Grid.SetColumnSpan(divider, 2);
        totals.Add(new Label { Text = "Total", FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 2);
        totals.Add(_total, 1, 2);
        totals.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        totals.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        totals.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var totalsCard = new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20, 16), WidthRequest = 340, HorizontalOptions = LayoutOptions.End, Content = totals };

        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger"), HorizontalOptions = LayoutOptions.End };
        var cancel = new HoverButton { Text = "Cancel", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
        cancel.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);
        _saveUnpaid = new HoverButton { Text = "Save as unpaid", Style = ThemeResources.Get<Style>("NavyOutlineButton"), Padding = new Thickness(22, 0) };
        _saveUnpaid.Clicked += (s, e) => SaveRequested?.Invoke(this, false);
        _savePay = new HoverButton { Text = "Save & receive payment", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(22, 0) };
        _savePay.Clicked += (s, e) => SaveRequested?.Invoke(this, true);
        var buttons = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.End, Children = { cancel, _saveUnpaid, _savePay } };
        body.Children.Add(totalsCard);
        body.Children.Add(_message);
        body.Children.Add(buttons);

        var scroll = new ScrollView
        {
            Content = new VerticalStackLayout { Padding = new Thickness(40, 28, 40, 40), Spacing = 14, Children = { back, title, body } }
        };
        ScrollFocus.DontScrollOnFocusWhileOpening(scroll);
        Content = scroll;
        UpdateTotals();
    }

    public event EventHandler BackRequested;

    /// <summary>Save (argument: true = "Save &amp; receive payment").</summary>
    public event EventHandler<bool> SaveRequested;

    public int AppointmentId => _draft.AppointmentId;

    public IReadOnlyList<BillLineInput> ReadLines() =>
        _lines.Select(l => new BillLineInput(l.ItemType, l.Description.Text, l.Quantity.Text, l.Price.Text)).ToList();

    /// <summary>Errors per line index ("line:2") and an overall message.</summary>
    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        for (int i = 0; i < _lines.Count; i++)
        {
            errors.TryGetValue("line:" + i, out var message);
            _lines[i].Error.Text = message ?? string.Empty;
            _lines[i].Error.IsVisible = !string.IsNullOrEmpty(message);
        }
        _message.Text = result.Message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _saveUnpaid.IsEnabled = _savePay.IsEnabled = !busy;
        _savePay.Text = busy ? "Saving…" : "Save & receive payment";
    }

    // Rows -------------------------------------------------------------------------------------------------------

    private void AddEditableLine(string type, string description, string quantity, string price, bool focus = false)
    {
        var line = new Line { ItemType = type };
        line.Description = SmallEntry(description, "e.g. Wound dressing", 100, Keyboard.Default);
        line.Quantity = SmallEntry(quantity, "1", 5, Keyboard.Numeric);
        line.Price = SmallEntry(price, "0.00", 10, Keyboard.Numeric);
        line.Subtotal = new Label { FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
        line.Error = new Label { IsVisible = false, FontSize = 12.5, TextColor = ThemeResources.Color("Danger"), Margin = new Thickness(20, -6, 20, 8) };
        foreach (var entry in new[] { line.Description, line.Quantity, line.Price })
            entry.TextChanged += (s, e) => { line.Error.IsVisible = false; UpdateTotals(); };

        var grid = PageKit.RowGrid(Columns, 60);
        grid.Add(TypePill(type), 0, 0);
        grid.Add(Boxed(line.Description), 1, 0);
        grid.Add(Boxed(line.Quantity, 66), 2, 0);
        grid.Add(Boxed(line.Price, 106), 3, 0);
        grid.Add(line.Subtotal, 4, 0);
        grid.Add(PageKit.IconButton("trash-2", "Remove " + (type == BillItemTypes.Consultation ? "consultation fee" : "service"), () =>
        {
            _lines.Remove(line);
            _rows.Children.Remove(line.Row);
            UpdateTotals();
        }), 5, 0);
        line.Row = PageKit.WithBottomLine(new VerticalStackLayout { Children = { grid, line.Error } });
        _lines.Add(line);
        // Services go after everything else; the consultation fee stays first.
        _rows.Children.Add(line.Row);
        UpdateTotals();
        if (focus) Dispatcher.Dispatch(() => line.Description.Focus());
    }

    private static View FixedRow(BillItem item)
    {
        var grid = PageKit.RowGrid(Columns, 56);
        grid.Add(TypePill(item.ItemType), 0, 0);
        grid.Add(PageKit.Cell(item.Description), 1, 0);
        grid.Add(PageKit.Cell(item.Quantity.ToString()), 2, 0);
        grid.Add(PageKit.Cell(CurrencyHelper.Peso(item.UnitPrice)), 3, 0);
        grid.Add(new Label { Text = CurrencyHelper.Peso(item.Subtotal), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 4, 0);
        var locked = new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var lockBox = new ContentView { WidthRequest = 34, HeightRequest = 34, Content = locked };
        ToolTipProperties.SetText(lockBox, "From " + (item.ItemType == BillItemTypes.Vaccine ? "vaccination" : "pharmacy") + " — can't be edited here");
        grid.Add(lockBox, 5, 0);
        return PageKit.WithBottomLine(grid);
    }

    /// <summary>Rule 4: subtotals and the total are calculated, not typed.</summary>
    private void UpdateTotals()
    {
        if (_subtotal == null) return;
        decimal sum = _draft.FixedItems.Sum(i => i.Subtotal);
        foreach (var line in _lines)
        {
            bool ok = int.TryParse(line.Quantity.Text?.Trim(), out int q) & CurrencyHelper.TryParse(line.Price.Text, out decimal p);
            decimal sub = ok && q > 0 && p >= 0 ? q * Math.Round(p, 2) : 0;
            line.Subtotal.Text = ok ? CurrencyHelper.Peso(sub) : "—";
            sum += sub;
        }
        _subtotal.Text = CurrencyHelper.Peso(sum);
        _total.Text = CurrencyHelper.Peso(sum);
    }

    // Building blocks ----------------------------------------------------------------------------------------------

    private static View VisitCard(BillDraft d)
    {
        var grid = new Grid
        {
            ColumnSpacing = 24,
            RowSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                PetsPage.SpeciesAvatar(d.Species, 44),
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = d.PetName, FontFamily = "NunitoExtraBold", FontSize = 20, TextColor = ThemeResources.Color("Navy") },
                        new Label { Text = string.IsNullOrWhiteSpace(d.Breed) ? d.Species : d.Species + " · " + d.Breed, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") }
                    }
                }
            }
        }, 0, 0);
        grid.Add(Info("OWNER", d.OwnerName, Validator.FormatPhMobile(d.OwnerContactNo)), 1, 0);
        grid.Add(Info("VISIT", d.VisitDate.ToString("MMM d, yyyy") + " · " + TimeSlotHelper.Format(d.VisitTime), d.VetName), 2, 0);
        var reason = Badges.Pill(d.Reason, "BodyText", "LightGray");
        reason.Margin = new Thickness(0, 10, 0, 0);
        var stack = new VerticalStackLayout { Children = { grid, reason } };
        return new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20, 16), Content = stack };
    }

    private static View Info(string label, string first, string second) => new VerticalStackLayout
    {
        Spacing = 2,
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = label, FontFamily = "DMSansBold", FontSize = 12, CharacterSpacing = 0.67, TextColor = ThemeResources.Color("SectionLabel") },
            new Label { Text = first, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
            new Label { Text = second, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") }
        }
    };

    internal static View TypePill(string type)
    {
        var (background, text) = BillItemTypes.ColorsFor(type);
        return Badges.Pill(type, text, background);
    }

    private static Entry SmallEntry(string text, string placeholder, int maxLength, Keyboard keyboard) => new()
    {
        Text = text,
        Placeholder = placeholder,
        MaxLength = maxLength,
        Keyboard = keyboard,
        FontFamily = "DMSans",
        FontSize = 14.5,
        TextColor = ThemeResources.Color("Navy"),
        PlaceholderColor = ThemeResources.Color("Placeholder"),
        BackgroundColor = Colors.Transparent,
        VerticalOptions = LayoutOptions.Center
    };

    private static View Boxed(Entry entry, double width = -1) => new Border
    {
        HeightRequest = 40,
        WidthRequest = width,
        Margin = new Thickness(0, 0, 12, 0),
        Padding = new Thickness(8, 0),
        StrokeThickness = 1,
        Stroke = ThemeResources.Color("BorderColor"),
        StrokeShape = new RoundRectangle { CornerRadius = 6 },
        BackgroundColor = ThemeResources.Color("InputBackground"),
        VerticalOptions = LayoutOptions.Center,
        Content = entry
    };
}
