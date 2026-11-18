using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// Super Admin Dashboard queries (Tasks/VetCare_Super_Admin_Dashboard.md): counts and statuses across every clinic.
/// Deleted clinics and records are skipped, users of Rejected clinics aren't counted, and nothing returns
/// owners, pets, records or money (rules 2 and 3).
/// </summary>
public sealed partial class DashboardRepository
{
    private sealed record DeviceRow(int DeviceId, int ClinicId, string ClinicName, string ClinicStatus, string DeviceName, bool IsActive,
        DateTime? LastSyncAt, int Recent, int RecentFailed, string LastStatus, string LastError);

    public async Task<SuperAdminDashboardData> GetSuperAdminAsync(DateTime today, int staleHours = SyncHealthHelper.DefaultStaleHours,
        int failingCount = SyncHealthHelper.DefaultFailingCount)
    {
        var now = DateTime.Now;
        var data = new SuperAdminDashboardData { LoadedAt = now };
        await using var connection = await Database.OpenAsync();

        // Clinics (stat card, growth, recent sign-ups) -------------------------------------------------------------
        var clinics = new List<Clinic>();
        await using (var cmd = new SqlCommand(@"
SELECT ClinicID, ClinicName, Address, Status, DateCreated, StatusChangedAt FROM Clinic WHERE IsDeleted = 0;", connection))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                clinics.Add(new Clinic
                {
                    ClinicId = r.GetInt32(0),
                    ClinicName = r.GetString(1),
                    Address = r.IsDBNull(2) ? null : r.GetString(2),
                    Status = r.IsDBNull(3) ? null : r.GetString(3),
                    DateCreated = r.IsDBNull(4) ? null : r.GetDateTime(4),
                    StatusChangedAt = r.IsDBNull(5) ? null : r.GetDateTime(5)
                });
        }
        var counted = clinics.Where(c => c.Status != AccountStatus.Rejected).ToList();
        data.Clinics = counted.Count;
        data.ActiveClinics = counted.Count(c => c.Status == AccountStatus.Active);
        data.PendingClinics = counted.Count(c => c.Status == AccountStatus.Pending);
        data.InactiveClinics = counted.Count(c => c.Status == AccountStatus.Inactive);
        data.OldestPending = counted.Where(c => c.Status == AccountStatus.Pending).Min(c => c.DateCreated);
        data.RecentSignups = clinics.OrderByDescending(c => c.DateCreated ?? DateTime.MinValue).ThenByDescending(c => c.ClinicId).Take(5).ToList();

        var growth = new List<(string, int, int)>();
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        for (int i = 5; i >= 0; i--)
        {
            var start = thisMonth.AddMonths(-i);
            var end = start.AddMonths(1);
            growth.Add((start.ToString("MMM"),
                counted.Count(c => c.DateCreated >= start && c.DateCreated < end),
                counted.Count(c => c.Status == AccountStatus.Active && (c.DateCreated == null || c.DateCreated < end))));
        }
        data.Growth = growth;

        // Users by role (not of Rejected or deleted clinics) --------------------------------------------------------
        await using (var cmd = new SqlCommand(@"
SELECT u.RoleID, COUNT(*) FROM [User] u JOIN Clinic c ON c.ClinicID = u.ClinicID
WHERE u.IsDeleted = 0 AND c.IsDeleted = 0 AND c.Status <> 'Rejected' AND u.RoleID IN (2, 3, 4)
GROUP BY u.RoleID;", connection))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                int n = r.GetInt32(1);
                switch (r.GetInt32(0))
                {
                    case RoleIds.Administrator: data.Admins = n; break;
                    case RoleIds.Veterinarian: data.Vets = n; break;
                    case RoleIds.CashierStaff: data.Staff = n; break;
                }
            }
        }

        // Devices with their recent syncs (health rules) -------------------------------------------------------------
        var devices = new List<DeviceRow>();
        await using (var cmd = new SqlCommand(@"
SELECT d.DeviceID, d.ClinicID, c.ClinicName, c.Status, d.DeviceName, d.IsActive, d.LastSyncAt,
       ISNULL(f.Recent, 0), ISNULL(f.Failed, 0), l.Status, l.ErrorMessage
FROM Device d
JOIN Clinic c ON c.ClinicID = d.ClinicID
OUTER APPLY (SELECT COUNT(*) AS Recent, SUM(CASE WHEN x.Status = 'Failed' THEN 1 ELSE 0 END) AS Failed
             FROM (SELECT TOP (@N) s.Status FROM SyncLog s WHERE s.DeviceID = d.DeviceID ORDER BY s.StartedAt DESC) x) f
OUTER APPLY (SELECT TOP 1 s.Status, s.ErrorMessage FROM SyncLog s WHERE s.DeviceID = d.DeviceID ORDER BY s.StartedAt DESC) l
WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected';", connection))
        {
            cmd.Parameters.AddWithValue("@N", failingCount);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                devices.Add(new DeviceRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4),
                    r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetDateTime(6), r.GetInt32(7), r.GetInt32(8),
                    r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10)));
        }
        var staleBefore = now.AddHours(-staleHours);
        bool Failing(DeviceRow d) => d.IsActive && d.Recent >= failingCount && d.RecentFailed == d.Recent;
        bool Stale(DeviceRow d) => d.IsActive && !Failing(d) && (d.LastSyncAt == null || d.LastSyncAt < staleBefore);
        bool Healthy(DeviceRow d) => d.IsActive && !Failing(d) && !Stale(d) && d.LastStatus != "Failed";
        data.Devices = devices.Count;
        data.HealthyDevices = devices.Count(Healthy);

        // Syncs: today's failures and conflicts, last 7 days ---------------------------------------------------------
        await using (var cmd = new SqlCommand(@"
SELECT SUM(CASE WHEN s.Status = 'Failed' THEN 1 ELSE 0 END), SUM(s.ConflictsFound)
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND s.StartedAt >= @Today AND s.StartedAt < @Tomorrow;

SELECT DISTINCT d.ClinicID
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND s.Status = 'Failed' AND s.StartedAt >= @Today AND s.StartedAt < @Tomorrow;

SELECT CONVERT(date, s.StartedAt), SUM(CASE WHEN s.Status = 'Success' THEN 1 ELSE 0 END), SUM(CASE WHEN s.Status = 'Failed' THEN 1 ELSE 0 END)
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND s.StartedAt >= @WeekStart AND s.StartedAt < @Tomorrow
GROUP BY CONVERT(date, s.StartedAt);

SELECT AVG(CAST(DATEDIFF(millisecond, s.StartedAt, s.FinishedAt) AS float)) / 1000.0, SUM(CAST(s.RecordsSent + s.RecordsReceived AS bigint))
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND s.StartedAt >= @WeekStart AND s.StartedAt < @Tomorrow AND s.FinishedAt IS NOT NULL;", connection))
        {
            cmd.Parameters.AddWithValue("@Today", today);
            cmd.Parameters.AddWithValue("@Tomorrow", today.AddDays(1));
            cmd.Parameters.AddWithValue("@WeekStart", today.AddDays(-6));
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            data.FailedSyncsToday = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            data.ConflictsToday = r.IsDBNull(1) ? 0 : r.GetInt32(1);

            await r.NextResultAsync();
            var failedClinics = new List<int>();
            while (await r.ReadAsync()) failedClinics.Add(r.GetInt32(0));
            data.FailedSyncClinicId = failedClinics.Count == 1 ? failedClinics[0] : null;

            await r.NextResultAsync();
            var byDay = new Dictionary<DateTime, (int, int)>();
            while (await r.ReadAsync()) byDay[r.GetDateTime(0).Date] = (r.GetInt32(1), r.GetInt32(2));
            data.SyncDays = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6))
                .Select(d => (d.ToString("MMM d"), byDay.TryGetValue(d, out var v) ? v.Item1 : 0, byDay.TryGetValue(d, out var w) ? w.Item2 : 0)).ToList();

            await r.NextResultAsync();
            await r.ReadAsync();
            data.AvgSyncSeconds = r.IsDBNull(0) ? null : r.GetDouble(0);
            data.RecordsSynced = r.IsDBNull(1) ? 0 : r.GetInt64(1);
        }

        // Clinics with no active admin --------------------------------------------------------------------------------
        var noAdmin = new List<Clinic>();
        await using (var cmd = new SqlCommand(@"
SELECT c.ClinicID, c.ClinicName FROM Clinic c
WHERE c.IsDeleted = 0 AND c.Status = 'Active'
  AND NOT EXISTS (SELECT 1 FROM [User] u WHERE u.ClinicID = c.ClinicID AND u.RoleID = 2 AND u.Status = 'Active' AND u.IsDeleted = 0)
ORDER BY c.ClinicName;", connection))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) noAdmin.Add(new Clinic { ClinicId = r.GetInt32(0), ClinicName = r.GetString(1) });
        }

        // Attention needed (most urgent first) ----------------------------------------------------------------------------
        var attention = new List<AttentionItem>();
        var pending = counted.Where(c => c.Status == AccountStatus.Pending).OrderBy(c => c.DateCreated).ToList();
        if (pending.Count > 0)
        {
            string names = string.Join(", ", pending.Take(3).Select(c => c.ClinicName)) + (pending.Count > 3 ? " and " + (pending.Count - 3) + " more" : string.Empty);
            attention.Add(new AttentionItem(AttentionKind.PendingSignups,
                pending.Count == 1 ? "1 clinic sign-up waiting" : pending.Count + " clinic sign-ups waiting", names, null));
        }
        foreach (var c in noAdmin)
            attention.Add(new AttentionItem(AttentionKind.ClinicWithoutAdmin, c.ClinicName + " has no active admin",
                "Active clinic · add an administrator so it can be managed", c.ClinicId));
        foreach (var d in devices.Where(x => x.ClinicStatus == AccountStatus.Active && Failing(x)))
            attention.Add(new AttentionItem(AttentionKind.FailingDevice, d.DeviceName + " is failing",
                d.ClinicName + " · last " + failingCount + " syncs failed" + (string.IsNullOrEmpty(d.LastError) ? string.Empty : " (" + d.LastError + ")"), d.ClinicId, d.DeviceId));
        foreach (var d in devices.Where(x => x.ClinicStatus == AccountStatus.Active && Stale(x)))
            attention.Add(new AttentionItem(AttentionKind.StaleDevice,
                d.LastSyncAt == null ? d.DeviceName + " has never synced" : d.DeviceName + " hasn't synced in " + GreetingHelper.Relative(d.LastSyncAt.Value, now).Replace(" ago", string.Empty),
                d.ClinicName + (d.LastSyncAt == null ? string.Empty : " · last sync " + d.LastSyncAt.Value.ToString("MMM d, h:mm tt")), d.ClinicId, d.DeviceId));
        var silentBefore = now.AddDays(-SyncHealthHelper.ClinicSilentDays);
        foreach (var g in devices.Where(x => x.ClinicStatus == AccountStatus.Active && x.IsActive).GroupBy(x => (x.ClinicId, x.ClinicName)))
        {
            var last = g.Max(x => x.LastSyncAt);
            if (last == null || last < silentBefore)
                attention.Add(new AttentionItem(AttentionKind.ClinicNotSyncing, g.Key.ClinicName + " hasn't synced in " + SyncHealthHelper.ClinicSilentDays + "+ days",
                    (g.Count() == 1 ? "1 device" : g.Count() + " devices") + (last == null ? " · never synced" : " · last sync " + last.Value.ToString("MMM d")), g.Key.ClinicId));
        }
        foreach (var c in counted.Where(c => c.Status == AccountStatus.Inactive))
        {
            int blocked = devices.Count(d => d.ClinicId == c.ClinicId && d.IsActive);
            if (blocked == 0) continue;
            attention.Add(new AttentionItem(AttentionKind.InactiveClinic, c.ClinicName + " is inactive",
                (blocked == 1 ? "1 device blocked" : blocked + " devices blocked") + (c.StatusChangedAt == null ? string.Empty : " · since " + c.StatusChangedAt.Value.ToString("MMM d")), c.ClinicId));
        }
        data.Attention = attention;

        // Clinic sync health ------------------------------------------------------------------------------------------
        data.SyncHealth = devices.GroupBy(d => (d.ClinicId, d.ClinicName, d.ClinicStatus))
            .Select(g => new ClinicSyncHealth
            {
                ClinicId = g.Key.ClinicId,
                ClinicName = g.Key.ClinicName,
                ClinicStatus = g.Key.ClinicStatus,
                Devices = g.Count(),
                Healthy = g.Count(Healthy),
                Stale = g.Count(Stale),
                Failing = g.Count(Failing),
                LastSync = g.Max(x => x.LastSyncAt),
                Overall = SyncHealthHelper.Overall(g.Key.ClinicStatus, g.Count(Failing), g.Count(Stale))
            })
            .OrderBy(h => SyncHealthHelper.Rank(h.Overall)).ThenBy(h => h.ClinicName).Take(5).ToList();

        // Platform usage this month vs last month (counts only) ----------------------------------------------------------
        await using (var cmd = new SqlCommand(@"
SELECT
 (SELECT COUNT(*) FROM Appointment a JOIN Clinic c ON c.ClinicID = a.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND a.IsDeleted = 0 AND a.CreatedAt >= @M0 AND a.CreatedAt < @M1),
 (SELECT COUNT(*) FROM Appointment a JOIN Clinic c ON c.ClinicID = a.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND a.IsDeleted = 0 AND a.CreatedAt >= @P0 AND a.CreatedAt < @M0),
 (SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID JOIN Clinic c ON c.ClinicID = o.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND p.IsDeleted = 0 AND p.CreatedAt >= @M0 AND p.CreatedAt < @M1),
 (SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID JOIN Clinic c ON c.ClinicID = o.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND p.IsDeleted = 0 AND p.CreatedAt >= @P0 AND p.CreatedAt < @M0),
 (SELECT COUNT(*) FROM MedicalRecord m JOIN Pet p ON p.PetID = m.PetID JOIN Owner o ON o.OwnerID = p.OwnerID JOIN Clinic c ON c.ClinicID = o.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND m.IsDeleted = 0 AND m.RecordDate >= @M0 AND m.RecordDate < @M1),
 (SELECT COUNT(*) FROM MedicalRecord m JOIN Pet p ON p.PetID = m.PetID JOIN Owner o ON o.OwnerID = p.OwnerID JOIN Clinic c ON c.ClinicID = o.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND m.IsDeleted = 0 AND m.RecordDate >= @P0 AND m.RecordDate < @M0),
 (SELECT COUNT(*) FROM Bill b JOIN Clinic c ON c.ClinicID = b.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND b.IsDeleted = 0 AND b.Status <> 'Void' AND b.BillDate >= @M0 AND b.BillDate < @M1),
 (SELECT COUNT(*) FROM Bill b JOIN Clinic c ON c.ClinicID = b.ClinicID WHERE c.IsDeleted = 0 AND c.Status <> 'Rejected' AND b.IsDeleted = 0 AND b.Status <> 'Void' AND b.BillDate >= @P0 AND b.BillDate < @M0);", connection))
        {
            cmd.Parameters.AddWithValue("@M0", thisMonth);
            cmd.Parameters.AddWithValue("@M1", thisMonth.AddMonths(1));
            cmd.Parameters.AddWithValue("@P0", thisMonth.AddMonths(-1));
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            data.Usage = new PlatformUsage
            {
                Appointments = r.GetInt32(0), AppointmentsLast = r.GetInt32(1),
                Pets = r.GetInt32(2), PetsLast = r.GetInt32(3),
                Records = r.GetInt32(4), RecordsLast = r.GetInt32(5),
                Bills = r.GetInt32(6), BillsLast = r.GetInt32(7)
            };
        }

        return data;
    }
}
