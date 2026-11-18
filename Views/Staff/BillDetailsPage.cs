using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>
/// Bill details (section 3.D): number, status, people, items with the total, payments and the balance.
/// Service and consultation lines can be edited or removed while the bill is Unpaid (rule 8); paid bills are locked.
/// </summary>
public sealed class BillDetailsPage : ContentView
{
    private static readonly TableColumn[] ItemColumns =
    {
        new("Type", 125), new("Description", 300), new("Qty", 70), new("Unit price", 115), new("Subtotal", 115), new("", 90)
    };

    public BillDetailsPage(Bill bill, bool canManage)
    {
        Bill = bill;
        bool editable = canManage && bill.IsUnpaid;

        var back = new TextLink { Text = "← Bills & payments", FontSize = 14 };
        back.Clicked += (s, e) => BackRequested?.Invoke(this, EventArgs.Empty);

        // Header: bill no., status, sync — and the actions on the right.
        var titleRow = new HorizontalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = bill.NumberText, FontFamily = "NunitoExtraBold", FontSize = 28, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                Badges.BillStatus(bill.Status),
                Badges.Sync(bill.SyncStatus)
            }
        };
        var actions = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        if (canManage && bill.Status != BillStatuses.Void)
        {
            var print = new IconTextButton { Icon = "printer", Text = "Print receipt", Outline = true };
            print.Clicked += (s, e) => PrintRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(print);
        }
        if (editable)
        {
            var add = new IconTextButton { Icon = "plus", Text = "Add service", Outline = true };
            add.Clicked += (s, e) => AddServiceRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(add);
        }
        if (canManage && bill.IsOpen)
        {
            var pay = new IconTextButton { Icon = "wallet", Text = "Receive payment" };
            pay.Clicked += (s, e) => PaymentRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(pay);
        }
        if (canManage && bill.Status != BillStatuses.Void)
        {
            Border menu = null;
            menu = PageKit.IconButton("ellipsis-vertical", "More for " + bill.NumberText, () => MenuRequested?.Invoke(this, menu));
            actions.Children.Add(menu);
        }
        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(titleRow, 0, 0);
        header.Add(actions, 1, 0);

        var body = new VerticalStackLayout { Spacing = 16 };
        if (bill.Status == BillStatuses.Paid)
            body.Children.Add(Note("lock", "Locked — paid bills can't be changed.", "LightGray", "BodyText"));
        if (bill.Status == BillStatuses.Void)
            body.Children.Add(Note("circle-alert", "Voided" + (string.IsNullOrWhiteSpace(bill.Remarks) ? "." : ": " + bill.Remarks) + " It's kept in the records but doesn't count as income.", "LightGray", "BodyText"));
        body.Children.Add(InfoCard(bill));
        body.Children.Add(ItemsCard(bill, editable));

        var lower = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(300)) } };
        lower.Add(PaymentsCard(bill), 0, 0);
        lower.Add(BalanceCard(bill), 1, 0);
        body.Children.Add(lower);

        var scroll = new ScrollView { Content = new VerticalStackLayout { Padding = new Thickness(40, 28, 40, 40), Spacing = 14, Children = { back, header, body } } };
        ScrollFocus.DontScrollOnFocus(scroll);
        Content = scroll;
    }

    public Bill Bill { get; }

    public event EventHandler BackRequested;
    public event EventHandler PrintRequested;
    public event EventHandler AddServiceRequested;
    public event EventHandler PaymentRequested;
    public event EventHandler<View> MenuRequested;
    public event EventHandler<BillItem> EditItemRequested;
    public event EventHandler<BillItem> RemoveItemRequested;

    private static View InfoCard(Bill b)
    {
        var grid = new Grid
        {
            ColumnSpacing = 24,
            RowSpacing = 14,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        grid.Add(Info("OWNER", b.OwnerName, b.ContactDisplay), 0, 0);
        grid.Add(Info("PET", b.PetName, b.SpeciesAndBreed), 1, 0);
        grid.Add(Info("VET", b.VetName, b.Reason), 2, 0);
        grid.Add(Info("VISIT DATE", b.VisitDate.ToString("MMM d, yyyy"), TimeSlotHelper.Format(b.VisitTime)), 0, 1);
        grid.Add(Info("BILL DATE", b.BillDate.ToString("MMM d, yyyy"), b.BillDate.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)), 1, 1);
        grid.Add(Info("CASHIER", b.CashierName, null), 2, 1);
        return new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20, 16), Content = grid };
    }

    private View ItemsCard(Bill b, bool editable)
    {
        var stack = new VerticalStackLayout { Children = { PageKit.HeaderRow(ItemColumns) } };
        foreach (var item in b.Items)
        {
            var grid = PageKit.RowGrid(ItemColumns, 54);
            grid.Add(CreateBillPage.TypePill(item.ItemType), 0, 0);
            grid.Add(PageKit.Cell(item.Description), 1, 0);
            grid.Add(PageKit.Cell(item.Quantity.ToString()), 2, 0);
            grid.Add(PageKit.Cell(CurrencyHelper.Peso(item.UnitPrice)), 3, 0);
            grid.Add(new Label { Text = CurrencyHelper.Peso(item.Subtotal), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 4, 0);
            if (editable && item.IsEditable)
            {
                grid.Add(new HorizontalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        PageKit.IconButton("pencil", "Edit " + item.Description, () => EditItemRequested?.Invoke(this, item)),
                        PageKit.IconButton("trash-2", "Remove " + item.Description, () => RemoveItemRequested?.Invoke(this, item))
                    }
                }, 5, 0);
            }
            else if (!item.IsEditable)
            {
                var locked = new ContentView
                {
                    WidthRequest = 34,
                    HeightRequest = 34,
                    HorizontalOptions = LayoutOptions.Start,
                    Content = new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                };
                ToolTipProperties.SetText(locked, "From " + (item.ItemType == BillItemTypes.Vaccine ? "vaccination" : "pharmacy") + " — can't be edited here");
                grid.Add(locked, 5, 0);
            }
            stack.Children.Add(PageKit.WithBottomLine(grid));
        }
        if (b.Items.Count == 0)
            stack.Children.Add(new Label { Text = "No items.", FontSize = 14, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(20, 16) });

        var totalRow = PageKit.RowGrid(ItemColumns, 52);
        totalRow.BackgroundColor = ThemeResources.Color("InputBackground");
        var label = new Label { Text = "Total", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
        totalRow.Add(label, 0, 0);
        totalRow.Add(new Label { Text = CurrencyHelper.Peso(b.TotalAmount), FontFamily = "NunitoExtraBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 4, 0);
        stack.Children.Add(totalRow);

        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = "Items", FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(20, 16, 20, 12) },
                    new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stack }
                }
            }
        };
    }

    private static View PaymentsCard(Bill b)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        stack.Children.Add(new Label { Text = "Payments", FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(0, 0, 0, 8) });
        if (b.Payments.Count == 0)
            stack.Children.Add(new Label { Text = "No payments yet", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        foreach (var p in b.Payments)
        {
            var row = new Grid
            {
                Padding = new Thickness(0, 10),
                ColumnSpacing = 12,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
            };
            row.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    Badges.PaymentMethod(p.PaymentMethod),
                    new Label { Text = p.PaymentDate.ToString("MMM d, yyyy · h:mm tt", System.Globalization.CultureInfo.InvariantCulture), FontSize = 13.5, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
                }
            }, 0, 0);
            row.Add(new Label { Text = CurrencyHelper.Peso(p.AmountPaid), FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Success"), VerticalOptions = LayoutOptions.Center }, 1, 0);
            var who = new Label
            {
                Text = "Received by " + p.ReceivedByName + (string.IsNullOrEmpty(p.ReferenceNo) ? string.Empty : " · Ref " + p.ReferenceNo),
                FontSize = 13,
                TextColor = ThemeResources.Color("Muted")
            };
            row.Add(who, 0, 1);
            Grid.SetColumnSpan(who, 2);
            stack.Children.Add(PageKit.WithBottomLine(row));
        }
        return new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20, 16), Content = stack };
    }

    private static View BalanceCard(Bill b)
    {
        bool fullyPaid = b.Status == BillStatuses.Paid;
        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        grid.Add(Muted("Total"), 0, 0);
        grid.Add(Amount(CurrencyHelper.Peso(b.TotalAmount), "Navy"), 1, 0);
        grid.Add(Muted("Paid"), 0, 1);
        grid.Add(Amount(CurrencyHelper.Peso(b.Paid), "Success"), 1, 1);
        var line = new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") };
        grid.Add(line, 0, 2);
        Grid.SetColumnSpan(line, 2);
        View balance = fullyPaid
            ? Badges.Pill("Fully paid", "CompletedText", "Mint")
            : new Label { Text = CurrencyHelper.Peso(b.Balance), FontFamily = "NunitoExtraBold", FontSize = 26, TextColor = ThemeResources.Color(b.Balance > 0 ? "Orange" : "Navy") };
        grid.Add(new Label { Text = "Balance", FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 3);
        grid.Add(balance, 1, 3);
        return new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(20, 16), VerticalOptions = LayoutOptions.Start, Content = grid };
    }

    private static Label Muted(string text) => new() { Text = text, FontSize = 14.5, TextColor = ThemeResources.Color("BodyText") };

    private static Label Amount(string text, string color) => new() { Text = text, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color(color), HorizontalOptions = LayoutOptions.End };

    private static View Info(string label, string first, string second)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = label, FontFamily = "DMSansBold", FontSize = 12, CharacterSpacing = 0.67, TextColor = ThemeResources.Color("SectionLabel") },
                new Label { Text = string.IsNullOrEmpty(first) ? "—" : first, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation }
            }
        };
        if (!string.IsNullOrEmpty(second))
            stack.Children.Add(new Label { Text = second, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.TailTruncation });
        return stack;
    }

    private static View Note(string icon, string text, string background, string color) => new Border
    {
        Padding = new Thickness(14, 10),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        BackgroundColor = ThemeResources.Color(background),
        Content = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(color), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontSize = 14, TextColor = ThemeResources.Color(color), VerticalOptions = LayoutOptions.Center }
            }
        }
    };
}
