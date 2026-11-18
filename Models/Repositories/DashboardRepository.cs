using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

// Read-only shapes for the three dashboards (Tasks/VetCare_Dashboard_Module.md). Nothing here is ever saved.

public sealed record DayIncome(DateTime Day, decimal Amount);

public sealed record DayVisits(DateTime Day, int Completed, int Cancelled, int NoShow);

public sealed record VetVisits(string VetName, int Visits);

/// <summary>A medical record line for "My recent records".</summary>
public sealed record RecentRecord(int RecordId, DateTime Date, string PetName, string Species, string Diagnosis);

/// <summary>A visit the vet hasn't finished (In Consultation), with when its draft was last saved.</summary>
public sealed record UnfinishedVisit(Appointment Appointment, DateTime? DraftSavedAt);

/// <summary>A today's-queue row with its bill (if any), so the staff dashboard can pick the next action.</summary>
public sealed record QueueItem(Appointment Appointment, int? BillId, string BillStatus, decimal Balance);

/// <summary>A "Ready to bill / Unpaid" row: a completed visit without a bill, or an open bill with a balance.</summary>
public sealed record BillingTask(int? AppointmentId, int? BillId, string OwnerName, string PetName, decimal Balance, DateTime When)
{
    public bool NeedsBill => BillId == null;
}

public sealed class AdminDashboardData
{
    public int AppointmentsToday { get; set; }
    public int CompletedToday { get; set; }
    public int NoShowToday { get; set; }
    public decimal CollectedToday { get; set; }
    public decimal CollectedYesterday { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public decimal Outstanding { get; set; }
    public int OutstandingBills { get; set; }
    public StockSummary Stock { get; set; } = new();
    public int ExpiredCount { get; set; }
    public IReadOnlyList<DayIncome> Income30 { get; set; } = Array.Empty<DayIncome>();
    public IReadOnlyList<DayVisits> Week { get; set; } = Array.Empty<DayVisits>();
    public IReadOnlyList<VetVisits> VetsToday { get; set; } = Array.Empty<VetVisits>();
    public IReadOnlyList<Medicine> StockAlerts { get; set; } = Array.Empty<Medicine>();
    public IReadOnlyList<Payment> RecentPayments { get; set; } = Array.Empty<Payment>();
    public int OverdueVaccinations { get; set; }
}

/// <summary>The vet's own day. No money fields at all (dashboard rule 5).</summary>
public sealed class VetDashboardData
{
    public int MyToday { get; set; }
    public int Waiting { get; set; }
    public int CompletedToday { get; set; }
    public int Unfinished { get; set; }

    /// <summary>The first Checked In pet, or (when nobody is waiting) the next Scheduled visit; null when neither.</summary>
    public Appointment NextPatient { get; set; }

    public bool NextIsWaiting { get; set; }

    /// <summary>"Nov 14 — Healthy; 5-in-1 Dose 2 given", or null for a first visit.</summary>
    public string LastVisit { get; set; }

    public IReadOnlyList<Appointment> Schedule { get; set; } = Array.Empty<Appointment>();
    public IReadOnlyList<UnfinishedVisit> UnfinishedVisits { get; set; } = Array.Empty<UnfinishedVisit>();
    public IReadOnlyList<RecentRecord> RecentRecords { get; set; } = Array.Empty<RecentRecord>();
    public IReadOnlyList<Medicine> Vaccines { get; set; } = Array.Empty<Medicine>();
}

public sealed class StaffDashboardData
{
    public int AppointmentsToday { get; set; }
    public int ToCheckIn { get; set; }
    public int GuestsToRegister { get; set; }
    public int ToDispense { get; set; }
    public int ReadyToBill { get; set; }
    public decimal CollectedToday { get; set; }
    public IReadOnlyList<QueueItem> Queue { get; set; } = Array.Empty<QueueItem>();
    public IReadOnlyList<PharmacyPrescription> Dispense { get; set; } = Array.Empty<PharmacyPrescription>();
    public IReadOnlyList<BillingTask> BillingTasks { get; set; } = Array.Empty<BillingTask>();
    public IReadOnlyList<RecordVaccination> FollowUps { get; set; } = Array.Empty<RecordVaccination>();
    public IReadOnlyList<Medicine> StockAlerts { get; set; } = Array.Empty<Medicine>();
}

/// <summary>Sync pill: the database answers or not, and how many rows wait to sync (SyncStatus = Pending).</summary>
public sealed record SyncState(bool Online, int Pending);

/// <summary>
/// Dashboard queries (read-only). Every query is filtered by ClinicID (and by VetID for the vet) and skips
/// archived rows. Lists that other modules already build come from their repositories.
/// </summary>
public sealed partial class DashboardRepository
{
    private readonly AppointmentRepository _appointments = new();
    private readonly MedicineRepository _medicines = new();
    private readonly BillingRepository _billing = new();
    private readonly PrescriptionRepository _prescriptions = new();
    private readonly VaccinationRepository _vaccinations = new();

    public const int ListSize = 5;

    // Administrator --------------------------------------------------------------------------------------------

    public async Task<AdminDashboardData> GetAdminAsync(int clinicId, DateTime today)
    {
        var data = new AdminDashboardData();
        var monday = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        const string sql = @"
SELECT COUNT(CASE WHEN Status <> 'Cancelled' THEN 1 END), COUNT(CASE WHEN Status = 'Completed' THEN 1 END), COUNT(CASE WHEN Status = 'No-show' THEN 1 END)
FROM Appointment WHERE ClinicID = @C AND IsDeleted = 0 AND AppointmentDate = @Today;

SELECT ISNULL(SUM(CASE WHEN pm.PaymentDate >= @Today AND pm.PaymentDate < @Tomorrow THEN pm.AmountPaid END), 0),
       ISNULL(SUM(CASE WHEN pm.PaymentDate >= @Yesterday AND pm.PaymentDate < @Today THEN pm.AmountPaid END), 0),
       ISNULL(SUM(CASE WHEN pm.PaymentDate >= @MonthStart AND pm.PaymentDate < @Tomorrow THEN pm.AmountPaid END), 0)
FROM Payment pm JOIN Bill b ON b.BillID = pm.BillID
WHERE b.ClinicID = @C AND pm.IsDeleted = 0 AND b.IsDeleted = 0;

SELECT COUNT(*), ISNULL(SUM(x.Balance), 0) FROM (
    SELECT b.TotalAmount - ISNULL((SELECT SUM(pm.AmountPaid) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0), 0) AS Balance
    FROM Bill b WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND b.Status IN ('Unpaid', 'Partial')) x;

SELECT CAST(pm.PaymentDate AS date), SUM(pm.AmountPaid)
FROM Payment pm JOIN Bill b ON b.BillID = pm.BillID
WHERE b.ClinicID = @C AND pm.IsDeleted = 0 AND b.IsDeleted = 0 AND pm.PaymentDate >= @From30 AND pm.PaymentDate < @Tomorrow
GROUP BY CAST(pm.PaymentDate AS date);

SELECT AppointmentDate, COUNT(CASE WHEN Status = 'Completed' THEN 1 END), COUNT(CASE WHEN Status = 'Cancelled' THEN 1 END), COUNT(CASE WHEN Status = 'No-show' THEN 1 END)
FROM Appointment WHERE ClinicID = @C AND IsDeleted = 0 AND AppointmentDate >= @Monday AND AppointmentDate < @NextMonday
GROUP BY AppointmentDate;

SELECT u.FirstName, u.LastName, COUNT(a.AppointmentID)
FROM [User] u
LEFT JOIN Appointment a ON a.VetID = u.UserID AND a.ClinicID = @C AND a.IsDeleted = 0 AND a.AppointmentDate = @Today AND a.Status <> 'Cancelled'
WHERE u.ClinicID = @C AND u.RoleID = 3 AND u.IsDeleted = 0 AND u.Status = 'Active'
GROUP BY u.UserID, u.FirstName, u.LastName
ORDER BY COUNT(a.AppointmentID) DESC, u.LastName;

SELECT COUNT(*) FROM Medicine WHERE ClinicID = @C AND IsDeleted = 0 AND ExpiryDate < @Today;";

        await using (var connection = await Database.OpenAsync())
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@C", clinicId);
            AddDate(command, "@Today", today.Date);
            AddDate(command, "@Tomorrow", today.Date.AddDays(1));
            AddDate(command, "@Yesterday", today.Date.AddDays(-1));
            AddDate(command, "@MonthStart", new DateTime(today.Year, today.Month, 1));
            AddDate(command, "@From30", today.Date.AddDays(-29));
            AddDate(command, "@Monday", monday);
            AddDate(command, "@NextMonday", monday.AddDays(7));
            await using var r = await command.ExecuteReaderAsync();

            await r.ReadAsync();
            data.AppointmentsToday = r.GetInt32(0); data.CompletedToday = r.GetInt32(1); data.NoShowToday = r.GetInt32(2);
            await r.NextResultAsync(); await r.ReadAsync();
            data.CollectedToday = r.GetDecimal(0); data.CollectedYesterday = r.GetDecimal(1); data.CollectedThisMonth = r.GetDecimal(2);
            await r.NextResultAsync(); await r.ReadAsync();
            data.OutstandingBills = r.GetInt32(0); data.Outstanding = r.GetDecimal(1);

            await r.NextResultAsync();
            var income = new Dictionary<DateTime, decimal>();
            while (await r.ReadAsync()) income[r.GetDateTime(0).Date] = r.GetDecimal(1);
            data.Income30 = Enumerable.Range(0, 30).Select(i => today.Date.AddDays(i - 29))
                .Select(d => new DayIncome(d, income.TryGetValue(d, out var v) ? v : 0)).ToList();

            await r.NextResultAsync();
            var week = new Dictionary<DateTime, DayVisits>();
            while (await r.ReadAsync()) week[r.GetDateTime(0).Date] = new DayVisits(r.GetDateTime(0).Date, r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
            data.Week = Enumerable.Range(0, 7).Select(i => monday.AddDays(i))
                .Select(d => week.TryGetValue(d, out var v) ? v : new DayVisits(d, 0, 0, 0)).ToList();

            await r.NextResultAsync();
            var vets = new List<VetVisits>();
            while (await r.ReadAsync()) vets.Add(new VetVisits("Dr. " + PersonName.Join(r.GetString(0), r.GetString(1)), r.GetInt32(2)));
            data.VetsToday = vets;

            await r.NextResultAsync(); await r.ReadAsync();
            data.ExpiredCount = r.GetInt32(0);
        }

        data.Stock = await _medicines.GetSummaryAsync(clinicId, today);
        data.StockAlerts = await GetStockAlertsAsync(clinicId, today, vaccinesOnly: false);
        data.RecentPayments = (await _billing.GetPaymentsAsync(new PaymentQuery { ClinicId = clinicId, Page = 1, PageSize = ListSize })).Page.Items;
        data.OverdueVaccinations = (await _vaccinations.GetSummaryAsync(clinicId, today)).Overdue;
        return data;
    }

    /// <summary>Low, out, expiring and expired medicines (worst first), at most 5.</summary>
    public async Task<IReadOnlyList<Medicine>> GetStockAlertsAsync(int clinicId, DateTime today, bool vaccinesOnly)
    {
        var all = vaccinesOnly
            ? (await _medicines.GetActiveAsync(clinicId)).Where(m => m.IsVaccine)
            : await _medicines.GetActiveAsync(clinicId);
        int Rank(StockStatus s) => s switch
        {
            StockStatus.OutOfStock => 0,
            StockStatus.Expired => 1,
            StockStatus.LowStock => 2,
            StockStatus.ExpiringSoon => 3,
            _ => 4
        };
        return all.Select(m => (m, s: StockStatusHelper.StatusOf(m, today)))
            .Where(x => vaccinesOnly || x.s != StockStatus.InStock)   // the vet sees every vaccine; alerts skip "In stock"
            .OrderBy(x => Rank(x.s)).ThenBy(x => x.m.MedicineName)
            .Select(x => x.m).Take(ListSize).ToList();
    }

    // Veterinarian ------------------------------------------------------------------------------------------------

    public async Task<VetDashboardData> GetVetAsync(int clinicId, int vetId, DateTime today)
    {
        var data = new VetDashboardData();
        var todays = await _appointments.GetRangeAsync(new AppointmentQuery { ClinicId = clinicId, VetId = vetId, From = today.Date, To = today.Date });
        data.Schedule = todays;
        data.MyToday = todays.Count(a => a.Status != AppointmentStatuses.Cancelled);
        data.Waiting = todays.Count(a => a.Status == AppointmentStatuses.CheckedIn);
        data.CompletedToday = todays.Count(a => a.Status == AppointmentStatuses.Completed);

        var waiting = todays.Where(a => a.Status == AppointmentStatuses.CheckedIn).OrderBy(a => a.AppointmentTime).FirstOrDefault();
        var nextScheduled = todays.Where(a => a.Status == AppointmentStatuses.Scheduled && a.StartsAt >= DateTime.Now.AddMinutes(-30))
            .OrderBy(a => a.AppointmentTime).FirstOrDefault();
        data.NextPatient = waiting ?? nextScheduled;
        data.NextIsWaiting = waiting != null;

        await using var connection = await Database.OpenAsync();

        // Unfinished: every visit of mine still In Consultation (any day), with its draft's last save.
        var unfinished = (await _appointments.GetRangeAsync(new AppointmentQuery { ClinicId = clinicId, VetId = vetId, Status = AppointmentStatuses.InConsultation }))
            .OrderBy(a => a.StartsAt).ToList();
        var drafts = new List<UnfinishedVisit>();
        foreach (var a in unfinished.Take(ListSize))
        {
            await using var c = new SqlCommand("SELECT MAX(UpdatedAt) FROM MedicalRecord WHERE AppointmentID = @A AND IsDeleted = 0;", connection);
            c.Parameters.AddWithValue("@A", a.AppointmentId);
            drafts.Add(new UnfinishedVisit(a, await c.ExecuteScalarAsync() as DateTime?));
        }
        data.Unfinished = unfinished.Count;
        data.UnfinishedVisits = drafts;

        // The next patient's last visit (another appointment): date, diagnosis and vaccines given.
        if (data.NextPatient?.PetId is int petId)
        {
            await using var c = new SqlCommand(@"
SELECT TOP 1 r.RecordID, r.RecordDate, r.Diagnosis FROM MedicalRecord r
WHERE r.PetID = @P AND r.IsDeleted = 0 AND r.AppointmentID <> @A ORDER BY r.RecordDate DESC, r.RecordID DESC;", connection);
            c.Parameters.AddWithValue("@P", petId);
            c.Parameters.AddWithValue("@A", data.NextPatient.AppointmentId);
            int? recordId = null;
            await using (var r = await c.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    recordId = r.GetInt32(0);
                    data.LastVisit = r.GetDateTime(1).ToString("MMM d") + " — " + OneLine(r.GetString(2), 60);
                }
            }
            if (recordId != null)
            {
                await using var v = new SqlCommand(@"
SELECT m.MedicineName, x.DoseNo FROM Vaccination x JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE x.RecordID = @R AND x.IsDeleted = 0;", connection);
                v.Parameters.AddWithValue("@R", recordId.Value);
                var given = new List<string>();
                await using var r = await v.ExecuteReaderAsync();
                while (await r.ReadAsync()) given.Add(r.GetString(0) + " Dose " + r.GetInt32(1));
                if (given.Count > 0) data.LastVisit += "; " + string.Join(", ", given) + " given";
            }
        }

        // My recent records.
        await using (var c = new SqlCommand(@"
SELECT TOP 5 r.RecordID, r.RecordDate, p.PetName, p.Species, r.Diagnosis
FROM MedicalRecord r JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @C
JOIN Pet p ON p.PetID = r.PetID
WHERE r.VetID = @V AND r.IsDeleted = 0
ORDER BY r.RecordDate DESC, r.RecordID DESC;", connection))
        {
            c.Parameters.AddWithValue("@C", clinicId);
            c.Parameters.AddWithValue("@V", vetId);
            var list = new List<RecentRecord>();
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new RecentRecord(r.GetInt32(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), OneLine(r.GetString(4), 80)));
            data.RecentRecords = list;
        }

        // Vaccine stock check: every vaccine, worst first (low and out first).
        data.Vaccines = await GetStockAlertsAsync(clinicId, today, vaccinesOnly: true);
        return data;
    }

    // Cashier/Staff ----------------------------------------------------------------------------------------------------

    public async Task<StaffDashboardData> GetStaffAsync(int clinicId, DateTime today)
    {
        var data = new StaffDashboardData();
        var todays = await _appointments.GetRangeAsync(new AppointmentQuery { ClinicId = clinicId, From = today.Date, To = today.Date });
        data.AppointmentsToday = todays.Count(a => a.Status != AppointmentStatuses.Cancelled);
        var toCheckIn = todays.Where(a => a.Status == AppointmentStatuses.Scheduled).ToList();
        data.ToCheckIn = toCheckIn.Count;
        data.GuestsToRegister = toCheckIn.Count(a => a.IsGuest);

        // Bills of today's visits (for Create bill / Receive payment / Paid).
        var bills = new Dictionary<int, (int BillId, string Status, decimal Balance)>();
        await using (var connection = await Database.OpenAsync())
        await using (var c = new SqlCommand(@"
SELECT b.AppointmentID, b.BillID, b.Status,
       b.TotalAmount - ISNULL((SELECT SUM(pm.AmountPaid) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0), 0)
FROM Bill b JOIN Appointment a ON a.AppointmentID = b.AppointmentID
WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND b.Status <> 'Void' AND a.AppointmentDate = @Today;", connection))
        {
            c.Parameters.AddWithValue("@C", clinicId);
            AddDate(c, "@Today", today.Date);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) bills[r.GetInt32(0)] = (r.GetInt32(1), r.GetString(2), r.GetDecimal(3));
        }
        data.Queue = todays.Where(a => a.Status != AppointmentStatuses.Cancelled)
            .OrderBy(a => a.AppointmentTime)
            .Select(a => bills.TryGetValue(a.AppointmentId, out var b) ? new QueueItem(a, b.BillId, b.Status, b.Balance) : new QueueItem(a, null, null, 0))
            .ToList();

        var dispense = await _prescriptions.GetToDispenseAsync(clinicId, null, null, null, null);
        data.ToDispense = dispense.Count;
        data.Dispense = dispense.Take(ListSize).ToList();

        var summary = await _billing.GetSummaryAsync(clinicId, today);
        data.ReadyToBill = summary.ReadyToBill;
        data.CollectedToday = summary.CollectedToday;

        // Ready to bill first (oldest visit first), then open bills with a balance (oldest first).
        var tasks = (await _billing.GetReadyToBillAsync(clinicId, null))
            .Select(v => new BillingTask(v.AppointmentId, null, v.OwnerName, v.PetName, 0, v.VisitDate + v.VisitTime)).ToList();
        foreach (string status in new[] { BillStatuses.Partial, BillStatuses.Unpaid })
        {
            var open = await _billing.SearchBillsAsync(new BillQuery { ClinicId = clinicId, Status = status, Page = 1, PageSize = ListSize });
            tasks.AddRange(open.Items.Select(b => new BillingTask(b.AppointmentId, b.BillId, b.OwnerName, b.PetName, b.Balance, b.BillDate)));
        }
        data.BillingTasks = tasks.Take(ListSize).ToList();

        data.FollowUps = (await _vaccinations.GetDueListAsync(clinicId, DueFilter.All, null, null, today)).Take(ListSize).ToList();
        data.StockAlerts = (await GetStockAlertsAsync(clinicId, today, vaccinesOnly: false))
            .Where(m => StockStatusHelper.StatusOf(m, today) is StockStatus.LowStock or StockStatus.OutOfStock).ToList();
        return data;
    }

    // Sync ---------------------------------------------------------------------------------------------------------------

    /// <summary>Online = the database answers. Pending = rows of the clinic still marked Pending (no sync engine yet).</summary>
    public async Task<SyncState> GetSyncStateAsync(int clinicId)
    {
        const string sql = @"
SELECT (SELECT COUNT(*) FROM Owner WHERE ClinicID = @C AND SyncStatus = 'Pending')
     + (SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID WHERE o.ClinicID = @C AND p.SyncStatus = 'Pending')
     + (SELECT COUNT(*) FROM Appointment WHERE ClinicID = @C AND SyncStatus = 'Pending')
     + (SELECT COUNT(*) FROM Medicine WHERE ClinicID = @C AND SyncStatus = 'Pending')
     + (SELECT COUNT(*) FROM InventoryTransaction WHERE ClinicID = @C AND SyncStatus = 'Pending')
     + (SELECT COUNT(*) FROM Bill WHERE ClinicID = @C AND SyncStatus = 'Pending');";
        try
        {
            await using var connection = await Database.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@C", clinicId);
            return new SyncState(true, (int)await command.ExecuteScalarAsync());
        }
        catch (SqlException)
        {
            return new SyncState(false, 0);
        }
    }

    // Helpers --------------------------------------------------------------------------------------------------------------

    private static void AddDate(SqlCommand command, string name, DateTime value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.DateTime) { Value = value });

    private static string OneLine(string text, int max)
    {
        string line = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length <= max ? line : line.Substring(0, max - 1).TrimEnd() + "…";
    }
}
