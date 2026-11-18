using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>The numbers at the top of the Reports home ("Quick look").</summary>
public sealed class ReportQuickLook
{
    public decimal Collected { get; set; }
    public decimal CollectedBefore { get; set; }
    public int Appointments { get; set; }
    public double NoShowRate { get; set; }
    public double NoShowRateBefore { get; set; }
    public int StockAlerts { get; set; }
}

/// <summary>
/// All report queries (read-only, rule 3). Every query is filtered by ClinicID (rule 2), skips archived rows and
/// leaves voided bills out of income (rule 5). Money is from Payment (collections) or BillItem (income) — rule 6.
/// </summary>
public sealed class ReportRepository
{
    private readonly MedicineRepository _medicines = new();
    private readonly VaccinationRepository _vaccinations = new();

    // Buckets: one per day up to 62 days, else one per month (so a year still reads well). -----------------------------

    private sealed record Bucket(DateTime Start, string Label);

    private static List<Bucket> Buckets(DateTime from, DateTime to)
    {
        var list = new List<Bucket>();
        if ((to - from).TotalDays <= 62)
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1)) list.Add(new Bucket(d, d.ToString("MMM d")));
        else
            for (var m = new DateTime(from.Year, from.Month, 1); m <= to.Date; m = m.AddMonths(1)) list.Add(new Bucket(m, m.ToString("MMM yyyy")));
        return list;
    }

    private static DateTime BucketOf(DateTime date, DateTime from, DateTime to) =>
        (to - from).TotalDays <= 62 ? date.Date : new DateTime(date.Year, date.Month, 1);

    private static bool Monthly(ReportFilters f) => (f.To - f.From).TotalDays > 62;

    // Shared helpers ----------------------------------------------------------------------------------------------------

    private static async Task<List<T>> QueryAsync<T>(string sql, Action<SqlCommand> parameters, Func<SqlDataReader, T> map)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command);
        var list = new List<T>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(map(r));
        return list;
    }

    private static Action<SqlCommand> Params(int clinicId, ReportFilters f, Action<SqlCommand> more = null) => c =>
    {
        c.Parameters.AddWithValue("@C", clinicId);
        c.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = f.From.Date });
        c.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = f.To.Date });
        c.Parameters.Add(new SqlParameter("@ToEnd", SqlDbType.DateTime) { Value = f.To.Date.AddDays(1) });
        c.Parameters.AddWithValue("@Vet", (object)f.VetId ?? DBNull.Value);
        c.Parameters.AddWithValue("@Status", (object)f.AppointmentStatus ?? DBNull.Value);
        c.Parameters.AddWithValue("@Species", (object)f.Species ?? DBNull.Value);
        c.Parameters.AddWithValue("@Method", (object)f.PaymentMethod ?? DBNull.Value);
        c.Parameters.AddWithValue("@Cashier", (object)f.CashierId ?? DBNull.Value);
        c.Parameters.AddWithValue("@Category", (object)f.MedicineCategory ?? DBNull.Value);
        c.Parameters.AddWithValue("@Type", (object)f.TransactionType ?? DBNull.Value);
        more?.Invoke(c);
    };

    private static string Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Person(SqlDataReader r, int first, int last) => r.IsDBNull(first) ? "—" : PersonName.Join(r.GetString(first), Text(r, last));
    private static double Rate(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100.0 / whole, 1);
    private static string Pct(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
    private static string Num(int n) => n.ToString("N0");
    private static string Peso(decimal v) => CurrencyHelper.Peso(v);

    /// <summary>Active Veterinarians (vet filter) or Cashier/Staff (cashier filter) of the clinic.</summary>
    public Task<List<(int Id, string Name)>> GetPeopleAsync(int clinicId, int roleId) => QueryAsync(@"
SELECT UserID, FirstName, LastName FROM [User] WHERE ClinicID = @C AND RoleID = @R AND IsDeleted = 0 ORDER BY FirstName, LastName;",
        c => { c.Parameters.AddWithValue("@C", clinicId); c.Parameters.AddWithValue("@R", roleId); },
        r => (r.GetInt32(0), PersonName.Join(r.GetString(1), r.GetString(2))));

    // Quick look -----------------------------------------------------------------------------------------------------------

    public async Task<ReportQuickLook> GetQuickLookAsync(int clinicId, DateTime from, DateTime to, DateTime today)
    {
        var (pFrom, pTo) = DateRangeHelper.Previous(from, to);
        var data = new ReportQuickLook();
        await using (var connection = await Database.OpenAsync())
        await using (var c = new SqlCommand(@"
SELECT ISNULL(SUM(CASE WHEN pm.PaymentDate >= @From AND pm.PaymentDate < @ToEnd THEN pm.AmountPaid END), 0),
       ISNULL(SUM(CASE WHEN pm.PaymentDate >= @PFrom AND pm.PaymentDate < @PToEnd THEN pm.AmountPaid END), 0)
FROM Payment pm JOIN Bill b ON b.BillID = pm.BillID WHERE b.ClinicID = @C AND pm.IsDeleted = 0 AND b.IsDeleted = 0;
SELECT COUNT(CASE WHEN AppointmentDate BETWEEN @From AND @To AND Status <> 'Cancelled' THEN 1 END),
       COUNT(CASE WHEN AppointmentDate BETWEEN @From AND @To AND Status = 'Completed' THEN 1 END),
       COUNT(CASE WHEN AppointmentDate BETWEEN @From AND @To AND Status = 'No-show' THEN 1 END),
       COUNT(CASE WHEN AppointmentDate BETWEEN @PFrom AND @PTo AND Status = 'Completed' THEN 1 END),
       COUNT(CASE WHEN AppointmentDate BETWEEN @PFrom AND @PTo AND Status = 'No-show' THEN 1 END)
FROM Appointment WHERE ClinicID = @C AND IsDeleted = 0;", connection))
        {
            c.Parameters.AddWithValue("@C", clinicId);
            foreach (var (name, value) in new[] { ("@From", from.Date), ("@To", to.Date), ("@ToEnd", to.Date.AddDays(1)), ("@PFrom", pFrom), ("@PTo", pTo), ("@PToEnd", pTo.AddDays(1)) })
                c.Parameters.Add(new SqlParameter(name, SqlDbType.DateTime) { Value = value });
            await using var r = await c.ExecuteReaderAsync();
            await r.ReadAsync();
            data.Collected = r.GetDecimal(0);
            data.CollectedBefore = r.GetDecimal(1);
            await r.NextResultAsync();
            await r.ReadAsync();
            data.Appointments = r.GetInt32(0);
            data.NoShowRate = Rate(r.GetInt32(2), r.GetInt32(1) + r.GetInt32(2));
            data.NoShowRateBefore = Rate(r.GetInt32(4), r.GetInt32(3) + r.GetInt32(4));
        }
        var stock = await _medicines.GetSummaryAsync(clinicId, today);
        int expired = (await _medicines.GetActiveAsync(clinicId)).Count(m => m.IsExpired);
        data.StockAlerts = stock.LowStock + stock.OutOfStock + stock.ExpiringSoon + expired;
        return data;
    }

    // A. Appointments --------------------------------------------------------------------------------------------------

    private const string AppointmentWhere = @"
a.ClinicID = @C AND a.IsDeleted = 0 AND a.AppointmentDate BETWEEN @From AND @To
AND (@Vet IS NULL OR a.VetID = @Vet) AND (@Status IS NULL OR a.Status = @Status)";

    /// <summary>A1: totals by status, and per day (or month).</summary>
    public async Task<ReportResult> AppointmentSummaryAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync("SELECT a.AppointmentDate, a.Status, COUNT(*) FROM Appointment a WHERE " + AppointmentWhere + " GROUP BY a.AppointmentDate, a.Status;",
            Params(clinicId, f), r => (Day: r.GetDateTime(0), Status: r.GetString(1), Count: r.GetInt32(2)));
        var buckets = Buckets(f.From, f.To);
        int Count(DateTime b, Func<string, bool> status) => rows.Where(x => BucketOf(x.Day, f.From, f.To) == b && status(x.Status)).Sum(x => x.Count);
        bool Done(string s) => s == AppointmentStatuses.Completed;
        bool Cancelled(string s) => s == AppointmentStatuses.Cancelled;
        bool NoShow(string s) => s == AppointmentStatuses.NoShow;
        bool Other(string s) => !Done(s) && !Cancelled(s) && !NoShow(s);

        var table = buckets.Select(b => new object[] { b.Start, Count(b.Start, Done), Count(b.Start, Cancelled), Count(b.Start, NoShow), Count(b.Start, Other), Count(b.Start, _ => true) })
            .Where(r => (int)r[5] > 0).ToList();
        int total = rows.Sum(x => x.Count), done = rows.Where(x => Done(x.Status)).Sum(x => x.Count),
            cancelled = rows.Where(x => Cancelled(x.Status)).Sum(x => x.Count), noShow = rows.Where(x => NoShow(x.Status)).Sum(x => x.Count);
        var statuses = rows.GroupBy(x => x.Status).OrderByDescending(g => g.Sum(x => x.Count)).ToList();

        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Total appointments", Num(total), null, "calendar-days", "Cyan"),
                new ReportCard("Completed", Num(done), Pct(Rate(done, total)) + " of all", "circle-check", "Mint", "CompletedText"),
                new ReportCard("Cancelled", Num(cancelled), Pct(Rate(cancelled, total)) + " of all", "x", "LightGray", "BodyText"),
                new ReportCard("No-show", Num(noShow), "No-show rate " + Pct(Rate(noShow, done + noShow)), "user-x", "OrangeSoft", "Orange")
            },
            Chart = new ReportChart("Appointments per " + (Monthly(f) ? "month" : "day"), ChartKind.StackedBar, buckets.Select(b => b.Label).ToList(), new[]
            {
                new ReportSeries("Completed", buckets.Select(b => (double)Count(b.Start, Done)).ToList()),
                new ReportSeries("Cancelled", buckets.Select(b => (double)Count(b.Start, Cancelled)).ToList()),
                new ReportSeries("No-show", buckets.Select(b => (double)Count(b.Start, NoShow)).ToList()),
                new ReportSeries("Other", buckets.Select(b => (double)Count(b.Start, Other)).ToList())
            }),
            SideChart = new ReportChart("By status", ChartKind.Donut, statuses.Select(g => g.Key).ToList(), new[] { new ReportSeries("Appointments", statuses.Select(g => (double)g.Sum(x => x.Count)).ToList()) }),
            TableTitle = Monthly(f) ? "Monthly breakdown" : "Daily breakdown",
            Columns = new[]
            {
                new ReportColumn(Monthly(f) ? "Month" : "Date", ColumnKind.Date, 140), new ReportColumn("Completed", ColumnKind.Number, 110),
                new ReportColumn("Cancelled", ColumnKind.Number, 110), new ReportColumn("No-show", ColumnKind.Number, 110),
                new ReportColumn("Scheduled / other", ColumnKind.Number, 140), new ReportColumn("Total", ColumnKind.Number, 100)
            },
            Rows = table,
            Total = new object[] { "TOTAL", done, cancelled, noShow, total - done - cancelled - noShow, total }
        };
    }

    /// <summary>A2: no-shows and cancellations by day and by vet; no-show rate = no-shows ÷ (completed + no-shows) (rule 7).</summary>
    public async Task<ReportResult> NoShowAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT a.AppointmentDate, a.Status, v.UserID, v.FirstName, v.LastName, COUNT(*)
FROM Appointment a JOIN [User] v ON v.UserID = a.VetID WHERE " + AppointmentWhere + @"
GROUP BY a.AppointmentDate, a.Status, v.UserID, v.FirstName, v.LastName;",
            Params(clinicId, f), r => (Day: r.GetDateTime(0), Status: r.GetString(1), Vet: "Dr. " + PersonName.Join(r.GetString(3), r.GetString(4)), Count: r.GetInt32(5)));
        int Sum(IEnumerable<(DateTime Day, string Status, string Vet, int Count)> set, string status) => set.Where(x => x.Status == status).Sum(x => x.Count);
        int total = rows.Sum(x => x.Count), done = Sum(rows, "Completed"), noShow = Sum(rows, "No-show"), cancelled = Sum(rows, "Cancelled");
        var buckets = Buckets(f.From, f.To);
        var byVet = rows.GroupBy(x => x.Vet).Select(g =>
        {
            int t = g.Sum(x => x.Count), d = Sum(g, "Completed"), n = Sum(g, "No-show"), c = Sum(g, "Cancelled");
            return new object[] { g.Key, t, d, n, c, Rate(n, d + n), Rate(c, t) };
        }).OrderByDescending(r => (int)r[3] + (int)r[4]).ToList();

        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("No-shows", Num(noShow), null, "user-x", "OrangeSoft", "Orange"),
                new ReportCard("Cancellations", Num(cancelled), null, "x", "LightGray", "BodyText"),
                new ReportCard("No-show rate", Pct(Rate(noShow, done + noShow)), "No-shows ÷ (completed + no-shows)", "trending-down", "WarningBox", "ConsultText"),
                new ReportCard("Cancellation rate", Pct(Rate(cancelled, total)), "of " + Num(total) + " appointments", "calendar-days", "Cyan")
            },
            Chart = new ReportChart("No-shows and cancellations per " + (Monthly(f) ? "month" : "day"), ChartKind.StackedBar, buckets.Select(b => b.Label).ToList(), new[]
            {
                new ReportSeries("No-show", buckets.Select(b => (double)rows.Where(x => BucketOf(x.Day, f.From, f.To) == b.Start && x.Status == "No-show").Sum(x => x.Count)).ToList()),
                new ReportSeries("Cancelled", buckets.Select(b => (double)rows.Where(x => BucketOf(x.Day, f.From, f.To) == b.Start && x.Status == "Cancelled").Sum(x => x.Count)).ToList())
            }),
            SideChart = new ReportChart("No-shows by vet", ChartKind.Donut, byVet.Select(r => (string)r[0]).ToList(), new[] { new ReportSeries("No-shows", byVet.Select(r => (double)(int)r[3]).ToList()) }),
            TableTitle = "By vet",
            Columns = new[]
            {
                new ReportColumn("Vet", ColumnKind.Text, 190), new ReportColumn("Appointments", ColumnKind.Number, 115), new ReportColumn("Completed", ColumnKind.Number, 100),
                new ReportColumn("No-show", ColumnKind.Number, 90), new ReportColumn("Cancelled", ColumnKind.Number, 95),
                new ReportColumn("No-show rate", ColumnKind.Percent, 115), new ReportColumn("Cancel rate", ColumnKind.Percent, 105)
            },
            Rows = byVet,
            Total = new object[] { "TOTAL", total, done, noShow, cancelled, Rate(noShow, done + noShow), Rate(cancelled, total) }
        };
    }

    /// <summary>A3: appointments and completed visits per vet.</summary>
    public async Task<ReportResult> VetWorkloadAsync(int clinicId, ReportFilters f)
    {
        var vets = await QueryAsync(@"
SELECT v.FirstName, v.LastName,
       COUNT(CASE WHEN a.Status <> 'Cancelled' THEN 1 END), COUNT(CASE WHEN a.Status = 'Completed' THEN 1 END),
       COUNT(CASE WHEN a.Status = 'No-show' THEN 1 END), COUNT(CASE WHEN a.Status = 'Cancelled' THEN 1 END),
       (SELECT COUNT(*) FROM MedicalRecord r JOIN Appointment x ON x.AppointmentID = r.AppointmentID
        WHERE r.VetID = v.UserID AND r.IsDeleted = 0 AND x.ClinicID = @C AND r.RecordDate >= @From AND r.RecordDate < @ToEnd)
FROM [User] v
LEFT JOIN Appointment a ON a.VetID = v.UserID AND a.ClinicID = @C AND a.IsDeleted = 0 AND a.AppointmentDate BETWEEN @From AND @To
WHERE v.ClinicID = @C AND v.RoleID = 3 AND v.IsDeleted = 0
GROUP BY v.UserID, v.FirstName, v.LastName;", Params(clinicId, f),
            r => new object[] { "Dr. " + PersonName.Join(r.GetString(0), r.GetString(1)), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), Rate(r.GetInt32(3), r.GetInt32(2)) });
        vets = vets.OrderByDescending(r => (int)r[1]).ToList();
        int total = vets.Sum(r => (int)r[1]), done = vets.Sum(r => (int)r[2]);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Vets", Num(vets.Count), null, "users", "Cyan"),
                new ReportCard("Appointments", Num(total), "Not counting cancelled", "calendar-days", "PaleBlue"),
                new ReportCard("Completed visits", Num(done), Pct(Rate(done, total)) + " completed", "circle-check", "Mint", "CompletedText"),
                new ReportCard("Average per vet", vets.Count == 0 ? "0" : (total / (double)vets.Count).ToString("0.#"), null, "stethoscope", "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("Appointments per vet", ChartKind.HorizontalBar, vets.Select(r => (string)r[0]).ToList(), new[] { new ReportSeries("Appointments", vets.Select(r => (double)(int)r[1]).ToList()) }),
            SideChart = new ReportChart("Completed visits", ChartKind.Donut, vets.Select(r => (string)r[0]).ToList(), new[] { new ReportSeries("Completed", vets.Select(r => (double)(int)r[2]).ToList()) }),
            TableTitle = "By vet",
            Columns = new[]
            {
                new ReportColumn("Vet", ColumnKind.Text, 190), new ReportColumn("Appointments", ColumnKind.Number, 115), new ReportColumn("Completed", ColumnKind.Number, 100),
                new ReportColumn("No-show", ColumnKind.Number, 90), new ReportColumn("Cancelled", ColumnKind.Number, 95), new ReportColumn("Records", ColumnKind.Number, 90),
                new ReportColumn("Completion", ColumnKind.Percent, 105)
            },
            Rows = vets,
            Total = new object[] { "TOTAL", total, done, vets.Sum(r => (int)r[3]), vets.Sum(r => (int)r[4]), vets.Sum(r => (int)r[5]), Rate(done, total) }
        };
    }

    /// <summary>A4: visit reasons (the quick-pick chip before ":", or the reason itself).</summary>
    public async Task<ReportResult> VisitReasonsAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT CASE WHEN CHARINDEX(':', a.Reason) > 0 THEN LEFT(a.Reason, CHARINDEX(':', a.Reason) - 1) ELSE a.Reason END AS Reason, COUNT(*)
FROM Appointment a WHERE " + AppointmentWhere + @" AND a.Status <> 'Cancelled'
GROUP BY CASE WHEN CHARINDEX(':', a.Reason) > 0 THEN LEFT(a.Reason, CHARINDEX(':', a.Reason) - 1) ELSE a.Reason END
ORDER BY COUNT(*) DESC;", Params(clinicId, f), r => (Reason: r.GetString(0).Trim(), Count: r.GetInt32(1)));
        int total = rows.Sum(x => x.Count);
        var top = rows.Take(8).ToList();
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Visits", Num(total), "Not counting cancelled", "calendar-days", "Cyan"),
                new ReportCard("Different reasons", Num(rows.Count), null, "message-square", "PaleBlue"),
                new ReportCard("Top reason", rows.FirstOrDefault().Reason ?? "—", rows.Count == 0 ? null : Pct(Rate(rows[0].Count, total)) + " of visits", "trending-up", "Mint", "CompletedText")
            },
            Chart = new ReportChart("Most common reasons", ChartKind.HorizontalBar, top.Select(x => x.Reason).ToList(), new[] { new ReportSeries("Visits", top.Select(x => (double)x.Count).ToList()) }),
            SideChart = new ReportChart("Share of visits", ChartKind.Donut, top.Select(x => x.Reason).ToList(), new[] { new ReportSeries("Visits", top.Select(x => (double)x.Count).ToList()) }),
            TableTitle = "Reasons",
            Columns = new[] { new ReportColumn("Reason", ColumnKind.Text, 280), new ReportColumn("Visits", ColumnKind.Number, 120), new ReportColumn("Share", ColumnKind.Percent, 120) },
            Rows = rows.Select(x => new object[] { x.Reason, x.Count, Rate(x.Count, total) }).ToList(),
            Total = new object[] { "TOTAL", total, total == 0 ? 0.0 : 100.0 }
        };
    }

    /// <summary>A5: guest bookings and how they ended (arrived &amp; registered / no-show / cancelled / still waiting).</summary>
    public async Task<ReportResult> GuestBookingsAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT a.AppointmentDate, a.AppointmentTime,
       COALESCE(a.GuestOwnerName, o.FirstName + ' ' + o.LastName), COALESCE(a.GuestPetName, p.PetName),
       v.FirstName, v.LastName, a.Status, a.WasGuest
FROM Appointment a
JOIN [User] v ON v.UserID = a.VetID
LEFT JOIN Pet p ON p.PetID = a.PetID
LEFT JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE " + AppointmentWhere + @" AND (a.PetID IS NULL OR a.WasGuest = 1)
ORDER BY a.AppointmentDate DESC, a.AppointmentTime DESC;", Params(clinicId, f), r =>
        {
            string status = r.GetString(6);
            bool arrived = r.GetBoolean(7);
            string result = arrived ? "Arrived & registered" : status == "No-show" ? "No-show" : status == "Cancelled" ? "Cancelled" : "Waiting";
            return new object[] { r.GetDateTime(0) + r.GetTimeSpan(1), Text(r, 2) ?? "Guest (details removed)", Text(r, 3) ?? "—", "Dr. " + PersonName.Join(r.GetString(4), r.GetString(5)), result };
        });
        int Count(string result) => rows.Count(r => (string)r[4] == result);
        var results = new[] { "Arrived & registered", "No-show", "Cancelled", "Waiting" };
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Guest bookings", Num(rows.Count), null, "user-plus", "Cyan"),
                new ReportCard("Arrived & registered", Num(Count(results[0])), Pct(Rate(Count(results[0]), rows.Count)), "circle-check", "Mint", "CompletedText"),
                new ReportCard("No-show", Num(Count(results[1])), null, "user-x", "OrangeSoft", "Orange"),
                new ReportCard("Cancelled", Num(Count(results[2])), Count(results[3]) > 0 ? Count(results[3]) + " still waiting" : null, "x", "LightGray", "BodyText")
            },
            Chart = new ReportChart("Guest bookings by result", ChartKind.Donut, results.ToList(), new[] { new ReportSeries("Bookings", results.Select(x => (double)Count(x)).ToList()) }),
            TableTitle = "Guest bookings",
            Columns = new[]
            {
                new ReportColumn("Date & time", ColumnKind.DateTime, 150), new ReportColumn("Guest", ColumnKind.Text, 190), new ReportColumn("Pet", ColumnKind.Text, 130),
                new ReportColumn("Vet", ColumnKind.Text, 180), new ReportColumn("Result", ColumnKind.Pill, 170)
            },
            Rows = rows
        };
    }

    // B. Medical ----------------------------------------------------------------------------------------------------------

    /// <summary>B1: medical records per day and per vet.</summary>
    public async Task<ReportResult> ConsultationSummaryAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT r.RecordDate, v.FirstName, v.LastName, r.PetID,
       (SELECT COUNT(*) FROM Prescription x WHERE x.RecordID = r.RecordID AND x.IsDeleted = 0),
       (SELECT COUNT(*) FROM Vaccination x WHERE x.RecordID = r.RecordID AND x.IsDeleted = 0)
FROM MedicalRecord r JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @C
JOIN [User] v ON v.UserID = r.VetID
WHERE r.IsDeleted = 0 AND r.RecordDate >= @From AND r.RecordDate < @ToEnd AND (@Vet IS NULL OR r.VetID = @Vet);", Params(clinicId, f),
            r => (Day: r.GetDateTime(0), Vet: "Dr. " + PersonName.Join(r.GetString(1), r.GetString(2)), Pet: r.GetInt32(3), Rx: r.GetInt32(4), Vax: r.GetInt32(5)));
        var buckets = Buckets(f.From, f.To);
        var byVet = rows.GroupBy(x => x.Vet).Select(g => new object[] { g.Key, g.Count(), g.Select(x => x.Pet).Distinct().Count(), g.Sum(x => x.Rx), g.Sum(x => x.Vax) })
            .OrderByDescending(r => (int)r[1]).ToList();
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Medical records", Num(rows.Count), null, "clipboard-plus", "Cyan"),
                new ReportCard("Pets seen", Num(rows.Select(x => x.Pet).Distinct().Count()), null, "paw-print", "Mint", "CompletedText"),
                new ReportCard("Prescriptions", Num(rows.Sum(x => x.Rx)), null, "pill", "WarningBox", "ConsultText"),
                new ReportCard("Vaccinations", Num(rows.Sum(x => x.Vax)), null, "syringe", "PaleBlue")
            },
            Chart = new ReportChart("Records per " + (Monthly(f) ? "month" : "day"), ChartKind.Bar, buckets.Select(b => b.Label).ToList(),
                new[] { new ReportSeries("Records", buckets.Select(b => (double)rows.Count(x => BucketOf(x.Day, f.From, f.To) == b.Start)).ToList()) }),
            SideChart = new ReportChart("By vet", ChartKind.Donut, byVet.Select(r => (string)r[0]).ToList(), new[] { new ReportSeries("Records", byVet.Select(r => (double)(int)r[1]).ToList()) }),
            TableTitle = "By vet",
            Columns = new[]
            {
                new ReportColumn("Vet", ColumnKind.Text, 200), new ReportColumn("Records", ColumnKind.Number, 110), new ReportColumn("Pets seen", ColumnKind.Number, 110),
                new ReportColumn("Prescriptions", ColumnKind.Number, 120), new ReportColumn("Vaccinations", ColumnKind.Number, 120)
            },
            Rows = byVet,
            Total = new object[] { "TOTAL", rows.Count, rows.Select(x => x.Pet).Distinct().Count(), rows.Sum(x => x.Rx), rows.Sum(x => x.Vax) }
        };
    }

    /// <summary>B2: new pets in the period, and every pet by species and breed.</summary>
    public async Task<ReportResult> PatientReportAsync(int clinicId, ReportFilters f)
    {
        var pets = await QueryAsync(@"
SELECT p.Species, ISNULL(NULLIF(LTRIM(RTRIM(p.Breed)), ''), '(not set)'), p.CreatedAt, p.OwnerID
FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE o.ClinicID = @C AND p.IsDeleted = 0 AND o.IsDeleted = 0 AND (@Species IS NULL OR p.Species = @Species);", Params(clinicId, f),
            r => (Species: r.GetString(0), Breed: r.GetString(1), Created: r.GetDateTime(2), Owner: r.GetInt32(3)));
        bool IsNew(DateTime d) => d >= f.From.Date && d < f.To.Date.AddDays(1);
        var buckets = Buckets(f.From, f.To);
        var groups = pets.GroupBy(x => (x.Species, x.Breed)).Select(g => new object[] { g.Key.Species, g.Key.Breed, g.Count(), g.Count(x => IsNew(x.Created)) })
            .OrderBy(r => (string)r[0]).ThenByDescending(r => (int)r[2]).ToList();
        var species = pets.GroupBy(x => x.Species).OrderByDescending(g => g.Count()).ToList();
        int newPets = pets.Count(x => IsNew(x.Created));
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Pets on record", Num(pets.Count), null, "paw-print", "Cyan"),
                new ReportCard("New this period", Num(newPets), null, "user-plus", "Mint", "CompletedText"),
                new ReportCard("Owners", Num(pets.Select(x => x.Owner).Distinct().Count()), null, "users", "PaleBlue"),
                new ReportCard("Species", Num(species.Count), species.Count == 0 ? null : "Most: " + species[0].Key, "dog", "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("New pets per " + (Monthly(f) ? "month" : "day"), ChartKind.Bar, buckets.Select(b => b.Label).ToList(),
                new[] { new ReportSeries("New pets", buckets.Select(b => (double)pets.Count(x => IsNew(x.Created) && BucketOf(x.Created, f.From, f.To) == b.Start)).ToList()) }),
            SideChart = new ReportChart("By species", ChartKind.Donut, species.Select(g => g.Key).ToList(), new[] { new ReportSeries("Pets", species.Select(g => (double)g.Count()).ToList()) }),
            TableTitle = "By species and breed",
            Columns = new[] { new ReportColumn("Species", ColumnKind.Text, 150), new ReportColumn("Breed", ColumnKind.Text, 220), new ReportColumn("Pets", ColumnKind.Number, 110), new ReportColumn("New this period", ColumnKind.Number, 140) },
            Rows = groups,
            Total = new object[] { "TOTAL", null, pets.Count, newPets }
        };
    }

    /// <summary>B3: vaccines given, by vaccine and species.</summary>
    public async Task<ReportResult> VaccinationReportAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT m.MedicineName, p.Species, x.PetID, x.DateGiven
FROM Vaccination x JOIN Medicine m ON m.MedicineID = x.MedicineID AND m.ClinicID = @C
JOIN Pet p ON p.PetID = x.PetID
WHERE x.IsDeleted = 0 AND x.DateGiven >= @From AND x.DateGiven < @ToEnd
  AND (@Species IS NULL OR p.Species = @Species) AND (@Vet IS NULL OR x.GivenBy = @Vet);", Params(clinicId, f),
            r => (Vaccine: r.GetString(0), Species: r.GetString(1), Pet: r.GetInt32(2), Date: r.GetDateTime(3)));
        var byVaccine = rows.GroupBy(x => x.Vaccine).OrderByDescending(g => g.Count()).ToList();
        var bySpecies = rows.GroupBy(x => x.Species).OrderByDescending(g => g.Count()).ToList();
        var table = rows.GroupBy(x => (x.Vaccine, x.Species)).Select(g => new object[] { g.Key.Vaccine, g.Key.Species, g.Count(), g.Select(x => x.Pet).Distinct().Count() })
            .OrderByDescending(r => (int)r[2]).ToList();
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Vaccines given", Num(rows.Count), null, "syringe", "Cyan"),
                new ReportCard("Pets vaccinated", Num(rows.Select(x => x.Pet).Distinct().Count()), null, "paw-print", "Mint", "CompletedText"),
                new ReportCard("Vaccine types", Num(byVaccine.Count), null, "shield-check", "PaleBlue"),
                new ReportCard("Most given", byVaccine.FirstOrDefault()?.Key ?? "—", byVaccine.Count == 0 ? null : Num(byVaccine[0].Count()) + " doses", "trending-up", "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("Doses by vaccine", ChartKind.HorizontalBar, byVaccine.Select(g => g.Key).ToList(), new[] { new ReportSeries("Doses", byVaccine.Select(g => (double)g.Count()).ToList()) }),
            SideChart = new ReportChart("By species", ChartKind.Donut, bySpecies.Select(g => g.Key).ToList(), new[] { new ReportSeries("Doses", bySpecies.Select(g => (double)g.Count()).ToList()) }),
            TableTitle = "By vaccine and species",
            Columns = new[] { new ReportColumn("Vaccine", ColumnKind.Text, 240), new ReportColumn("Species", ColumnKind.Text, 140), new ReportColumn("Doses", ColumnKind.Number, 110), new ReportColumn("Pets", ColumnKind.Number, 110) },
            Rows = table,
            Total = new object[] { "TOTAL", null, rows.Count, rows.Select(x => x.Pet).Distinct().Count() }
        };
    }

    /// <summary>B4: pets with overdue or due-soon (30 days) vaccines, with the owner's contact no. — as of today.</summary>
    public async Task<ReportResult> OverdueVaccinesAsync(int clinicId, ReportFilters f, DateTime today)
    {
        var due = await _vaccinations.GetDueListAsync(clinicId, DueFilter.All, null, f.Species, today);
        var rows = due.Select(v =>
        {
            var status = VaccineScheduleHelper.StatusOf(v.NextDueDate, today);
            return new object[]
            {
                v.PetName, v.Species, v.OwnerName, v.OwnerContactDisplay, v.VaccineName, v.NextDueDate ?? today,
                status == VaccineStatus.Overdue ? "Overdue" : "Due soon", VaccineScheduleHelper.DaysText(v.NextDueDate ?? today, today)
            };
        }).ToList();
        int overdue = rows.Count(r => (string)r[6] == "Overdue");
        int week = due.Count(v => v.NextDueDate >= today && v.NextDueDate <= today.AddDays(7));
        return new ReportResult
        {
            Note = "As of " + today.ToString("MMM d, yyyy") + " — the date range doesn't change this list.",
            Cards = new[]
            {
                new ReportCard("Overdue", Num(overdue), null, "circle-alert", "OrangeSoft", "Orange"),
                new ReportCard("Due in 7 days", Num(week), null, "clock", "WarningBox", "ConsultText"),
                new ReportCard("Due in " + VetCare.Helpers.VaccineScheduleHelper.DueSoonDays + " days", Num(rows.Count - overdue), null, "calendar-days", "Cyan"),
                new ReportCard("Owners to contact", Num(due.Select(v => v.OwnerId).Distinct().Count()), null, "phone", "PaleBlue")
            },
            Chart = new ReportChart("Overdue vs due soon", ChartKind.Donut, new[] { "Overdue", "Due soon" }, new[] { new ReportSeries("Pets", new double[] { overdue, rows.Count - overdue }) }),
            TableTitle = "Pets to contact",
            Columns = new[]
            {
                new ReportColumn("Pet", ColumnKind.Text, 120), new ReportColumn("Species", ColumnKind.Text, 90), new ReportColumn("Owner", ColumnKind.Text, 160),
                new ReportColumn("Contact no.", ColumnKind.Text, 130), new ReportColumn("Vaccine", ColumnKind.Text, 170), new ReportColumn("Due", ColumnKind.Date, 120),
                new ReportColumn("Status", ColumnKind.Pill, 105), new ReportColumn("When", ColumnKind.Text, 120)
            },
            Rows = rows
        };
    }

    // C. Inventory ---------------------------------------------------------------------------------------------------------

    /// <summary>C1: every medicine with its status and stock value (stock × price, rule 8) — as of today.</summary>
    public async Task<ReportResult> StockLevelsAsync(int clinicId, ReportFilters f, DateTime today)
    {
        var meds = (await _medicines.GetActiveAsync(clinicId)).Where(m => f.MedicineCategory == null || m.Category == f.MedicineCategory).ToList();
        var rows = meds.Select(m => new object[]
        {
            m.MedicineName, m.Category, m.StockQty, m.ReorderLevel, m.UnitPrice, m.StockQty * m.UnitPrice, (object)m.ExpiryDate ?? "—",
            StockStatusHelper.Style(StockStatusHelper.StatusOf(m, today)).Text
        }).ToList();
        var byCategory = meds.GroupBy(m => m.Category).Select(g => (Category: g.Key, Value: g.Sum(m => m.StockQty * m.UnitPrice))).OrderByDescending(x => x.Value).ToList();
        var byStatus = rows.GroupBy(r => (string)r[7]).ToList();
        int Status(StockStatus s) => meds.Count(m => StockStatusHelper.StatusOf(m, today) == s);
        decimal value = meds.Sum(m => m.StockQty * m.UnitPrice);
        return new ReportResult
        {
            Note = "As of " + today.ToString("MMM d, yyyy") + " — the date range doesn't change stock levels.",
            Cards = new[]
            {
                new ReportCard("Total items", Num(meds.Count), null, "package", "Cyan"),
                new ReportCard("Stock value", Peso(value), "Stock × unit price", "wallet", "Mint", "CompletedText"),
                new ReportCard("Low stock", Num(Status(StockStatus.LowStock)), null, "trending-down", "WarningBox", "ConsultText"),
                new ReportCard("Out of stock", Num(Status(StockStatus.OutOfStock)), Status(StockStatus.Expired) > 0 ? Status(StockStatus.Expired) + " expired" : null, "package-x", "DangerSoft", "Danger")
            },
            Chart = new ReportChart("Stock value by category", ChartKind.HorizontalBar, byCategory.Select(x => x.Category).ToList(), new[] { new ReportSeries("Value", byCategory.Select(x => (double)x.Value).ToList()) }, Money: true),
            SideChart = new ReportChart("By status", ChartKind.Donut, byStatus.Select(g => g.Key).ToList(), new[] { new ReportSeries("Items", byStatus.Select(g => (double)g.Count()).ToList()) }),
            TableTitle = "Medicines",
            Columns = new[]
            {
                new ReportColumn("Medicine", ColumnKind.Text, 190), new ReportColumn("Category", ColumnKind.Text, 115), new ReportColumn("Stock", ColumnKind.Number, 75),
                new ReportColumn("Reorder", ColumnKind.Number, 75), new ReportColumn("Unit price", ColumnKind.Money, 95), new ReportColumn("Stock value", ColumnKind.Money, 115),
                new ReportColumn("Expiry", ColumnKind.Date, 110), new ReportColumn("Status", ColumnKind.Pill, 120)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, meds.Sum(m => m.StockQty), null, null, value, null, null }
        };
    }

    private const string StockLogFrom = @"
FROM InventoryTransaction t JOIN Medicine m ON m.MedicineID = t.MedicineID
LEFT JOIN [User] u ON u.UserID = t.CreatedBy
WHERE t.ClinicID = @C AND t.CreatedAt >= @From AND t.CreatedAt < @ToEnd AND (@Category IS NULL OR m.Category = @Category)";

    /// <summary>C2: every stock change in the period.</summary>
    public async Task<ReportResult> StockMovementAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT t.CreatedAt, m.MedicineName, m.Unit, t.TransactionType, t.Quantity, u.FirstName, u.LastName, t.Notes" + StockLogFrom + @"
  AND (@Type IS NULL OR t.TransactionType = @Type)
ORDER BY t.CreatedAt DESC, t.TransactionID DESC;", Params(clinicId, f),
            r => new object[] { r.GetDateTime(0), r.GetString(1) + " (" + r.GetString(2) + ")", r.GetString(3), r.GetInt32(4), Person(r, 5, 6), Text(r, 7) ?? "—" });
        var buckets = Buckets(f.From, f.To);
        int In = rows.Where(r => (int)r[3] > 0).Sum(r => (int)r[3]), Out = -rows.Where(r => (int)r[3] < 0).Sum(r => (int)r[3]);
        var byType = rows.GroupBy(r => (string)r[2]).OrderByDescending(g => g.Count()).ToList();
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Stock changes", Num(rows.Count), null, "refresh-cw", "Cyan"),
                new ReportCard("Units added", Num(In), "Stock in and corrections", "plus", "Mint", "CompletedText"),
                new ReportCard("Units removed", Num(Out), "Dispensed, used and removed", "minus", "OrangeSoft", "Orange"),
                new ReportCard("Net change", (In - Out >= 0 ? "+" : "−") + Num(Math.Abs(In - Out)), null, "package", "PaleBlue")
            },
            Chart = new ReportChart("Units in and out per " + (Monthly(f) ? "month" : "day"), ChartKind.StackedBar, buckets.Select(b => b.Label).ToList(), new[]
            {
                new ReportSeries("In", buckets.Select(b => (double)rows.Where(r => BucketOf((DateTime)r[0], f.From, f.To) == b.Start && (int)r[3] > 0).Sum(r => (int)r[3])).ToList()),
                new ReportSeries("Out", buckets.Select(b => (double)-rows.Where(r => BucketOf((DateTime)r[0], f.From, f.To) == b.Start && (int)r[3] < 0).Sum(r => (int)r[3])).ToList())
            }),
            SideChart = new ReportChart("By type", ChartKind.Donut, byType.Select(g => g.Key).ToList(), new[] { new ReportSeries("Changes", byType.Select(g => (double)g.Count()).ToList()) }),
            TableTitle = "Stock changes",
            Columns = new[]
            {
                new ReportColumn("Date & time", ColumnKind.DateTime, 150), new ReportColumn("Medicine", ColumnKind.Text, 200), new ReportColumn("Type", ColumnKind.Pill, 120),
                new ReportColumn("Qty", ColumnKind.Number, 70), new ReportColumn("Done by", ColumnKind.Text, 150), new ReportColumn("Remarks", ColumnKind.Text, 190)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, null, In - Out, null, null }
        };
    }

    /// <summary>C3: most-used medicines and vaccines (Dispensed + Vaccine Used), by quantity and amount.</summary>
    public async Task<ReportResult> MedicineUsageAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT m.MedicineName, m.Category, m.Unit, -SUM(t.Quantity), m.UnitPrice" + StockLogFrom + @"
  AND t.TransactionType IN ('Dispensed', 'Vaccine Used')
GROUP BY m.MedicineID, m.MedicineName, m.Category, m.Unit, m.UnitPrice;", Params(clinicId, f),
            r => new object[] { r.GetString(0), r.GetString(1), r.GetInt32(3), r.GetString(2), r.GetInt32(3) * r.GetDecimal(4) });
        rows = rows.OrderByDescending(r => (decimal)r[4]).ToList();
        var top = rows.Take(8).ToList();
        var byCategory = rows.GroupBy(r => (string)r[1]).Select(g => (g.Key, Value: g.Sum(r => (decimal)r[4]))).OrderByDescending(x => x.Value).ToList();
        decimal amount = rows.Sum(r => (decimal)r[4]);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Units used", Num(rows.Sum(r => (int)r[2])), null, "pill", "Cyan"),
                new ReportCard("Amount", Peso(amount), "Units × unit price", "wallet", "Mint", "CompletedText"),
                new ReportCard("Medicines used", Num(rows.Count), null, "package", "PaleBlue"),
                new ReportCard("Top item", rows.Count == 0 ? "—" : (string)rows[0][0], rows.Count == 0 ? null : Peso((decimal)rows[0][4]), "trending-up", "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("Top items by amount", ChartKind.HorizontalBar, top.Select(r => (string)r[0]).ToList(), new[] { new ReportSeries("Amount", top.Select(r => (double)(decimal)r[4]).ToList()) }, Money: true),
            SideChart = new ReportChart("By category", ChartKind.Donut, byCategory.Select(x => x.Key).ToList(), new[] { new ReportSeries("Amount", byCategory.Select(x => (double)x.Value).ToList()) }, Money: true),
            TableTitle = "Usage by medicine",
            Columns = new[]
            {
                new ReportColumn("Medicine", ColumnKind.Text, 230), new ReportColumn("Category", ColumnKind.Text, 130), new ReportColumn("Units used", ColumnKind.Number, 110),
                new ReportColumn("Unit", ColumnKind.Text, 90), new ReportColumn("Amount", ColumnKind.Money, 130)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, rows.Sum(r => (int)r[2]), null, amount }
        };
    }

    /// <summary>C4: medicines removed as Expired or Damaged, and the value lost (quantity × price, rule 8).</summary>
    public async Task<ReportResult> ExpiredDamagedAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT t.CreatedAt, m.MedicineName, t.TransactionType, -t.Quantity, -t.Quantity * m.UnitPrice, u.FirstName, u.LastName, t.Notes" + StockLogFrom + @"
  AND t.TransactionType IN ('Expired', 'Damaged')
ORDER BY t.CreatedAt DESC;", Params(clinicId, f),
            r => new object[] { r.GetDateTime(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetDecimal(4), Person(r, 5, 6), Text(r, 7) ?? "—" });
        var buckets = Buckets(f.From, f.To);
        decimal lost = rows.Sum(r => (decimal)r[4]);
        decimal Lost(string type) => rows.Where(r => (string)r[2] == type).Sum(r => (decimal)r[4]);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Removals", Num(rows.Count), null, "package-x", "Cyan"),
                new ReportCard("Units lost", Num(rows.Sum(r => (int)r[3])), null, "minus", "WarningBox", "ConsultText"),
                new ReportCard("Value lost", Peso(lost), "Quantity × unit price", "wallet", "DangerSoft", "Danger"),
                new ReportCard("Expired / damaged", Peso(Lost("Expired")) + " / " + Peso(Lost("Damaged")), null, "calendar-clock", "LightGray", "BodyText")
            },
            Chart = new ReportChart("Value lost per " + (Monthly(f) ? "month" : "day"), ChartKind.Bar, buckets.Select(b => b.Label).ToList(),
                new[] { new ReportSeries("Value lost", buckets.Select(b => (double)rows.Where(r => BucketOf((DateTime)r[0], f.From, f.To) == b.Start).Sum(r => (decimal)r[4])).ToList()) }, Money: true),
            SideChart = new ReportChart("By type", ChartKind.Donut, new[] { "Expired", "Damaged" }, new[] { new ReportSeries("Value", new[] { (double)Lost("Expired"), (double)Lost("Damaged") }) }, Money: true),
            TableTitle = "Removed stock",
            Columns = new[]
            {
                new ReportColumn("Date & time", ColumnKind.DateTime, 145), new ReportColumn("Medicine", ColumnKind.Text, 180), new ReportColumn("Type", ColumnKind.Pill, 100),
                new ReportColumn("Qty", ColumnKind.Number, 65), new ReportColumn("Value lost", ColumnKind.Money, 110), new ReportColumn("Done by", ColumnKind.Text, 140),
                new ReportColumn("Reason", ColumnKind.Text, 170)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, null, rows.Sum(r => (int)r[3]), lost, null, null }
        };
    }

    // D. Financial ---------------------------------------------------------------------------------------------------------

    /// <summary>D1: money actually received (Payment), per day and per method (rule 6).</summary>
    public async Task<ReportResult> DailyCollectionAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT pm.PaymentDate, pm.PaymentMethod, pm.AmountPaid
FROM Payment pm JOIN Bill b ON b.BillID = pm.BillID
WHERE b.ClinicID = @C AND pm.IsDeleted = 0 AND b.IsDeleted = 0 AND pm.PaymentDate >= @From AND pm.PaymentDate < @ToEnd
  AND (@Method IS NULL OR pm.PaymentMethod = @Method) AND (@Cashier IS NULL OR pm.ReceivedBy = @Cashier);", Params(clinicId, f),
            r => (Date: r.GetDateTime(0), Method: Text(r, 1) ?? PaymentMethods.Cash, Amount: r.GetDecimal(2)));
        var buckets = Buckets(f.From, f.To);
        var methods = PaymentMethods.All;
        var table = buckets.Select(b =>
        {
            var set = rows.Where(x => BucketOf(x.Date, f.From, f.To) == b.Start).ToList();
            var row = new List<object> { b.Start, set.Count };
            row.AddRange(methods.Select(m => (object)set.Where(x => x.Method == m).Sum(x => x.Amount)));
            row.Add(set.Sum(x => x.Amount));
            return row.ToArray();
        }).Where(r => (int)r[1] > 0).OrderByDescending(r => (DateTime)r[0]).ToList();
        decimal total = rows.Sum(x => x.Amount);
        int days = (f.To.Date - f.From.Date).Days + 1;
        var byMethod = methods.Select(m => (Method: m, Amount: rows.Where(x => x.Method == m).Sum(x => x.Amount))).ToList();
        var top = byMethod.OrderByDescending(x => x.Amount).First();
        var totalRow = new List<object> { "TOTAL", rows.Count };
        totalRow.AddRange(byMethod.Select(x => (object)x.Amount));
        totalRow.Add(total);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Total collected", Peso(total), null, "wallet", "Mint", "CompletedText"),
                new ReportCard("Number of payments", Num(rows.Count), null, "receipt", "Cyan"),
                new ReportCard("Average per day", Peso(Math.Round(total / days, 2)), "Over " + days + (days == 1 ? " day" : " days"), "calendar-days", "PaleBlue"),
                new ReportCard("Top method", total == 0 ? "—" : top.Method, total == 0 ? null : Pct(Rate((int)(top.Amount * 100), (int)(total * 100))), PaymentMethods.IconFor(top.Method), "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("Collections per " + (Monthly(f) ? "month" : "day"), ChartKind.Bar, buckets.Select(b => b.Label).ToList(),
                new[] { new ReportSeries("Collected", buckets.Select(b => (double)rows.Where(x => BucketOf(x.Date, f.From, f.To) == b.Start).Sum(x => x.Amount)).ToList()) }, Money: true),
            SideChart = new ReportChart("By payment method", ChartKind.Donut, byMethod.Select(x => x.Method).ToList(), new[] { new ReportSeries("Collected", byMethod.Select(x => (double)x.Amount).ToList()) }, Money: true),
            TableTitle = Monthly(f) ? "Monthly breakdown" : "Daily breakdown",
            Columns = new[]
            {
                new ReportColumn(Monthly(f) ? "Month" : "Date", ColumnKind.Date, 110), new ReportColumn("Payments", ColumnKind.Number, 85),
                new ReportColumn("Cash", ColumnKind.Money, 100), new ReportColumn("GCash", ColumnKind.Money, 100), new ReportColumn("Maya", ColumnKind.Money, 95),
                new ReportColumn("Card", ColumnKind.Money, 95), new ReportColumn("Bank transfer", ColumnKind.Money, 110), new ReportColumn("Total", ColumnKind.Money, 110)
            },
            Rows = table,
            Total = totalRow.ToArray()
        };
    }

    /// <summary>D2: amounts billed (BillItem), by item type — paid or not; voided bills are left out (rules 5 and 6).</summary>
    public async Task<ReportResult> IncomeSummaryAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT b.BillDate, b.BillID, i.ItemType, i.Subtotal
FROM BillItem i JOIN Bill b ON b.BillID = i.BillID
WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND i.IsDeleted = 0 AND b.Status <> 'Void' AND b.BillDate >= @From AND b.BillDate < @ToEnd;", Params(clinicId, f),
            r => (Date: r.GetDateTime(0), Bill: r.GetInt32(1), Type: Text(r, 2) ?? BillItemTypes.Service, Amount: r.GetDecimal(3)));
        var types = new[] { BillItemTypes.Consultation, BillItemTypes.Medicine, BillItemTypes.Vaccine, BillItemTypes.Service };
        var buckets = Buckets(f.From, f.To);
        decimal total = rows.Sum(x => x.Amount);
        int bills = rows.Select(x => x.Bill).Distinct().Count();
        var byType = types.Select(t => (Type: t, Amount: rows.Where(x => x.Type == t).Sum(x => x.Amount))).ToList();
        var table = buckets.Select(b =>
        {
            var set = rows.Where(x => BucketOf(x.Date, f.From, f.To) == b.Start).ToList();
            var row = new List<object> { b.Start, set.Select(x => x.Bill).Distinct().Count() };
            row.AddRange(types.Select(t => (object)set.Where(x => x.Type == t).Sum(x => x.Amount)));
            row.Add(set.Sum(x => x.Amount));
            return row.ToArray();
        }).Where(r => (int)r[1] > 0).OrderByDescending(r => (DateTime)r[0]).ToList();
        var top = byType.OrderByDescending(x => x.Amount).First();
        var totalRow = new List<object> { "TOTAL", bills };
        totalRow.AddRange(byType.Select(x => (object)x.Amount));
        totalRow.Add(total);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Total billed", Peso(total), "Paid or not; voided bills left out", "receipt", "Mint", "CompletedText"),
                new ReportCard("Bills", Num(bills), null, "file-pen", "Cyan"),
                new ReportCard("Average bill", Peso(bills == 0 ? 0 : Math.Round(total / bills, 2)), null, "trending-up", "PaleBlue"),
                new ReportCard("Top item type", total == 0 ? "—" : top.Type, total == 0 ? null : Peso(top.Amount), "pill", "WarningBox", "ConsultText")
            },
            Chart = new ReportChart("Billed per " + (Monthly(f) ? "month" : "day") + " by item type", ChartKind.StackedBar, buckets.Select(b => b.Label).ToList(),
                types.Select(t => new ReportSeries(t, buckets.Select(b => (double)rows.Where(x => x.Type == t && BucketOf(x.Date, f.From, f.To) == b.Start).Sum(x => x.Amount)).ToList())).ToList(), Money: true),
            SideChart = new ReportChart("By item type", ChartKind.Donut, byType.Select(x => x.Type).ToList(), new[] { new ReportSeries("Billed", byType.Select(x => (double)x.Amount).ToList()) }, Money: true),
            TableTitle = Monthly(f) ? "Monthly breakdown" : "Daily breakdown",
            Columns = new[]
            {
                new ReportColumn(Monthly(f) ? "Month" : "Date", ColumnKind.Date, 110), new ReportColumn("Bills", ColumnKind.Number, 70),
                new ReportColumn("Consultation", ColumnKind.Money, 120), new ReportColumn("Medicine", ColumnKind.Money, 110),
                new ReportColumn("Vaccine", ColumnKind.Money, 110), new ReportColumn("Service", ColumnKind.Money, 110), new ReportColumn("Total", ColumnKind.Money, 120)
            },
            Rows = table,
            Total = totalRow.ToArray()
        };
    }

    /// <summary>D3: Unpaid and Partial bills with their age (0–7, 8–30, 31+ days) — as of today.</summary>
    public async Task<ReportResult> OutstandingAsync(int clinicId, ReportFilters f, DateTime today)
    {
        var rows = await QueryAsync(@"
SELECT b.BillNo, b.BillID, o.FirstName, o.LastName, o.ContactNo, b.BillDate, b.TotalAmount,
       ISNULL((SELECT SUM(pm.AmountPaid) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0), 0), b.Status
FROM Bill b JOIN Owner o ON o.OwnerID = b.OwnerID
WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND b.Status IN ('Unpaid', 'Partial')
ORDER BY b.BillDate;", Params(clinicId, f), r =>
        {
            var date = r.GetDateTime(5);
            int age = (today.Date - date.Date).Days;
            decimal total = r.GetDecimal(6), paid = r.GetDecimal(7);
            return new object[] { Text(r, 0) ?? "Bill #" + r.GetInt32(1), PersonName.Join(r.GetString(2), r.GetString(3)), Validator.FormatPhMobile(r.GetString(4)), date, total, paid, total - paid, age };
        });
        string Group(int age) => age <= 7 ? "0–7 days" : age <= 30 ? "8–30 days" : "31+ days";
        var groups = new[] { "0–7 days", "8–30 days", "31+ days" };
        decimal outstanding = rows.Sum(r => (decimal)r[6]);
        return new ReportResult
        {
            Note = "As of " + today.ToString("MMM d, yyyy") + " — every unpaid and partially paid bill, whatever its date.",
            Cards = new[]
            {
                new ReportCard("Total outstanding", Peso(outstanding), null, "circle-dollar-sign", "OrangeSoft", "Orange"),
                new ReportCard("Bills", Num(rows.Count), null, "receipt", "Cyan"),
                new ReportCard("Oldest", rows.Count == 0 ? "—" : rows.Max(r => (int)r[7]) + " days", null, "hourglass", "WarningBox", "ConsultText"),
                new ReportCard("Average balance", Peso(rows.Count == 0 ? 0 : Math.Round(outstanding / rows.Count, 2)), null, "wallet", "PaleBlue")
            },
            Chart = new ReportChart("Balance by age", ChartKind.Bar, groups, new[] { new ReportSeries("Balance", groups.Select(g => (double)rows.Where(r => Group((int)r[7]) == g).Sum(r => (decimal)r[6])).ToList()) }, Money: true),
            SideChart = new ReportChart("Bills by age", ChartKind.Donut, groups, new[] { new ReportSeries("Bills", groups.Select(g => (double)rows.Count(r => Group((int)r[7]) == g)).ToList()) }),
            TableTitle = "Open bills",
            Columns = new[]
            {
                new ReportColumn("Bill no.", ColumnKind.Text, 115), new ReportColumn("Owner", ColumnKind.Text, 160), new ReportColumn("Contact no.", ColumnKind.Text, 125),
                new ReportColumn("Bill date", ColumnKind.Date, 110), new ReportColumn("Total", ColumnKind.Money, 100), new ReportColumn("Paid", ColumnKind.Money, 100),
                new ReportColumn("Balance", ColumnKind.Money, 100), new ReportColumn("Age (days)", ColumnKind.Number, 90)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, null, null, rows.Sum(r => (decimal)r[4]), rows.Sum(r => (decimal)r[5]), outstanding, null }
        };
    }

    /// <summary>D4: voided bills by date and cashier, with the reason (rule 5: only here, never in income).</summary>
    public async Task<ReportResult> VoidedBillsAsync(int clinicId, ReportFilters f)
    {
        var rows = await QueryAsync(@"
SELECT b.BillNo, b.BillID, b.BillDate, o.FirstName, o.LastName, p.PetName, b.TotalAmount, c.FirstName, c.LastName, b.Remarks
FROM Bill b JOIN Owner o ON o.OwnerID = b.OwnerID
JOIN Appointment a ON a.AppointmentID = b.AppointmentID LEFT JOIN Pet p ON p.PetID = a.PetID
LEFT JOIN [User] c ON c.UserID = b.CashierID
WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND b.Status = 'Void' AND b.BillDate >= @From AND b.BillDate < @ToEnd
  AND (@Cashier IS NULL OR b.CashierID = @Cashier)
ORDER BY b.BillDate DESC;", Params(clinicId, f),
            r => new object[] { Text(r, 0) ?? "Bill #" + r.GetInt32(1), r.GetDateTime(2), PersonName.Join(r.GetString(3), r.GetString(4)), Text(r, 5) ?? "—", r.GetDecimal(6), Person(r, 7, 8), Text(r, 9) ?? "—" });
        var buckets = Buckets(f.From, f.To);
        var byCashier = rows.GroupBy(r => (string)r[5]).OrderByDescending(g => g.Count()).ToList();
        decimal amount = rows.Sum(r => (decimal)r[4]);
        return new ReportResult
        {
            Cards = new[]
            {
                new ReportCard("Voided bills", Num(rows.Count), null, "x", "LightGray", "BodyText"),
                new ReportCard("Amount voided", Peso(amount), "Not counted as income", "receipt", "OrangeSoft", "Orange"),
                new ReportCard("Cashiers", Num(byCashier.Count), null, "users", "Cyan")
            },
            Chart = new ReportChart("Voided bills per " + (Monthly(f) ? "month" : "day"), ChartKind.Bar, buckets.Select(b => b.Label).ToList(),
                new[] { new ReportSeries("Bills", buckets.Select(b => (double)rows.Count(r => BucketOf((DateTime)r[1], f.From, f.To) == b.Start)).ToList()) }),
            SideChart = new ReportChart("By cashier", ChartKind.Donut, byCashier.Select(g => g.Key).ToList(), new[] { new ReportSeries("Bills", byCashier.Select(g => (double)g.Count()).ToList()) }),
            TableTitle = "Voided bills",
            Columns = new[]
            {
                new ReportColumn("Bill no.", ColumnKind.Text, 115), new ReportColumn("Bill date", ColumnKind.Date, 110), new ReportColumn("Owner", ColumnKind.Text, 160),
                new ReportColumn("Pet", ColumnKind.Text, 110), new ReportColumn("Amount", ColumnKind.Money, 105), new ReportColumn("Cashier", ColumnKind.Text, 150),
                new ReportColumn("Reason", ColumnKind.Text, 170)
            },
            Rows = rows,
            Total = new object[] { "TOTAL", null, null, null, amount, null, null }
        };
    }
}
