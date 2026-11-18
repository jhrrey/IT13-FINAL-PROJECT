using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// SQL for the <c>Device</c>, <c>SyncLog</c> and <c>SyncLogDaily</c> tables (Devices & Sync Monitoring).
/// Only device and sync information: never the records inside the synced data (privacy rule).
/// </summary>
public sealed class DeviceRepository
{
    private const string DeviceColumns = @"
d.DeviceID, d.ClinicID, d.DeviceName, d.MachineCode, d.LastSyncAt, d.IsActive, d.RegisteredAt,
d.PendingChanges, d.LastSyncAttemptAt, d.DeactivatedReason, d.AppVersion, c.ClinicName, c.Status,
ISNULL(f.Recent, 0), ISNULL(f.Failed, 0)
FROM Device d
JOIN Clinic c ON c.ClinicID = d.ClinicID
OUTER APPLY (SELECT COUNT(*) AS Recent, SUM(CASE WHEN x.Status = 'Failed' THEN 1 ELSE 0 END) AS Failed
             FROM (SELECT TOP (@N) s.Status FROM SyncLog s WHERE s.DeviceID = d.DeviceID ORDER BY s.StartedAt DESC, s.SyncLogID DESC) x) f";

    // Devices --------------------------------------------------------------------------------------------------

    /// <summary>A clinic's computers (Clinic details → Devices).</summary>
    public async Task<IReadOnlyList<ClinicDevice>> GetByClinicAsync(int clinicId)
    {
        var list = await GetDevicesAsync(clinicId, null);
        return list.OrderByDescending(d => d.IsActive).ThenBy(d => d.DeviceName).ToList();
    }

    /// <summary>Every device (or one clinic's) not of a deleted clinic, with its health; search on name or machine code.</summary>
    public async Task<IReadOnlyList<ClinicDevice>> GetDevicesAsync(int? clinicId, string search)
    {
        string sql = "SELECT " + DeviceColumns + @"
WHERE c.IsDeleted = 0 AND (@C IS NULL OR d.ClinicID = @C)
  AND (@Search IS NULL OR d.DeviceName LIKE @Search OR d.MachineCode LIKE @Search)
ORDER BY c.ClinicName, d.DeviceName;";
        return await ReadDevicesAsync(sql, p =>
        {
            p.AddWithValue("@C", (object)clinicId ?? DBNull.Value);
            p.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : "%" + Escape(search.Trim()) + "%");
        });
    }

    public async Task<ClinicDevice> GetByIdAsync(int deviceId) =>
        (await ReadDevicesAsync("SELECT " + DeviceColumns + " WHERE d.DeviceID = @ID;", p => p.AddWithValue("@ID", deviceId))).FirstOrDefault();

    /// <summary>This computer for this clinic, or null if it isn't registered yet.</summary>
    public async Task<ClinicDevice> FindAsync(int clinicId, string machineCode) =>
        (await ReadDevicesAsync("SELECT " + DeviceColumns + " WHERE d.ClinicID = @C AND d.MachineCode = @M;", p =>
        {
            p.AddWithValue("@C", clinicId);
            p.AddWithValue("@M", machineCode);
        })).FirstOrDefault();

    public Task<int> CountActiveAsync(int clinicId) =>
        ScalarAsync("SELECT COUNT(*) FROM Device WHERE ClinicID = @C AND IsActive = 1;", p => p.AddWithValue("@C", clinicId));

    /// <summary>Registers a computer for a clinic (its Administrator signed in on it for the first time).</summary>
    public Task<int> RegisterAsync(int clinicId, string name, string machineCode, string appVersion) =>
        ScalarAsync(@"
INSERT INTO Device (ClinicID, DeviceName, MachineCode, IsActive, RegisteredAt, AppVersion)
VALUES (@C, @Name, @M, 1, GETDATE(), @V);
SELECT CAST(SCOPE_IDENTITY() AS int);", p =>
        {
            p.AddWithValue("@C", clinicId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@M", machineCode);
            p.AddWithValue("@V", (object)appVersion ?? DBNull.Value);
        });

    /// <summary>
    /// Saves one sync in the log and updates the device: the last attempt, pending changes and app version always;
    /// LastSyncAt only when it succeeded.
    /// </summary>
    public async Task RecordSyncAsync(int deviceId, int? userId, string direction, DateTime startedAt, DateTime? finishedAt,
        int sent, int received, int conflicts, string status, string error, int pending, string appVersion)
    {
        const string sql = @"
INSERT INTO SyncLog (DeviceID, UserID, Direction, StartedAt, FinishedAt, RecordsSent, RecordsReceived, ConflictsFound, Status, ErrorMessage)
VALUES (@D, @U, @Dir, @Start, @End, @Sent, @Recv, @Conf, @Status, @Error);
UPDATE Device SET LastSyncAttemptAt = @Start, PendingChanges = @Pending, AppVersion = ISNULL(@V, AppVersion),
       LastSyncAt = CASE WHEN @Status = 'Success' THEN ISNULL(@End, @Start) ELSE LastSyncAt END
WHERE DeviceID = @D;";
        await ExecuteAsync(sql, p =>
        {
            p.AddWithValue("@D", deviceId);
            p.AddWithValue("@U", (object)userId ?? DBNull.Value);
            p.AddWithValue("@Dir", direction);
            p.AddWithValue("@Start", startedAt);
            p.AddWithValue("@End", (object)finishedAt ?? DBNull.Value);
            p.AddWithValue("@Sent", sent);
            p.AddWithValue("@Recv", received);
            p.AddWithValue("@Conf", conflicts);
            p.AddWithValue("@Status", status);
            p.AddWithValue("@Error", (object)error ?? DBNull.Value);
            p.AddWithValue("@Pending", pending);
            p.AddWithValue("@V", (object)appVersion ?? DBNull.Value);
        });
    }

    /// <summary>Turns a computer on or off from Clinic details (no reason given).</summary>
    public Task SetActiveAsync(int clinicId, int deviceId, bool isActive) =>
        ExecuteAsync("UPDATE Device SET IsActive = @Active, DeactivatedReason = CASE WHEN @Active = 1 THEN NULL ELSE DeactivatedReason END WHERE DeviceID = @ID AND ClinicID = @C;", p =>
        {
            p.AddWithValue("@Active", isActive);
            p.AddWithValue("@ID", deviceId);
            p.AddWithValue("@C", clinicId);
        });

    public Task DeactivateAsync(int deviceId, string reason) =>
        ExecuteAsync("UPDATE Device SET IsActive = 0, DeactivatedReason = @R WHERE DeviceID = @ID;", p =>
        {
            p.AddWithValue("@ID", deviceId);
            p.AddWithValue("@R", (object)reason ?? DBNull.Value);
        });

    public Task ReactivateAsync(int deviceId) =>
        ExecuteAsync("UPDATE Device SET IsActive = 1, DeactivatedReason = NULL WHERE DeviceID = @ID;", p => p.AddWithValue("@ID", deviceId));

    public Task RenameAsync(int deviceId, string name) =>
        ExecuteAsync("UPDATE Device SET DeviceName = @Name WHERE DeviceID = @ID;", p =>
        {
            p.AddWithValue("@ID", deviceId);
            p.AddWithValue("@Name", name);
        });

    // Sync logs ---------------------------------------------------------------------------------------------------

    /// <summary>The clinic's latest 200 syncs in the date range, optionally for one device and one status (Clinic details).</summary>
    public async Task<IReadOnlyList<SyncLogEntry>> GetSyncLogsAsync(int clinicId, DateTime from, DateTime to, int? deviceId, string status)
    {
        var page = await GetLogsPageAsync(new SyncLogQuery(from, to, clinicId, deviceId, null, status, 1, 200));
        return page.Items;
    }

    /// <summary>One page of sync logs, newest first (Devices & sync → Sync log).</summary>
    public async Task<PagedResult<SyncLogEntry>> GetLogsPageAsync(SyncLogQuery q)
    {
        const string where = @"
c.IsDeleted = 0 AND s.StartedAt >= @From AND s.StartedAt < @ToEnd
  AND (@C IS NULL OR d.ClinicID = @C) AND (@Device IS NULL OR s.DeviceID = @Device)
  AND (@Dir IS NULL OR s.Direction = @Dir) AND (@Status IS NULL OR s.Status = @Status)";
        string sql = @"
WITH Rows AS (
    SELECT s.SyncLogID, s.DeviceID, d.DeviceName, u.FirstName + ' ' + u.LastName AS UserName, s.Direction, s.StartedAt, s.FinishedAt,
           s.RecordsSent, s.RecordsReceived, s.ConflictsFound, s.Status, s.ErrorMessage, c.ClinicName,
           ROW_NUMBER() OVER (ORDER BY s.StartedAt DESC, s.SyncLogID DESC) AS RowNum
    FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
    LEFT JOIN [User] u ON u.UserID = s.UserID
    WHERE " + where + @")
SELECT * FROM Rows WHERE RowNum BETWEEN @First AND @Last ORDER BY RowNum;
SELECT COUNT(*) FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID WHERE " + where + ";";

        var list = new List<SyncLogEntry>();
        int total;
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@From", q.From.Date);
        command.Parameters.AddWithValue("@ToEnd", q.To.Date.AddDays(1));
        command.Parameters.AddWithValue("@C", (object)q.ClinicId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Device", (object)q.DeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Dir", string.IsNullOrEmpty(q.Direction) ? DBNull.Value : q.Direction);
        command.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(q.Status) ? DBNull.Value : q.Status);
        command.Parameters.AddWithValue("@First", (q.Page - 1) * q.PageSize + 1);
        command.Parameters.AddWithValue("@Last", q.Page * q.PageSize);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add(ReadLog(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }
        return new PagedResult<SyncLogEntry>(list, total, q.Page, q.PageSize);
    }

    /// <summary>A device's last <paramref name="count"/> syncs (device details panel).</summary>
    public async Task<IReadOnlyList<SyncLogEntry>> GetHistoryAsync(int deviceId, int count)
    {
        string sql = @"
SELECT TOP (@Top) s.SyncLogID, s.DeviceID, d.DeviceName, u.FirstName + ' ' + u.LastName, s.Direction, s.StartedAt, s.FinishedAt,
       s.RecordsSent, s.RecordsReceived, s.ConflictsFound, s.Status, s.ErrorMessage, c.ClinicName
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
LEFT JOIN [User] u ON u.UserID = s.UserID
WHERE s.DeviceID = @D ORDER BY s.StartedAt DESC, s.SyncLogID DESC;";
        var list = new List<SyncLogEntry>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Top", count);
        command.Parameters.AddWithValue("@D", deviceId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadLog(reader));
        return list;
    }

    /// <summary>Successful and total syncs of one device since <paramref name="since"/> (success rate).</summary>
    public async Task<(int Success, int Total)> GetSuccessRateAsync(int deviceId, DateTime since)
    {
        const string sql = "SELECT SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END), COUNT(*) FROM SyncLog WHERE DeviceID = @D AND StartedAt >= @Since;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@D", deviceId);
        command.Parameters.AddWithValue("@Since", since);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.GetInt32(1));
    }

    // Summaries ----------------------------------------------------------------------------------------------------

    /// <summary>Failed syncs and conflicts today (one clinic or all).</summary>
    public async Task<(int Failed, int Conflicts)> GetTodayAsync(int? clinicId, DateTime today)
    {
        const string sql = @"
SELECT SUM(CASE WHEN s.Status = 'Failed' THEN 1 ELSE 0 END), SUM(s.ConflictsFound)
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND (@C IS NULL OR d.ClinicID = @C) AND s.StartedAt >= @Today AND s.StartedAt < @Tomorrow;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@C", (object)clinicId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Today", today.Date);
        command.Parameters.AddWithValue("@Tomorrow", today.Date.AddDays(1));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
    }

    /// <summary>Successful and failed syncs per day for the last <paramref name="days"/> days (live logs plus daily totals).</summary>
    public async Task<IReadOnlyList<(DateTime Day, int Success, int Failed)>> GetChartAsync(int? clinicId, DateTime today, int days)
    {
        const string sql = @"
SELECT CONVERT(date, s.StartedAt), SUM(CASE WHEN s.Status = 'Success' THEN 1 ELSE 0 END), SUM(CASE WHEN s.Status = 'Failed' THEN 1 ELSE 0 END)
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND (@C IS NULL OR d.ClinicID = @C) AND s.StartedAt >= @From AND s.StartedAt < @ToEnd
GROUP BY CONVERT(date, s.StartedAt)
UNION ALL
SELECT g.[Day], SUM(g.Successes), SUM(g.Failures)
FROM SyncLogDaily g JOIN Device d ON d.DeviceID = g.DeviceID JOIN Clinic c ON c.ClinicID = d.ClinicID
WHERE c.IsDeleted = 0 AND (@C IS NULL OR d.ClinicID = @C) AND g.[Day] >= @From AND g.[Day] < @ToEnd
GROUP BY g.[Day];";
        var byDay = new Dictionary<DateTime, (int, int)>();
        await using (var connection = await Database.OpenAsync())
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@C", (object)clinicId ?? DBNull.Value);
            command.Parameters.AddWithValue("@From", today.Date.AddDays(1 - days));
            command.Parameters.AddWithValue("@ToEnd", today.Date.AddDays(1));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var day = reader.GetDateTime(0).Date;
                var (s, f) = byDay.TryGetValue(day, out var v) ? v : (0, 0);
                byDay[day] = (s + reader.GetInt32(1), f + reader.GetInt32(2));
            }
        }
        return Enumerable.Range(0, days).Select(i => today.Date.AddDays(i - days + 1))
            .Select(d => (d, byDay.TryGetValue(d, out var v) ? v.Item1 : 0, byDay.TryGetValue(d, out var w) ? w.Item2 : 0)).ToList();
    }

    /// <summary>Failed syncs and conflicts of each clinic in the last 7 days (Clinic sync health).</summary>
    public async Task<Dictionary<int, (int Failed, int Conflicts)>> GetWeekByClinicAsync(DateTime today)
    {
        const string sql = @"
SELECT d.ClinicID, SUM(CASE WHEN s.Status = 'Failed' THEN 1 ELSE 0 END), SUM(s.ConflictsFound)
FROM SyncLog s JOIN Device d ON d.DeviceID = s.DeviceID
WHERE s.StartedAt >= @From GROUP BY d.ClinicID;";
        var map = new Dictionary<int, (int, int)>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@From", today.Date.AddDays(-6));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) map[reader.GetInt32(0)] = (reader.GetInt32(1), reader.GetInt32(2));
        return map;
    }

    /// <summary>
    /// Rule 10: logs older than <paramref name="keepDays"/> are rolled into daily totals per device, then removed.
    /// Returns the rows removed.
    /// </summary>
    public async Task<int> CleanupAsync(int keepDays)
    {
        const string sql = @"
DECLARE @Cutoff datetime = DATEADD(day, -@Keep, CONVERT(date, GETDATE()));
DECLARE @T TABLE (DeviceID int, [Day] date, S int, F int, RS int, RR int, C int);
INSERT INTO @T
SELECT DeviceID, CONVERT(date, StartedAt), SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END), SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END),
       SUM(RecordsSent), SUM(RecordsReceived), SUM(ConflictsFound)
FROM SyncLog WHERE StartedAt < @Cutoff GROUP BY DeviceID, CONVERT(date, StartedAt);
UPDATE g SET Successes = g.Successes + t.S, Failures = g.Failures + t.F, RecordsSent = g.RecordsSent + t.RS,
             RecordsReceived = g.RecordsReceived + t.RR, Conflicts = g.Conflicts + t.C
FROM SyncLogDaily g JOIN @T t ON t.DeviceID = g.DeviceID AND t.[Day] = g.[Day];
INSERT INTO SyncLogDaily (DeviceID, [Day], Successes, Failures, RecordsSent, RecordsReceived, Conflicts)
SELECT t.DeviceID, t.[Day], t.S, t.F, t.RS, t.RR, t.C FROM @T t
WHERE NOT EXISTS (SELECT 1 FROM SyncLogDaily g WHERE g.DeviceID = t.DeviceID AND g.[Day] = t.[Day]);
DELETE FROM SyncLog WHERE StartedAt < @Cutoff;
SELECT @@ROWCOUNT;";
        await using var connection = await Database.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Keep", keepDays);
            int removed = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return removed;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // Helpers ------------------------------------------------------------------------------------------------------

    private static string Escape(string text) => text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static SyncLogEntry ReadLog(SqlDataReader r) => new()
    {
        SyncLogId = r.GetInt64(0),
        DeviceId = r.GetInt32(1),
        DeviceName = r.GetString(2),
        UserName = r.IsDBNull(3) ? null : r.GetString(3),
        Direction = r.GetString(4),
        StartedAt = r.GetDateTime(5),
        FinishedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
        RecordsSent = r.GetInt32(7),
        RecordsReceived = r.GetInt32(8),
        ConflictsFound = r.GetInt32(9),
        Status = r.GetString(10),
        ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
        ClinicName = r.IsDBNull(12) ? null : r.GetString(12)
    };

    private static async Task<IReadOnlyList<ClinicDevice>> ReadDevicesAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        var list = new List<ClinicDevice>();
        var now = DateTime.Now;
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@N", SyncHealthHelper.FailingCount);
        parameters(command.Parameters);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var d = new ClinicDevice
            {
                DeviceId = reader.GetInt32(0),
                ClinicId = reader.GetInt32(1),
                DeviceName = reader.GetString(2),
                MachineCode = reader.GetString(3),
                LastSyncAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                IsActive = reader.GetBoolean(5),
                RegisteredAt = reader.GetDateTime(6),
                PendingChanges = reader.GetInt32(7),
                LastSyncAttemptAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                DeactivatedReason = reader.IsDBNull(9) ? null : reader.GetString(9),
                AppVersion = reader.IsDBNull(10) ? null : reader.GetString(10),
                ClinicName = reader.GetString(11),
                ClinicStatus = reader.IsDBNull(12) ? null : reader.GetString(12),
                RecentSyncs = reader.GetInt32(13),
                RecentFailed = reader.GetInt32(14)
            };
            d.Health = SyncHealthHelper.DeviceHealth(d.IsActive, d.ClinicStatus, d.LastSyncAt, d.RecentSyncs, d.RecentFailed, now);
            list.Add(d);
        }
        return list;
    }

    private static async Task ExecuteAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command.Parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command.Parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
