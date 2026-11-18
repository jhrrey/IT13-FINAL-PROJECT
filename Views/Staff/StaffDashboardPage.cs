using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>The Cashier/Staff dashboard (section 5): check in, dispense and collect.</summary>
public sealed class StaffDashboardPage : DashboardPageBase
{
    public void Show(StaffDashboardData d, string firstName, string clinic, SyncState sync)
    {
        var a = Actions;
        var today = DateTime.Today;

        var cards = new View[]
        {
            DashboardKit.StatCard("calendar-days", "Cyan", "Navy", d.AppointmentsToday.ToString(), "Appointments today", onTap: () => a.OpenModule(MenuCatalog.Appointments, null, null)),
            DashboardKit.StatCard("log-in", "PaleBlue", "Navy", d.ToCheckIn.ToString(), "To check in",
                d.GuestsToRegister > 0 ? d.GuestsToRegister + (d.GuestsToRegister == 1 ? " guest" : " guests") + " to register" : null,
                onTap: () => a.OpenModule(MenuCatalog.Appointments, null, null)),
            DashboardKit.StatCard("pill", "WarningBox", "ConsultText", d.ToDispense.ToString(), "Prescriptions to dispense", onTap: () => a.OpenModule(MenuCatalog.Prescriptions, null, null)),
            DashboardKit.StatCard("receipt", "OrangeSoft", "Orange", d.ReadyToBill.ToString(), "Ready to bill", onTap: () => a.OpenModule(MenuCatalog.Billing, null, null)),
            DashboardKit.StatCard("wallet", "Mint", "CompletedText", CurrencyHelper.Peso(d.CollectedToday), "Collected today", onTap: () => a.OpenModule(MenuCatalog.Billing, null, "tab:payments"))
        };

        // Today's queue --------------------------------------------------------------------------------------
        var queue = DashboardKit.ListCard("calendar-days", "Today's queue",
            d.Queue.Select(q => QueueRow(q, a)).ToList(), "No appointments today.",
            () => a.OpenModule(MenuCatalog.Appointments, null, null), "calendar-days");

        // To dispense ------------------------------------------------------------------------------------------------
        var dispense = DashboardKit.ListCard("pill", "To dispense",
            d.Dispense.Select(p => (View)DashboardKit.Row(
                DashboardKit.TwoLines(p.PetName, p.MedicineName + " × " + p.Quantity),
                p.CanDispense
                    ? DashboardKit.SmallButton("Dispense", "package-check", true, () => a.Dispense(p.PrescriptionId))
                    : PharmacyKit.DisabledButton(p.IsExpired ? "Expired" : "No stock", "package-x"))).ToList(),
            "Nothing to dispense. Nice!", () => a.OpenModule(MenuCatalog.Prescriptions, null, null), "package-check");

        // Ready to bill / Unpaid ----------------------------------------------------------------------------------------
        var billing = DashboardKit.ListCard("receipt", "Ready to bill / Unpaid",
            d.BillingTasks.Select(t => (View)DashboardKit.Row(
                DashboardKit.TwoLines(t.OwnerName, t.PetName + (t.NeedsBill ? " · not billed yet" : " · Balance " + CurrencyHelper.Peso(t.Balance))),
                t.NeedsBill
                    ? DashboardKit.SmallButton("Create bill", "receipt", true, () => a.CreateBill(t.AppointmentId.Value))
                    : DashboardKit.SmallButton("Receive payment", "wallet", false, () => a.ReceivePayment(t.BillId.Value)))).ToList(),
            "Everything is billed and paid.", () => a.OpenModule(MenuCatalog.Billing, null, null), "receipt");

        // Vaccine follow-ups ------------------------------------------------------------------------------------------------
        var followUps = DashboardKit.ListCard("shield-check", "Vaccine follow-ups",
            d.FollowUps.Select(v => (View)FollowUpRow(v, today, a)).ToList(),
            "No vaccines due soon.", () => a.OpenModule(MenuCatalog.Vaccinations, null, "due"), "shield-check");

        var stock = DashboardKit.ListCard("package-x", "Stock alerts",
            d.StockAlerts.Select(m => DashboardKit.StockRow(m, today, () => a.OpenModule(MenuCatalog.Medicines, null, "status:" + StockStatusHelper.StatusOf(m, today)))).ToList(),
            "No low or out-of-stock medicines.", () => a.OpenModule(MenuCatalog.Medicines, null, null), "package-check");

        var right = new VerticalStackLayout { Spacing = 16, Children = { dispense, billing } };

        Render(
            DashboardKit.Header(firstName, clinic, sync, a.SyncNow),
            DashboardKit.QuickActions(new[]
            {
                new QuickAction("Book appointment", "plus", a.BookAppointment, Primary: true),
                new QuickAction("Walk-in", "footprints", a.WalkIn),
                new QuickAction("Add owner", "user-plus", () => a.OpenModule(MenuCatalog.Owners, null, "add")),
                new QuickAction("Receive payment", "wallet", () => a.OpenModule(MenuCatalog.Billing, null, "tab:bills"))
            }),
            DashboardKit.StatGrid(cards, 5),
            DashboardKit.Columns(900, new[] { 3.0, 2.0 }, queue, right),
            DashboardKit.Columns(820, new[] { 1.0, 1.0 }, followUps, stock));
    }

    /// <summary>Time, pet (+ Guest), owner · vet, status, and the next action for the front desk.</summary>
    private static View QueueRow(QueueItem q, DashboardActions a)
    {
        var x = q.Appointment;
        View action = null;
        if (x.Status == AppointmentStatuses.Scheduled)
            action = x.IsGuest
                ? Outline("Register & check in", "user-plus", "Orange", () => a.OpenAppointment(x))
                : DashboardKit.SmallButton("Check in", "log-in", false, () => a.OpenAppointment(x));
        else if (x.Status == AppointmentStatuses.Completed && q.BillId == null)
            action = DashboardKit.SmallButton("Create bill", "receipt", true, () => a.CreateBill(x.AppointmentId));
        else if (q.BillId is int billId && q.BillStatus is BillStatuses.Unpaid or BillStatuses.Partial)
            action = DashboardKit.SmallButton("Receive payment", "wallet", false, () => a.ReceivePayment(billId));
        else if (q.BillStatus == BillStatuses.Paid)
            action = Badges.BillStatus(BillStatuses.Paid);

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(0, 8),
            MinimumHeightRequest = 56,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(68)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(new Label { Text = x.TimeText, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        var pet = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        pet.Add(AppointmentCards.PetAvatar(x, 32), 0, 0);
        pet.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                AppointmentCards.WithGuestPill(x, new Label { Text = x.PetName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 120, HorizontalOptions = LayoutOptions.Start }),
                new Label { Text = x.OwnerName + " · " + x.VetName, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation }
            }
        }, 1, 0);
        grid.Add(pet, 1, 0);
        grid.Add(Badges.AppointmentStatus(x.Status), 2, 0);
        if (action != null) grid.Add(action, 3, 0);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (action != null && e.GetPosition(grid) is Point p && p.X > grid.Width - action.Width - 6) return;
            a.OpenAppointment(x);
        };
        grid.GestureRecognizers.Add(tap);
        HandCursor.Apply(grid);
        return grid;
    }

    private static View FollowUpRow(RecordVaccination v, DateTime today, DashboardActions a)
    {
        var status = VaccineScheduleHelper.StatusOf(v.NextDueDate, today);
        var copy = PageKit.IconButton("copy", "Copy contact number", async () => await Clipboard.Default.SetTextAsync(v.OwnerContactNo));
        copy.WidthRequest = copy.HeightRequest = 26;
        var owner = new HorizontalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = v.OwnerName + " · " + v.OwnerContactDisplay, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation },
                copy
            }
        };
        var left = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = v.PetName + " · " + v.VaccineName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                owner
            }
        };
        var right = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Badges.VaccineStatus(status),
                        new Label { Text = VaccineScheduleHelper.DaysText(v.NextDueDate ?? today, today), FontSize = 12, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.Start }
                    }
                },
                DashboardKit.SmallButton("Book", "calendar-plus", true, () => a.BookFollowUp(v))
            }
        };
        return DashboardKit.Row(left, right);
    }

    /// <summary>An outline small button in a colour (e.g. orange "Register &amp; check in").</summary>
    private static View Outline(string text, string icon, string colorKey, Action onTap)
    {
        var color = ThemeResources.Color(colorKey);
        var button = new Border
        {
            HeightRequest = 34,
            Padding = new Thickness(12, 0),
            StrokeThickness = 1,
            Stroke = color,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = ThemeResources.Color("Surface"),
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = icon, StrokeColor = color, WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 13.5, TextColor = color, VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onTap();
        button.GestureRecognizers.Add(tap);
        HandCursor.Apply(button);
        SemanticProperties.SetDescription(button, text);
        return button;
    }
}
