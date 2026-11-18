namespace VetCare.Models.Entities;

public enum ReportCategory { Appointments, Medical, Inventory, Financial }

/// <summary>The 17 reports (Tasks/VetCare_Reports_Module.md, section 2).</summary>
public enum ReportType
{
    AppointmentSummary, NoShowCancellation, VetWorkload, VisitReasons, GuestBookings,
    ConsultationSummary, PatientReport, VaccinationReport, OverdueVaccines,
    StockLevels, StockMovement, MedicineUsage, ExpiredDamaged,
    DailyCollection, IncomeSummary, OutstandingBalances, VoidedBills
}

/// <summary>The extra filters a report offers next to the date range.</summary>
[Flags]
public enum ReportFilter
{
    None = 0,
    Vet = 1,
    AppointmentStatus = 2,
    Species = 4,
    PaymentMethod = 8,
    Cashier = 16,
    MedicineCategory = 32,
    TransactionType = 64
}

/// <param name="Snapshot">Shows today's state (stock levels, overdue vaccines); the date range doesn't change it.</param>
public sealed record ReportDefinition(ReportType Type, ReportCategory Category, string Name, string Description, string Icon,
    ReportFilter Filters, bool Snapshot = false);

/// <summary>The report list, by category, as on the Reports home.</summary>
public static class ReportCatalog
{
    public static readonly IReadOnlyList<ReportDefinition> All = new[]
    {
        new ReportDefinition(ReportType.AppointmentSummary, ReportCategory.Appointments, "Appointment summary", "Visits by status and by day.", "calendar-days", ReportFilter.Vet | ReportFilter.AppointmentStatus),
        new ReportDefinition(ReportType.NoShowCancellation, ReportCategory.Appointments, "No-show & cancellation", "Who didn't come, and how often.", "user-x", ReportFilter.Vet),
        new ReportDefinition(ReportType.VetWorkload, ReportCategory.Appointments, "Vet workload", "Visits handled by each vet.", "stethoscope", ReportFilter.None),
        new ReportDefinition(ReportType.VisitReasons, ReportCategory.Appointments, "Visit reasons", "Why pets are coming in.", "message-square", ReportFilter.Vet),
        new ReportDefinition(ReportType.GuestBookings, ReportCategory.Appointments, "Guest bookings", "Guests who arrived vs. didn't show.", "user-plus", ReportFilter.Vet),

        new ReportDefinition(ReportType.ConsultationSummary, ReportCategory.Medical, "Consultation summary", "Medical records per day and per vet.", "clipboard-plus", ReportFilter.Vet),
        new ReportDefinition(ReportType.PatientReport, ReportCategory.Medical, "Patient report", "New pets and pets by species.", "paw-print", ReportFilter.Species),
        new ReportDefinition(ReportType.VaccinationReport, ReportCategory.Medical, "Vaccination report", "Vaccines given by type.", "syringe", ReportFilter.Species | ReportFilter.Vet),
        new ReportDefinition(ReportType.OverdueVaccines, ReportCategory.Medical, "Overdue vaccines", "Pets that need their next shot.", "shield-check", ReportFilter.Species, Snapshot: true),

        new ReportDefinition(ReportType.StockLevels, ReportCategory.Inventory, "Stock levels", "Current stock and its value.", "package", ReportFilter.MedicineCategory, Snapshot: true),
        new ReportDefinition(ReportType.StockMovement, ReportCategory.Inventory, "Stock movement", "Every stock in, out, and adjustment.", "refresh-cw", ReportFilter.TransactionType | ReportFilter.MedicineCategory),
        new ReportDefinition(ReportType.MedicineUsage, ReportCategory.Inventory, "Medicine usage", "Most-used medicines and vaccines.", "pill", ReportFilter.MedicineCategory),
        new ReportDefinition(ReportType.ExpiredDamaged, ReportCategory.Inventory, "Expired & damaged", "Stock lost and its value.", "package-x", ReportFilter.MedicineCategory),

        new ReportDefinition(ReportType.DailyCollection, ReportCategory.Financial, "Daily collection", "Money received per day and method.", "wallet", ReportFilter.PaymentMethod | ReportFilter.Cashier),
        new ReportDefinition(ReportType.IncomeSummary, ReportCategory.Financial, "Income summary", "Billed amounts by item type.", "trending-up", ReportFilter.None),
        new ReportDefinition(ReportType.OutstandingBalances, ReportCategory.Financial, "Outstanding balances", "Unpaid bills and how old they are.", "hourglass", ReportFilter.None),
        new ReportDefinition(ReportType.VoidedBills, ReportCategory.Financial, "Voided bills", "Bills cancelled before payment.", "x", ReportFilter.Cashier)
    };

    public static ReportDefinition For(ReportType type) => All.First(d => d.Type == type);

    /// <summary>Section icon and tile colour key: Appointments cyan, Medical mint, Inventory pale blue, Financial light orange.</summary>
    public static (string Name, string Icon, string Tile, string IconColor) Category(ReportCategory category) => category switch
    {
        ReportCategory.Appointments => ("Appointments", "calendar-days", "Cyan", "Navy"),
        ReportCategory.Medical => ("Medical", "stethoscope", "Mint", "CompletedText"),
        ReportCategory.Inventory => ("Inventory", "warehouse", "PaleBlue", "Navy"),
        _ => ("Financial", "circle-dollar-sign", "OrangeSoft", "Orange")
    };
}

/// <summary>The filters of one report run. <see cref="From"/> and <see cref="To"/> are required (rule 4).</summary>
public sealed class ReportFilters
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int? VetId { get; set; }
    public string AppointmentStatus { get; set; }
    public string Species { get; set; }
    public string PaymentMethod { get; set; }
    public int? CashierId { get; set; }
    public string MedicineCategory { get; set; }
    public string TransactionType { get; set; }
}

public enum ColumnKind { Text, Number, Money, Date, DateTime, Percent, Pill }

public sealed record ReportColumn(string Title, ColumnKind Kind, double Width)
{
    public bool RightAligned => Kind is ColumnKind.Number or ColumnKind.Money or ColumnKind.Percent;
}

public sealed record ReportCard(string Label, string Value, string Sub = null, string Icon = "chart-column", string Circle = "Cyan", string IconColor = "Navy");

public enum ChartKind { Bar, StackedBar, Line, HorizontalBar, Donut }

public sealed record ReportSeries(string Name, IReadOnlyList<double> Values);

/// <summary>A chart: x labels and one or more series (stacked for StackedBar; one series for the others).</summary>
public sealed record ReportChart(string Title, ChartKind Kind, IReadOnlyList<string> Labels, IReadOnlyList<ReportSeries> Series, bool Money = false)
{
    public bool IsEmpty => Labels.Count == 0 || Series.All(s => s.Values.All(v => v == 0));
}

/// <summary>Everything a report shows: summary cards, charts, the table (values typed per column) and its total row.</summary>
public sealed class ReportResult
{
    public ReportDefinition Definition { get; init; }
    public ReportFilters Filters { get; init; }
    public IReadOnlyList<ReportCard> Cards { get; init; } = Array.Empty<ReportCard>();
    public ReportChart Chart { get; init; }
    public ReportChart SideChart { get; init; }
    public string TableTitle { get; init; } = "Details";
    public IReadOnlyList<ReportColumn> Columns { get; init; } = Array.Empty<ReportColumn>();

    /// <summary>One object per column: string, int, decimal, double (percent) or DateTime, matching the column kind.</summary>
    public IReadOnlyList<object[]> Rows { get; init; } = Array.Empty<object[]>();

    /// <summary>The TOTAL row (null cells are blank), or null when a total doesn't make sense.</summary>
    public object[] Total { get; init; }

    /// <summary>"As of Nov 18, 2026" for snapshot reports.</summary>
    public string Note { get; init; }

    public bool IsEmpty => Rows.Count == 0;
}
