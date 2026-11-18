using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Admin;

/// <summary>The Administrator dashboard (section 3): the health of the whole clinic at a glance.</summary>
public sealed class AdminDashboardPage : DashboardPageBase
{
    public void Show(AdminDashboardData d, string firstName, string clinic, SyncState sync)
    {
        var today = DateTime.Today;
        var a = Actions;

        // Stat cards ---------------------------------------------------------------------------------
        var change = GreetingHelper.Change(d.CollectedToday, d.CollectedYesterday);
        string alerts = string.Join(" · ", new[]
        {
            d.Stock.LowStock > 0 ? d.Stock.LowStock + " low" : null,
            d.Stock.OutOfStock > 0 ? d.Stock.OutOfStock + " out" : null,
            d.Stock.ExpiringSoon > 0 ? d.Stock.ExpiringSoon + " expiring" : null,
            d.ExpiredCount > 0 ? d.ExpiredCount + " expired" : null
        }.Where(t => t != null));
        int alertCount = d.Stock.LowStock + d.Stock.OutOfStock + d.Stock.ExpiringSoon + d.ExpiredCount;
        var cards = new View[]
        {
            DashboardKit.StatCard("calendar-days", "Cyan", "Navy", d.AppointmentsToday.ToString(), "Appointments today",
                d.CompletedToday + " completed · " + d.NoShowToday + " no-show", onTap: () => a.OpenModule(MenuCatalog.Appointments, null, null)),
            DashboardKit.StatCard("wallet", "Mint", "CompletedText", CurrencyHelper.Peso(d.CollectedToday), "Collected today",
                change?.Text, change?.Up == false ? "Orange" : "Success", () => a.OpenModule(MenuCatalog.Billing, null, "tab:payments")),
            DashboardKit.StatCard("trending-up", "PaleBlue", "Navy", CurrencyHelper.Peso(d.CollectedThisMonth), "Collected this month",
                onTap: () => a.OpenModule(MenuCatalog.Billing, null, "tab:payments")),
            DashboardKit.StatCard("circle-dollar-sign", "OrangeSoft", "Orange", CurrencyHelper.Peso(d.Outstanding), "Outstanding balance",
                d.OutstandingBills + (d.OutstandingBills == 1 ? " bill" : " bills"), onTap: () => a.OpenModule(MenuCatalog.Billing, null, "tab:bills")),
            DashboardKit.StatCard("package-x", "WarningBox", "ConsultText", alertCount.ToString(), "Stock alerts",
                string.IsNullOrEmpty(alerts) ? "All stock is fine" : alerts, onTap: () => a.OpenModule(MenuCatalog.Medicines, null, null))
        };

        // Charts ---------------------------------------------------------------------------------------
        var income = new LineChartView { HeightRequest = 220, Format = v => "₱" + Shorten(v) };
        income.Points = d.Income30.Select(p => new ChartPoint(p.Day.ToString("MMM d"), (double)p.Amount)).ToList();
        var incomeCard = ChartCard("trending-up", "Income – last 30 days", CurrencyHelper.Peso(d.Income30.Sum(p => p.Amount)), income);

        var week = new StackedBarChartView { HeightRequest = 220 };
        week.Points = d.Week.Select(w => new ChartPoint(w.Day.ToString("ddd"), w.Completed, w.Cancelled, w.NoShow)).ToList();
        var weekCard = ChartCard("calendar-days", "Appointments this week", null, week);

        // Third row ---------------------------------------------------------------------------------------
        var vets = new HorizontalBarChartView { HeightRequest = Math.Max(1, d.VetsToday.Count) * HorizontalBarChartView.RowHeight };
        vets.Points = d.VetsToday.Select(v => new ChartPoint(v.VetName, v.Visits)).ToList();
        var vetsCard = DashboardKit.ListCard("stethoscope", "Visits per vet today",
            d.VetsToday.Count == 0 ? Array.Empty<View>() : new View[] { vets }, "No active vets yet.", emptyIcon: "users");

        var stock = DashboardKit.ListCard("package-x", "Stock alerts",
            d.StockAlerts.Select(m => DashboardKit.StockRow(m, today, () => a.OpenModule(MenuCatalog.Medicines, null, "status:" + StockStatusHelper.StatusOf(m, today)))).ToList(),
            "All stock is fine.", () => a.OpenModule(MenuCatalog.Medicines, null, null));

        var payments = DashboardKit.ListCard("wallet", "Recent payments",
            d.RecentPayments.Select(p => (View)DashboardKit.Row(
                DashboardKit.TwoLines(p.OwnerName, p.PaymentMethod + " · " + When(p.PaymentDate)),
                new Label { Text = CurrencyHelper.Peso(p.AmountPaid), FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                () => a.OpenModule(MenuCatalog.Billing, p.BillId, null))).ToList(),
            "No payments yet.", () => a.OpenModule(MenuCatalog.Billing, null, "tab:payments"), "wallet");

        // Fourth row ------------------------------------------------------------------------------------------
        var overdue = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = d.OverdueVaccinations + (d.OverdueVaccinations == 1 ? " pet" : " pets"), FontFamily = "NunitoExtraBold", FontSize = 30, TextColor = ThemeResources.Color(d.OverdueVaccinations > 0 ? "Orange" : "Navy") },
                new Label { Text = d.OverdueVaccinations > 0 ? "Owners should be contacted." : "No overdue vaccines. Nice!", FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
            }
        };
        var openDue = new IconTextButton { Icon = "shield-check", Text = "Open due list", Outline = true, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 6, 0, 0) };
        openDue.Clicked += (s, e) => a.OpenModule(MenuCatalog.Vaccinations, null, "overdue");
        overdue.Children.Add(openDue);
        var overdueCard = DashboardKit.ListCard("shield-check", "Vaccines overdue", new View[] { overdue }, null);

        var devices = DashboardKit.ListCard("monitor", "Devices & sync", Array.Empty<View>(),
            "No devices registered yet — computers appear here once Devices & sync is set up.", emptyIcon: "monitor");

        Render(
            DashboardKit.Header(firstName, clinic, sync, a.SyncNow),
            DashboardKit.QuickActions(new[]
            {
                new QuickAction("Add staff", "user-plus", () => a.OpenModule(MenuCatalog.UsersAndRoles, null, "add")),
                new QuickAction("View reports", "chart-column", () => a.OpenModule(MenuCatalog.Reports, null, null)),
                new QuickAction("Medicines & stock", "warehouse", () => a.OpenModule(MenuCatalog.Medicines, null, null)),
                new QuickAction("Users & roles", "users", () => a.OpenModule(MenuCatalog.UsersAndRoles, null, null))
            }),
            DashboardKit.StatGrid(cards, 5),
            DashboardKit.Columns(820, new[] { 3.0, 2.0 }, incomeCard, weekCard),
            DashboardKit.Columns(860, new[] { 1.0, 1.0, 1.0 }, vetsCard, stock, payments),
            DashboardKit.Columns(820, new[] { 1.0, 1.0 }, overdueCard, devices));
    }

    private static View ChartCard(string icon, string title, string total, View chart)
    {
        var head = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center }, 0, 0);
        head.Add(new Label { Text = title, FontFamily = "DMSansBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        if (total != null) head.Add(new Label { Text = total, FontFamily = "NunitoExtraBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 2, 0);
        return new Border
        {
            Style = ThemeResources.Get<Style>("WorkspaceCard"),
            Padding = new Thickness(20),
            Content = new VerticalStackLayout { Spacing = 14, Children = { head, chart } }
        };
    }

    private static string When(DateTime time) =>
        time.Date == DateTime.Today ? time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : time.ToString("MMM d");

    /// <summary>"12.5k" for axis labels.</summary>
    private static string Shorten(double v) => v >= 1000 ? (v / 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k" : v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}
