using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>SQL for the <c>SystemSetting</c> and <c>SettingChangeLog</c> tables. Settings are never deleted (rule 4).</summary>
public sealed class SettingRepository
{
    private const string Columns = @"
s.SettingID, s.ClinicID, c.ClinicName, s.SettingKey, s.SettingValue, s.DefaultValue, s.DataType, s.Category, s.AllowClinicOverride,
u.FirstName + ' ' + u.LastName AS UpdatedByName, s.UpdatedAt
FROM SystemSetting s
LEFT JOIN Clinic c ON c.ClinicID = s.ClinicID
LEFT JOIN [User] u ON u.UserID = s.UpdatedBy";

    /// <summary>The global values (ClinicID null).</summary>
    public Task<IReadOnlyList<SystemSetting>> GetGlobalAsync() =>
        ReadAsync("SELECT " + Columns + " WHERE s.ClinicID IS NULL AND s.IsDeleted = 0 ORDER BY s.SettingKey;", _ => { });

    /// <summary>One clinic's overrides.</summary>
    public Task<IReadOnlyList<SystemSetting>> GetOverridesForClinicAsync(int clinicId) =>
        ReadAsync("SELECT " + Columns + " WHERE s.ClinicID = @C AND s.IsDeleted = 0 ORDER BY s.SettingKey;", p => p.AddWithValue("@C", clinicId));

    /// <summary>Every clinic's override of one key (not of deleted clinics).</summary>
    public Task<IReadOnlyList<SystemSetting>> GetOverridesAsync(string key) =>
        ReadAsync("SELECT " + Columns + " WHERE s.SettingKey = @K AND s.ClinicID IS NOT NULL AND s.IsDeleted = 0 AND c.IsDeleted = 0 ORDER BY c.ClinicName;",
            p => p.AddWithValue("@K", key));

    /// <summary>How many clinics override each key.</summary>
    public async Task<Dictionary<string, int>> GetOverrideCountsAsync()
    {
        const string sql = @"
SELECT s.SettingKey, COUNT(*) FROM SystemSetting s JOIN Clinic c ON c.ClinicID = s.ClinicID
WHERE s.ClinicID IS NOT NULL AND s.IsDeleted = 0 AND c.IsDeleted = 0 GROUP BY s.SettingKey;";
        var counts = new Dictionary<string, int>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }

    /// <summary>
    /// Saves global values (clinicId null) or one clinic's overrides in one transaction, writing each change to the
    /// change log (rule 6). A clinic override row is created the first time.
    /// </summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, int userId, int? clinicId)
    {
        await using var connection = await Database.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            foreach (var (key, value) in values)
            {
                await using var command = new SqlCommand(@"
DECLARE @Old varchar(255) = (SELECT SettingValue FROM SystemSetting WHERE SettingKey = @K AND ((@C IS NULL AND ClinicID IS NULL) OR ClinicID = @C));
DECLARE @Global varchar(255) = (SELECT SettingValue FROM SystemSetting WHERE SettingKey = @K AND ClinicID IS NULL);
IF @C IS NULL
    UPDATE SystemSetting SET SettingValue = @V, UpdatedBy = @U, UpdatedAt = GETDATE(), IsDeleted = 0 WHERE SettingKey = @K AND ClinicID IS NULL;
ELSE IF @Old IS NULL
    INSERT INTO SystemSetting (ClinicID, SettingKey, SettingValue, DefaultValue, DataType, Category, AllowClinicOverride, UpdatedBy, UpdatedAt)
    SELECT @C, SettingKey, @V, DefaultValue, DataType, Category, AllowClinicOverride, @U, GETDATE()
    FROM SystemSetting WHERE SettingKey = @K AND ClinicID IS NULL;
ELSE
    UPDATE SystemSetting SET SettingValue = @V, UpdatedBy = @U, UpdatedAt = GETDATE(), IsDeleted = 0 WHERE SettingKey = @K AND ClinicID = @C;
INSERT INTO SettingChangeLog (SettingKey, ClinicID, OldValue, NewValue, ChangedBy) VALUES (@K, @C, ISNULL(@Old, @Global), @V, @U);",
                    connection, transaction);
                command.Parameters.AddWithValue("@K", key);
                command.Parameters.AddWithValue("@V", value ?? string.Empty);
                command.Parameters.AddWithValue("@U", userId);
                command.Parameters.AddWithValue("@C", (object)clinicId ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Removes a clinic's override: that clinic uses the global value again (rule 5). Logged in the history.</summary>
    public async Task RemoveOverrideAsync(string key, int clinicId, int userId)
    {
        const string sql = @"
DECLARE @Old varchar(255) = (SELECT SettingValue FROM SystemSetting WHERE SettingKey = @K AND ClinicID = @C AND IsDeleted = 0);
DECLARE @Global varchar(255) = (SELECT SettingValue FROM SystemSetting WHERE SettingKey = @K AND ClinicID IS NULL);
IF @Old IS NOT NULL
BEGIN
    DELETE FROM SystemSetting WHERE SettingKey = @K AND ClinicID = @C;
    INSERT INTO SettingChangeLog (SettingKey, ClinicID, OldValue, NewValue, ChangedBy) VALUES (@K, @C, @Old, @Global, @U);
END";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@K", key);
        command.Parameters.AddWithValue("@C", clinicId);
        command.Parameters.AddWithValue("@U", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<SettingChange>> GetHistoryAsync(DateTime? from, DateTime? to, string key, string scope, int top = 300)
    {
        // scope: null = all, "global", or a clinic id as text.
        string sql = @"
SELECT TOP (@Top) l.SettingKey, l.ClinicID, c.ClinicName, l.OldValue, l.NewValue, u.FirstName + ' ' + u.LastName, l.ChangedAt
FROM SettingChangeLog l
LEFT JOIN Clinic c ON c.ClinicID = l.ClinicID
LEFT JOIN [User] u ON u.UserID = l.ChangedBy
WHERE (@From IS NULL OR l.ChangedAt >= @From) AND (@ToEnd IS NULL OR l.ChangedAt < @ToEnd)
  AND (@K IS NULL OR l.SettingKey = @K)
  AND (@Scope IS NULL OR (@Scope = 'global' AND l.ClinicID IS NULL) OR (@Scope <> 'global' AND CAST(l.ClinicID AS varchar(20)) = @Scope))
ORDER BY l.ChangedAt DESC, l.ChangeID DESC;";
        var list = new List<SettingChange>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Top", top);
        command.Parameters.AddWithValue("@From", (object)from?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@ToEnd", (object)to?.Date.AddDays(1) ?? DBNull.Value);
        command.Parameters.AddWithValue("@K", (object)key ?? DBNull.Value);
        command.Parameters.AddWithValue("@Scope", (object)scope ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SettingChange
            {
                SettingKey = reader.GetString(0),
                ClinicId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                ClinicName = reader.IsDBNull(2) ? null : reader.GetString(2),
                OldValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                NewValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                ChangedByName = reader.IsDBNull(5) ? null : reader.GetString(5),
                ChangedAt = reader.GetDateTime(6)
            });
        }
        return list;
    }

    public Task<IReadOnlyList<SettingChange>> GetRecentChangesAsync(int count) => GetHistoryAsync(null, null, null, null, count);

    private static async Task<IReadOnlyList<SystemSetting>> ReadAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        var list = new List<SystemSetting>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command.Parameters);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SystemSetting
            {
                SettingId = reader.GetInt32(0),
                ClinicId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                ClinicName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SettingKey = reader.GetString(3),
                SettingValue = reader.GetString(4),
                DefaultValue = reader.GetString(5),
                DataType = reader.GetString(6),
                Category = reader.GetString(7),
                AllowClinicOverride = reader.GetBoolean(8),
                UpdatedByName = reader.IsDBNull(9) ? null : reader.GetString(9),
                UpdatedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            });
        }
        return list;
    }
}
