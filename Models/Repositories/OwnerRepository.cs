using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// All SQL for the <c>Owner</c> table. Every query is parameterized, filtered by clinic,
/// and ignores archived owners (IsDeleted = 1). Written for SQL Server 2008 (ROW_NUMBER paging).
/// </summary>
public sealed class OwnerRepository
{
    private const string SelectColumns = @"
o.OwnerID, o.ClinicID, o.FirstName, o.LastName, o.ContactNo, o.Email, o.Address,
o.DateRegistered, o.CreatedAt, o.UpdatedAt, o.IsDeleted, o.SyncStatus";

    /// <summary>One page of the clinic's owners, with the "Possible duplicate" flag worked out.</summary>
    public async Task<PagedResult<Owner>> SearchAsync(OwnerQuery query)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Max(1, query.PageSize);

        string where = "o.ClinicID = @ClinicID AND o.IsDeleted = 0";
        bool hasSearch = !string.IsNullOrWhiteSpace(query.Search);
        if (hasSearch)
        {
            where += @" AND (o.FirstName + ' ' + o.LastName LIKE @Search
                OR o.LastName + ' ' + o.FirstName LIKE @Search
                OR o.Email LIKE @Search
                OR (@SearchDigits <> '' AND o.ContactNo LIKE @SearchDigits))";
        }

        string orderBy = query.Sort == OwnerSort.NameAToZ
            ? "o.LastName, o.FirstName, o.OwnerID"
            : "o.DateRegistered DESC, o.OwnerID DESC";

        string sql = @"
WITH Matches AS (
    SELECT " + SelectColumns + @",
           CASE WHEN EXISTS (SELECT 1 FROM Owner d
                             WHERE d.ClinicID = o.ClinicID AND d.ContactNo = o.ContactNo
                               AND d.IsDeleted = 0 AND d.OwnerID <> o.OwnerID)
                THEN 1 ELSE 0 END AS PossibleDuplicate,
           ROW_NUMBER() OVER (ORDER BY " + orderBy + @") AS RowNum
    FROM Owner o
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;

SELECT COUNT(*) FROM Owner o WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);
        if (hasSearch)
        {
            string text = query.Search.Trim();
            string digits = new string(text.Where(char.IsDigit).ToArray());
            command.Parameters.AddWithValue("@Search", "%" + EscapeLike(text) + "%");
            command.Parameters.AddWithValue("@SearchDigits", digits.Length == 0 ? string.Empty : "%" + digits + "%");
        }

        var owners = new List<Owner>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var owner = Read(reader);
                owner.PossibleDuplicate = reader.GetInt32(reader.GetOrdinal("PossibleDuplicate")) == 1;
                owners.Add(owner);
            }
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }

        return new PagedResult<Owner>(owners, total, page, pageSize);
    }

    /// <summary>The owner if they belong to <paramref name="clinicId"/> and aren't archived; otherwise null.</summary>
    public async Task<Owner> GetByIdAsync(int clinicId, int ownerId)
    {
        string sql = "SELECT " + SelectColumns + @",
            CASE WHEN EXISTS (SELECT 1 FROM Owner d
                              WHERE d.ClinicID = o.ClinicID AND d.ContactNo = o.ContactNo
                                AND d.IsDeleted = 0 AND d.OwnerID <> o.OwnerID)
                 THEN 1 ELSE 0 END AS PossibleDuplicate
            FROM Owner o WHERE o.OwnerID = @OwnerID AND o.ClinicID = @ClinicID AND o.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@OwnerID", ownerId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var owner = Read(reader);
        owner.PossibleDuplicate = reader.GetInt32(reader.GetOrdinal("PossibleDuplicate")) == 1;
        return owner;
    }

    /// <summary>An active owner in the clinic with this contact number (other than <paramref name="excludeOwnerId"/>), or null.</summary>
    public async Task<Owner> FindByContactAsync(int clinicId, string contactNo, int? excludeOwnerId)
    {
        string sql = "SELECT TOP 1 " + SelectColumns + @"
            FROM Owner o
            WHERE o.ClinicID = @ClinicID AND o.ContactNo = @ContactNo AND o.IsDeleted = 0
              AND (@Exclude IS NULL OR o.OwnerID <> @Exclude)
            ORDER BY o.DateRegistered;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@ContactNo", contactNo);
        command.Parameters.AddWithValue("@Exclude", (object)excludeOwnerId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    /// <summary>Total owners and owners registered this calendar month (local server time).</summary>
    public async Task<(int Total, int NewThisMonth)> GetCountsAsync(int clinicId)
    {
        const string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN DateRegistered >= DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0) THEN 1 ELSE 0 END)
FROM Owner
WHERE ClinicID = @ClinicID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int total = reader.GetInt32(0);
        int newThisMonth = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        return (total, newThisMonth);
    }

    /// <summary>Inserts the owner (DateRegistered is set by the database) and returns the new OwnerID.</summary>
    public async Task<int> InsertAsync(Owner owner)
    {
        await using var connection = await Database.OpenAsync();
        return await InsertAsync(owner, connection, null);
    }

    /// <summary>The same insert inside the caller's transaction (guest check-in, walk-in registration).</summary>
    internal static async Task<int> InsertAsync(Owner owner, SqlConnection connection, SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO Owner (ClinicID, FirstName, LastName, ContactNo, Email, Address, SyncStatus)
VALUES (@ClinicID, @FirstName, @LastName, @ContactNo, @Email, @Address, @SyncStatus);
SELECT CAST(SCOPE_IDENTITY() AS int);";
        await using var command = new SqlCommand(sql, connection, tx);
        AddEditableParameters(command, owner);
        command.Parameters.AddWithValue("@ClinicID", owner.ClinicId);
        command.Parameters.AddWithValue("@SyncStatus", owner.SyncStatus ?? SyncStatuses.Synced);

        owner.OwnerId = (int)await command.ExecuteScalarAsync();
        return owner.OwnerId;
    }

    /// <summary>Saves the editable fields and stamps UpdatedAt. Only within the owner's clinic.</summary>
    public async Task UpdateAsync(Owner owner)
    {
        const string sql = @"
UPDATE Owner
SET FirstName = @FirstName, LastName = @LastName, ContactNo = @ContactNo, Email = @Email, Address = @Address,
    UpdatedAt = SYSUTCDATETIME(), SyncStatus = @SyncStatus
WHERE OwnerID = @OwnerID AND ClinicID = @ClinicID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddEditableParameters(command, owner);
        command.Parameters.AddWithValue("@OwnerID", owner.OwnerId);
        command.Parameters.AddWithValue("@ClinicID", owner.ClinicId);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Archives (soft-deletes) the owner so old appointments and bills stay complete (rule 6).</summary>
    public async Task ArchiveAsync(int clinicId, int ownerId)
    {
        const string sql = @"
UPDATE Owner SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), SyncStatus = @SyncStatus
WHERE OwnerID = @OwnerID AND ClinicID = @ClinicID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@OwnerID", ownerId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddEditableParameters(SqlCommand command, Owner owner)
    {
        command.Parameters.AddWithValue("@FirstName", owner.FirstName);
        command.Parameters.AddWithValue("@LastName", owner.LastName);
        command.Parameters.AddWithValue("@ContactNo", owner.ContactNo);
        command.Parameters.AddWithValue("@Email", (object)owner.Email ?? DBNull.Value);
        command.Parameters.AddWithValue("@Address", (object)owner.Address ?? DBNull.Value);
    }

    /// <summary>Makes %, _ and [ match literally inside a LIKE pattern.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static Owner Read(SqlDataReader reader)
    {
        string Text(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        return new Owner
        {
            OwnerId = reader.GetInt32(reader.GetOrdinal("OwnerID")),
            ClinicId = reader.GetInt32(reader.GetOrdinal("ClinicID")),
            FirstName = Text("FirstName"),
            LastName = Text("LastName"),
            ContactNo = Text("ContactNo"),
            Email = Text("Email"),
            Address = Text("Address"),
            DateRegistered = reader.GetDateTime(reader.GetOrdinal("DateRegistered")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
            SyncStatus = Text("SyncStatus")
        };
    }
}
