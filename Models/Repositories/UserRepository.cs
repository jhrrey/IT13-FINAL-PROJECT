using System.Text;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// All SQL for the <c>User</c> table. Every query is parameterized and ignores soft-deleted rows.
/// Written for SQL Server 2008 (paging uses ROW_NUMBER, not OFFSET/FETCH).
/// </summary>
public sealed class UserRepository
{
    private const string SelectColumns = @"
u.UserID, u.ClinicID, u.RoleID, r.RoleName, u.FirstName, u.LastName, u.Username, u.PasswordHash,
u.Email, u.ContactNo, u.Status, u.CreatedAt, u.UpdatedAt, u.IsDeleted, u.SyncStatus, u.LastLoginAt, u.MustChangePassword,
c.ClinicName, CASE WHEN c.IsDeleted = 1 THEN 'Rejected' ELSE c.Status END AS ClinicStatus";

    private const string FromJoins = @"
FROM [User] u
JOIN Role r ON r.RoleID = u.RoleID
LEFT JOIN Clinic c ON c.ClinicID = u.ClinicID";

    // Sign-in -------------------------------------------------------------------------

    /// <summary>The user with this email, with their role and clinic; null if none.</summary>
    public async Task<User> FindByEmailAsync(string email)
    {
        string sql = "SELECT TOP 1 " + SelectColumns + FromJoins + " WHERE u.Email = @Email AND u.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Email", email);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    // Lists and counts ----------------------------------------------------------------

    /// <summary>Active Veterinarians of the clinic, by name (appointments: rule 7).</summary>
    public async Task<IReadOnlyList<User>> GetActiveVetsAsync(int clinicId)
    {
        string sql = "SELECT " + SelectColumns + FromJoins + @"
            WHERE u.ClinicID = @ClinicID AND u.RoleID = @RoleID AND u.Status = @Active AND u.IsDeleted = 0
            ORDER BY u.FirstName, u.LastName;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@RoleID", RoleIds.Veterinarian);
        command.Parameters.AddWithValue("@Active", AccountStatus.Active);

        var vets = new List<User>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) vets.Add(Read(reader));
        return vets;
    }

    public async Task<User> GetByIdAsync(int userId)
    {
        string sql = "SELECT " + SelectColumns + FromJoins + " WHERE u.UserID = @UserID AND u.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserID", userId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    /// <summary>One page of users matching <paramref name="query"/>, ordered by name.</summary>
    public async Task<PagedResult<User>> SearchAsync(UserQuery query)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Max(1, query.PageSize);

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand { Connection = connection };
        string where = BuildWhere(command, query, includeFilters: true);

        command.CommandText = @"
WITH Matches AS (
    SELECT " + SelectColumns + @",
           ROW_NUMBER() OVER (ORDER BY u.FirstName, u.LastName, u.UserID) AS RowNum
    " + FromJoins + @"
    WHERE " + where + @"
)
SELECT *
FROM Matches
WHERE RowNum BETWEEN @FirstRow AND @LastRow
ORDER BY RowNum;

SELECT COUNT(*) FROM [User] u WHERE " + where + ";";

        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var users = new List<User>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) users.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }

        return new PagedResult<User>(users, total, page, pageSize);
    }

    /// <summary>Total / active / inactive and per-role counts for the viewer's scope (search filters ignored).</summary>
    public async Task<UserSummary> GetSummaryAsync(int? clinicId, IReadOnlyCollection<int> visibleRoleIds)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand { Connection = connection };
        string where = BuildWhere(command, new UserQuery { ClinicId = clinicId, VisibleRoleIds = visibleRoleIds }, includeFilters: false);

        command.CommandText = @"
SELECT u.RoleID, u.Status, COUNT(*) AS Users
FROM [User] u
WHERE " + where + @"
GROUP BY u.RoleID, u.Status;";

        var summary = new UserSummary();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int roleId = reader.GetInt32(0);
            string status = reader.GetString(1);
            int count = reader.GetInt32(2);

            summary.Total += count;
            if (status == AccountStatus.Active) summary.Active += count;
            else summary.Inactive += count;

            summary.ByRole[roleId] = summary.CountOf(roleId) + count;
        }

        return summary;
    }

    /// <summary>Active Administrators of a clinic, not counting <paramref name="excludingUserId"/> (rule 8).</summary>
    public async Task<int> CountActiveAdministratorsAsync(int clinicId, int excludingUserId)
    {
        const string sql = @"
SELECT COUNT(*) FROM [User]
WHERE ClinicID = @ClinicID AND RoleID = @RoleID AND Status = @Active AND IsDeleted = 0 AND UserID <> @UserID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@RoleID", RoleIds.Administrator);
        command.Parameters.AddWithValue("@Active", AccountStatus.Active);
        command.Parameters.AddWithValue("@UserID", excludingUserId);
        return (int)await command.ExecuteScalarAsync();
    }

    // Uniqueness (rule 4) ----------------------------------------------------------------

    public Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null) =>
        ExistsAsync("Username", username, excludeUserId);

    public Task<bool> EmailExistsAsync(string email, int? excludeUserId = null) =>
        ExistsAsync("Email", email, excludeUserId);

    private static async Task<bool> ExistsAsync(string column, string value, int? excludeUserId)
    {
        // column is one of two fixed names above, never user input.
        string sql = "SELECT COUNT(1) FROM [User] WHERE " + column + " = @Value AND (@Exclude IS NULL OR UserID <> @Exclude);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Value", value);
        command.Parameters.AddWithValue("@Exclude", (object)excludeUserId ?? DBNull.Value);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>A username based on <paramref name="preferred"/> that no other user has.</summary>
    public async Task<string> MakeUniqueUsernameAsync(string preferred)
    {
        string baseName = new string(preferred.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray()).ToLowerInvariant();
        if (baseName.Length == 0) baseName = "user";
        if (baseName.Length > 40) baseName = baseName[..40];

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            string candidate = attempt == 0 ? baseName : baseName + attempt;
            if (!await UsernameExistsAsync(candidate)) return candidate;
        }

        return baseName[..Math.Min(baseName.Length, 30)] + Guid.NewGuid().ToString("N")[..8];
    }

    // Changes ----------------------------------------------------------------------------

    /// <summary>Inserts a user and returns the new UserID.</summary>
    public async Task<int> InsertAsync(User user)
    {
        await using var connection = await Database.OpenAsync();
        return await InsertAsync(user, connection, null);
    }

    /// <summary>Inserts the user (optionally inside an open transaction) and returns the new UserID.</summary>
    internal static async Task<int> InsertAsync(User user, SqlConnection connection, SqlTransaction transaction)
    {
        const string sql = @"
INSERT INTO [User] (ClinicID, RoleID, FirstName, LastName, Username, PasswordHash, Email, ContactNo, Status, SyncStatus, MustChangePassword)
VALUES (@ClinicID, @RoleID, @FirstName, @LastName, @Username, @PasswordHash, @Email, @ContactNo, @Status, @SyncStatus, @MustChange);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@ClinicID", (object)user.ClinicId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RoleID", user.RoleId);
        command.Parameters.AddWithValue("@FirstName", user.FirstName ?? string.Empty);
        command.Parameters.AddWithValue("@LastName", user.LastName ?? string.Empty);
        command.Parameters.AddWithValue("@Username", user.Username);
        command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.Parameters.AddWithValue("@ContactNo", (object)user.ContactNo ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", user.Status ?? AccountStatus.Active);
        command.Parameters.AddWithValue("@SyncStatus", user.SyncStatus ?? SyncStatuses.Synced);
        command.Parameters.AddWithValue("@MustChange", user.MustChangePassword);

        user.UserId = (int)await command.ExecuteScalarAsync();
        return user.UserId;
    }

    /// <summary>Saves the editable fields (not the password) and stamps UpdatedAt.</summary>
    public async Task UpdateAsync(User user)
    {
        const string sql = @"
UPDATE [User]
SET ClinicID = @ClinicID, RoleID = @RoleID, FirstName = @FirstName, LastName = @LastName,
    Username = @Username, Email = @Email, ContactNo = @ContactNo, Status = @Status,
    UpdatedAt = SYSUTCDATETIME(), SyncStatus = @SyncStatus
WHERE UserID = @UserID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserID", user.UserId);
        command.Parameters.AddWithValue("@ClinicID", (object)user.ClinicId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RoleID", user.RoleId);
        command.Parameters.AddWithValue("@FirstName", user.FirstName ?? string.Empty);
        command.Parameters.AddWithValue("@LastName", user.LastName ?? string.Empty);
        command.Parameters.AddWithValue("@Username", user.Username);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.Parameters.AddWithValue("@ContactNo", (object)user.ContactNo ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", user.Status);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    /// <param name="mustChange">True for a temporary password (reset by someone else): the user sets their own at the next sign-in.</param>
    public async Task UpdatePasswordAsync(int userId, string passwordHash, bool mustChange = false)
    {
        const string sql = @"
UPDATE [User] SET PasswordHash = @PasswordHash, MustChangePassword = @MustChange, UpdatedAt = SYSUTCDATETIME(), SyncStatus = @SyncStatus
WHERE UserID = @UserID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserID", userId);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@MustChange", mustChange);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Stamps LastLoginAt (UTC) after a successful sign-in.</summary>
    public async Task RecordLoginAsync(int userId)
    {
        const string sql = "UPDATE [User] SET LastLoginAt = SYSUTCDATETIME() WHERE UserID = @UserID;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserID", userId);
        await command.ExecuteNonQueryAsync();
    }

    // Clinic admins (Super Admin) ---------------------------------------------------------

    /// <summary>Total / active / inactive Administrator accounts (of clinics not deleted), and Active clinics with no active admin.</summary>
    public async Task<(int Total, int Active, int Inactive, int ClinicsWithoutAdmin)> GetAdminSummaryAsync()
    {
        const string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN u.Status = 'Active' THEN 1 ELSE 0 END),
       SUM(CASE WHEN u.Status <> 'Active' THEN 1 ELSE 0 END)
FROM [User] u JOIN Clinic c ON c.ClinicID = u.ClinicID
WHERE u.RoleID = 2 AND u.IsDeleted = 0 AND c.IsDeleted = 0;

SELECT COUNT(*) FROM Clinic c
WHERE c.IsDeleted = 0 AND c.Status = 'Active'
  AND NOT EXISTS (SELECT 1 FROM [User] u WHERE u.ClinicID = c.ClinicID AND u.RoleID = 2 AND u.Status = 'Active' AND u.IsDeleted = 0);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int At(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
        var (total, active, inactive) = (At(0), At(1), At(2));
        await reader.NextResultAsync();
        await reader.ReadAsync();
        return (total, active, inactive, reader.GetInt32(0));
    }

    /// <summary>Active clinics with no active Administrator (the warning card).</summary>
    public async Task<IReadOnlyList<Clinic>> GetClinicsWithoutActiveAdminAsync()
    {
        const string sql = @"
SELECT c.ClinicID, c.ClinicName, c.Status FROM Clinic c
WHERE c.IsDeleted = 0 AND c.Status = 'Active'
  AND NOT EXISTS (SELECT 1 FROM [User] u WHERE u.ClinicID = c.ClinicID AND u.RoleID = 2 AND u.Status = 'Active' AND u.IsDeleted = 0)
ORDER BY c.ClinicName;";

        var list = new List<Clinic>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new Clinic { ClinicId = reader.GetInt32(0), ClinicName = reader.GetString(1), Status = reader.IsDBNull(2) ? null : reader.GetString(2) });
        return list;
    }

    /// <summary>Activates or deactivates (users are never deleted, rule 6).</summary>
    public async Task SetStatusAsync(int userId, string status)
    {
        const string sql = @"
UPDATE [User] SET Status = @Status, UpdatedAt = SYSUTCDATETIME(), SyncStatus = @SyncStatus
WHERE UserID = @UserID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserID", userId);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        await command.ExecuteNonQueryAsync();
    }

    // Super Admin (first-time setup) -----------------------------------------------------

    public async Task<bool> SuperAdminExistsAsync()
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM [User] WHERE RoleID = @RoleID AND IsDeleted = 0;", connection);
        command.Parameters.AddWithValue("@RoleID", RoleIds.SuperAdmin);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>
    /// Creates the first Super Admin (ClinicID NULL). Returns false, without inserting, if a Super Admin
    /// already exists. The check and insert run as one locked statement, so two setups can't both succeed.
    /// </summary>
    public async Task<bool> CreateFirstSuperAdminAsync(User superAdmin)
    {
        const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;
IF EXISTS (SELECT 1 FROM [User] WITH (UPDLOCK, HOLDLOCK) WHERE RoleID = @RoleID AND IsDeleted = 0)
BEGIN
    ROLLBACK;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO [User] (ClinicID, RoleID, FirstName, LastName, Username, PasswordHash, Email, ContactNo, Status, SyncStatus)
    VALUES (NULL, @RoleID, @FirstName, @LastName, @Username, @PasswordHash, @Email, NULL, @Status, @SyncStatus);
    DECLARE @id int;
    SET @id = CAST(SCOPE_IDENTITY() AS int);
    COMMIT;
    SELECT @id;
END";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RoleID", RoleIds.SuperAdmin);
        command.Parameters.AddWithValue("@FirstName", superAdmin.FirstName ?? string.Empty);
        command.Parameters.AddWithValue("@LastName", superAdmin.LastName ?? string.Empty);
        command.Parameters.AddWithValue("@Username", superAdmin.Username);
        command.Parameters.AddWithValue("@PasswordHash", superAdmin.PasswordHash);
        command.Parameters.AddWithValue("@Email", superAdmin.Email);
        command.Parameters.AddWithValue("@Status", superAdmin.Status ?? AccountStatus.Active);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);

        int id = (int)await command.ExecuteScalarAsync();
        if (id == 0) return false;

        superAdmin.UserId = id;
        superAdmin.ClinicId = null;
        superAdmin.RoleId = RoleIds.SuperAdmin;
        superAdmin.RoleName = Roles.SuperAdmin;
        return true;
    }

    // Helpers ----------------------------------------------------------------------------

    /// <summary>Builds the WHERE clause (and its parameters) shared by the list and count queries.</summary>
    private static string BuildWhere(SqlCommand command, UserQuery query, bool includeFilters)
    {
        var where = new StringBuilder("u.IsDeleted = 0");

        // The viewer's scope (always applied).
        if (query.VisibleRoleIds.Count == 0)
        {
            where.Append(" AND 1 = 0"); // nothing visible
        }
        else
        {
            var names = new List<string>();
            int i = 0;
            foreach (int roleId in query.VisibleRoleIds)
            {
                string name = "@VisibleRole" + i++;
                names.Add(name);
                command.Parameters.AddWithValue(name, roleId);
            }
            where.Append(" AND u.RoleID IN (").Append(string.Join(", ", names)).Append(')');
        }

        if (query.ClinicId != null)
        {
            where.Append(" AND u.ClinicID = @ClinicID");
            command.Parameters.AddWithValue("@ClinicID", query.ClinicId.Value);
        }

        if (!includeFilters) return where.ToString();

        // The toolbar filters.
        if (query.RoleId != null)
        {
            where.Append(" AND u.RoleID = @RoleID");
            command.Parameters.AddWithValue("@RoleID", query.RoleId.Value);
        }

        if (!string.IsNullOrEmpty(query.Status))
        {
            where.Append(" AND u.Status = @Status");
            command.Parameters.AddWithValue("@Status", query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where.Append(@" AND (u.FirstName + ' ' + u.LastName LIKE @Search
                OR u.Username LIKE @Search OR u.Email LIKE @Search)");
            command.Parameters.AddWithValue("@Search", "%" + EscapeLike(query.Search.Trim()) + "%");
        }

        return where.ToString();
    }

    /// <summary>Makes %, _ and [ match literally inside a LIKE pattern.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static User Read(SqlDataReader reader)
    {
        string Text(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        int clinicOrdinal = reader.GetOrdinal("ClinicID");
        return new User
        {
            UserId = reader.GetInt32(reader.GetOrdinal("UserID")),
            ClinicId = reader.IsDBNull(clinicOrdinal) ? null : reader.GetInt32(clinicOrdinal),
            RoleId = reader.GetInt32(reader.GetOrdinal("RoleID")),
            RoleName = Text("RoleName"),
            FirstName = Text("FirstName"),
            LastName = Text("LastName"),
            Username = Text("Username"),
            PasswordHash = Text("PasswordHash"),
            Email = Text("Email"),
            ContactNo = Text("ContactNo"),
            Status = Text("Status"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
            SyncStatus = Text("SyncStatus"),
            LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt")),
            MustChangePassword = reader.GetBoolean(reader.GetOrdinal("MustChangePassword")),
            ClinicName = Text("ClinicName"),
            ClinicStatus = Text("ClinicStatus")
        };
    }
}
