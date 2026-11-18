using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>All SQL for the <c>Clinic</c> table. Every query is parameterized and skips soft-deleted clinics.</summary>
public sealed class ClinicRepository
{
    private const string Columns = @"
c.ClinicID, c.ClinicName, c.Address, c.ContactNo, c.Email, c.Status, c.DateCreated, c.ConsultationFee,
c.Remarks, c.ApprovedBy, c.ApprovedAt, c.StatusChangedAt";

    /// <summary>The first active Administrator (or any Administrator) of the clinic, plus counts for the list.</summary>
    private const string ListColumns = Columns + @",
a.FirstName + ' ' + a.LastName AS AdminName, a.Email AS AdminEmail, a.Username AS AdminUsername,
(SELECT COUNT(*) FROM [User] u WHERE u.ClinicID = c.ClinicID AND u.IsDeleted = 0) AS UserCount,
(SELECT COUNT(*) FROM Device d WHERE d.ClinicID = c.ClinicID AND d.IsActive = 1) AS DeviceCount,
(SELECT MAX(d.LastSyncAt) FROM Device d WHERE d.ClinicID = c.ClinicID) AS LastSyncAt
FROM Clinic c
OUTER APPLY (SELECT TOP 1 u.FirstName, u.LastName, u.Email, u.Username FROM [User] u
             WHERE u.ClinicID = c.ClinicID AND u.RoleID = 2 AND u.IsDeleted = 0
             ORDER BY CASE WHEN u.Status = 'Active' THEN 0 ELSE 1 END, u.UserID) a";

    /// <summary>Every clinic, by name (for the clinic switcher and printed records).</summary>
    public async Task<IReadOnlyList<Clinic>> GetAllAsync()
    {
        string sql = "SELECT " + Columns + " FROM Clinic c WHERE c.IsDeleted = 0 ORDER BY c.ClinicName;";
        return await ReadAsync(sql, _ => { }, list: false);
    }

    /// <summary>One clinic, or null.</summary>
    public async Task<Clinic> GetByIdAsync(int clinicId)
    {
        string sql = "SELECT " + ListColumns + " WHERE c.ClinicID = @ID AND c.IsDeleted = 0;";
        return (await ReadAsync(sql, p => p.AddWithValue("@ID", clinicId), list: true)).FirstOrDefault();
    }

    /// <summary>The All clinics tab: search on name, e-mail or address, and an optional status.</summary>
    public async Task<IReadOnlyList<Clinic>> SearchAsync(string search, string status)
    {
        string sql = "SELECT " + ListColumns + @"
WHERE c.IsDeleted = 0
  AND (@Status IS NULL OR c.Status = @Status)
  AND (@Search IS NULL OR c.ClinicName LIKE @Search OR c.Email LIKE @Search OR c.Address LIKE @Search)
ORDER BY c.ClinicName;";
        return await ReadAsync(sql, p =>
        {
            p.AddWithValue("@Status", string.IsNullOrEmpty(status) ? DBNull.Value : status);
            p.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : "%" + Escape(search.Trim()) + "%");
        }, list: true);
    }

    /// <summary>Sign-ups waiting for review, oldest first.</summary>
    public async Task<IReadOnlyList<Clinic>> GetPendingAsync()
    {
        string sql = "SELECT " + ListColumns + " WHERE c.IsDeleted = 0 AND c.Status = 'Pending' ORDER BY c.DateCreated, c.ClinicID;";
        return await ReadAsync(sql, _ => { }, list: true);
    }

    public async Task<ClinicSummary> GetSummaryAsync()
    {
        const string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN Status = 'Active' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Status = 'Inactive' THEN 1 ELSE 0 END)
FROM Clinic WHERE IsDeleted = 0;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int At(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
        return new ClinicSummary(At(0), At(1), At(2), At(3));
    }

    /// <summary>Counts only (rule 10): users by role, owners, pets, this month's appointments, devices, last activity.</summary>
    public async Task<ClinicOverview> GetOverviewAsync(int clinicId, DateTime today)
    {
        const string sql = @"
SELECT
 (SELECT COUNT(*) FROM [User] WHERE ClinicID = @C AND IsDeleted = 0 AND RoleID = 2),
 (SELECT COUNT(*) FROM [User] WHERE ClinicID = @C AND IsDeleted = 0 AND RoleID = 3),
 (SELECT COUNT(*) FROM [User] WHERE ClinicID = @C AND IsDeleted = 0 AND RoleID = 4),
 (SELECT COUNT(*) FROM Owner WHERE ClinicID = @C AND IsDeleted = 0),
 (SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID WHERE o.ClinicID = @C AND p.IsDeleted = 0 AND o.IsDeleted = 0),
 (SELECT COUNT(*) FROM Appointment WHERE ClinicID = @C AND IsDeleted = 0 AND AppointmentDate >= @MonthStart AND AppointmentDate < @NextMonth),
 (SELECT COUNT(*) FROM Device WHERE ClinicID = @C AND IsActive = 1),
 (SELECT COUNT(*) FROM Device WHERE ClinicID = @C AND IsActive = 1 AND (LastSyncAt IS NULL OR LastSyncAt < @DayAgo)),
 (SELECT MIN(LastSyncAt) FROM Device WHERE ClinicID = @C AND IsActive = 1 AND LastSyncAt < @DayAgo),
 (SELECT MAX(t) FROM (
     SELECT MAX(UpdatedAt) AS t FROM Appointment WHERE ClinicID = @C
     UNION ALL SELECT MAX(UpdatedAt) FROM Owner WHERE ClinicID = @C
     UNION ALL SELECT MAX(p.UpdatedAt) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID WHERE o.ClinicID = @C
     UNION ALL SELECT MAX(UpdatedAt) FROM Bill WHERE ClinicID = @C) x);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        command.Parameters.AddWithValue("@C", clinicId);
        command.Parameters.AddWithValue("@MonthStart", monthStart);
        command.Parameters.AddWithValue("@NextMonth", monthStart.AddMonths(1));
        command.Parameters.AddWithValue("@DayAgo", DateTime.Now.AddHours(-Helpers.SyncHealthHelper.StaleHours));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new ClinicOverview
        {
            Admins = reader.GetInt32(0),
            Vets = reader.GetInt32(1),
            Staff = reader.GetInt32(2),
            Owners = reader.GetInt32(3),
            Pets = reader.GetInt32(4),
            AppointmentsThisMonth = reader.GetInt32(5),
            Devices = reader.GetInt32(6),
            StaleDevices = reader.GetInt32(7),
            OldestSync = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            LastActivity = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
        };
    }

    /// <summary>
    /// Other clinics with the same name and address, the same e-mail, or the same contact no. (digits only) (rule 3).
    /// </summary>
    public async Task<IReadOnlyList<ClinicDuplicate>> FindDuplicatesAsync(string name, string address, string email, string contactDigits, int? excludeId)
    {
        const string sql = @"
SELECT ClinicID, ClinicName, Status,
       CASE WHEN @Email IS NOT NULL AND Email = @Email THEN 'email'
            WHEN @Contact IS NOT NULL AND REPLACE(REPLACE(REPLACE(REPLACE(ContactNo, ' ', ''), '-', ''), '(', ''), ')', '') = @Contact THEN 'contact no.'
            ELSE 'name and address' END
FROM Clinic
WHERE IsDeleted = 0 AND (@Exclude IS NULL OR ClinicID <> @Exclude)
  AND ((@Email IS NOT NULL AND Email = @Email)
    OR (@Contact IS NOT NULL AND REPLACE(REPLACE(REPLACE(REPLACE(ContactNo, ' ', ''), '-', ''), '(', ''), ')', '') = @Contact)
    OR (ClinicName = @Name AND ISNULL(Address, '') = ISNULL(@Address, '')))
ORDER BY ClinicName;";

        var list = new List<ClinicDuplicate>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Name", (object)name ?? DBNull.Value);
        command.Parameters.AddWithValue("@Address", string.IsNullOrWhiteSpace(address) ? DBNull.Value : address);
        command.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email);
        command.Parameters.AddWithValue("@Contact", string.IsNullOrWhiteSpace(contactDigits) ? DBNull.Value : contactDigits);
        command.Parameters.AddWithValue("@Exclude", (object)excludeId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new ClinicDuplicate(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        return list;
    }

    /// <summary>True if another (not deleted) clinic already uses this e-mail (rule 2).</summary>
    public async Task<bool> EmailUsedAsync(string email, int? excludeId)
    {
        const string sql = "SELECT COUNT(*) FROM Clinic WHERE IsDeleted = 0 AND Email = @Email AND (@Exclude IS NULL OR ClinicID <> @Exclude);";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@Exclude", (object)excludeId ?? DBNull.Value);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>
    /// Saves a clinic and its first Administrator in one transaction: either both rows are saved or neither is.
    /// Used by the "Create account" form (Pending) and by the Super Admin's Add clinic (Active). Returns the new ClinicID.
    /// </summary>
    public async Task<int> RegisterWithAdministratorAsync(Clinic clinic, User administrator, int? approvedBy = null)
    {
        const string sql = @"
INSERT INTO Clinic (ClinicName, Address, ContactNo, Email, Status, StatusChangedAt, UpdatedAt, ApprovedBy, ApprovedAt)
VALUES (@ClinicName, @Address, @ContactNo, @Email, @Status, GETDATE(), GETDATE(), @ApprovedBy,
        CASE WHEN @Status = 'Active' THEN GETDATE() ELSE NULL END);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        await using var connection = await Database.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            int clinicId;
            await using (var command = new SqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@ClinicName", clinic.ClinicName);
                command.Parameters.AddWithValue("@Address", (object)clinic.Address ?? DBNull.Value);
                command.Parameters.AddWithValue("@ContactNo", (object)clinic.ContactNo ?? DBNull.Value);
                command.Parameters.AddWithValue("@Email", (object)clinic.Email ?? DBNull.Value);
                command.Parameters.AddWithValue("@Status", (object)clinic.Status ?? DBNull.Value);
                command.Parameters.AddWithValue("@ApprovedBy", (object)approvedBy ?? DBNull.Value);
                clinicId = (int)await command.ExecuteScalarAsync();
            }

            administrator.ClinicId = clinicId;
            administrator.UserId = await UserRepository.InsertAsync(administrator, connection, transaction);

            await transaction.CommitAsync();
            clinic.ClinicId = clinicId;
            return clinicId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(Clinic clinic)
    {
        const string sql = @"
UPDATE Clinic SET ClinicName = @ClinicName, Address = @Address, ContactNo = @ContactNo, Email = @Email, UpdatedAt = GETDATE()
WHERE ClinicID = @ID AND IsDeleted = 0;";
        await ExecuteAsync(sql, p =>
        {
            p.AddWithValue("@ID", clinic.ClinicId);
            p.AddWithValue("@ClinicName", clinic.ClinicName);
            p.AddWithValue("@Address", (object)clinic.Address ?? DBNull.Value);
            p.AddWithValue("@ContactNo", (object)clinic.ContactNo ?? DBNull.Value);
            p.AddWithValue("@Email", (object)clinic.Email ?? DBNull.Value);
        });
    }

    /// <summary>Approve a Pending sign-up: the clinic becomes Active and its Administrator accounts are turned on (rule 5).</summary>
    public async Task<bool> ApproveAsync(int clinicId, int approvedBy)
    {
        const string sql = @"
DECLARE @N int;
UPDATE Clinic SET Status = 'Active', ApprovedBy = @By, ApprovedAt = GETDATE(), StatusChangedAt = GETDATE(), UpdatedAt = GETDATE(), Remarks = NULL
WHERE ClinicID = @ID AND Status = 'Pending' AND IsDeleted = 0;
SET @N = @@ROWCOUNT;
IF @N = 1
    UPDATE [User] SET Status = 'Active', UpdatedAt = SYSUTCDATETIME()
    WHERE ClinicID = @ID AND RoleID = 2 AND IsDeleted = 0;
SELECT @N;";
        return await ScalarInTransactionAsync(sql, p => { p.AddWithValue("@ID", clinicId); p.AddWithValue("@By", approvedBy); }) > 0;
    }

    /// <summary>Reject a Pending sign-up with a reason (rule 6). The admin account stays inactive.</summary>
    public async Task<bool> RejectAsync(int clinicId, string reason)
    {
        const string sql = @"
UPDATE Clinic SET Status = 'Rejected', Remarks = @Reason, StatusChangedAt = GETDATE(), UpdatedAt = GETDATE()
WHERE ClinicID = @ID AND Status = 'Pending' AND IsDeleted = 0;
SELECT @@ROWCOUNT;";
        return await ScalarInTransactionAsync(sql, p => { p.AddWithValue("@ID", clinicId); p.AddWithValue("@Reason", reason); }) > 0;
    }

    /// <summary>Deactivate (rule 7): users and devices are blocked at sign-in / sync; nothing is deleted.</summary>
    public Task DeactivateAsync(int clinicId, string reason) =>
        ExecuteAsync(@"
UPDATE Clinic SET Status = 'Inactive', Remarks = @Reason, StatusChangedAt = GETDATE(), UpdatedAt = GETDATE()
WHERE ClinicID = @ID AND Status = 'Active' AND IsDeleted = 0;",
            p => { p.AddWithValue("@ID", clinicId); p.AddWithValue("@Reason", reason); });

    public Task ActivateAsync(int clinicId) =>
        ExecuteAsync(@"
UPDATE Clinic SET Status = 'Active', Remarks = NULL, StatusChangedAt = GETDATE(), UpdatedAt = GETDATE()
WHERE ClinicID = @ID AND Status = 'Inactive' AND IsDeleted = 0;",
            p => p.AddWithValue("@ID", clinicId));

    /// <summary>Rejected sign-ups older than 30 days are soft-deleted, with their (inactive) users (rule 6).</summary>
    public async Task<int> PurgeRejectedAsync(DateTime now)
    {
        const string sql = @"
DECLARE @Ids TABLE (ClinicID int);
INSERT INTO @Ids SELECT ClinicID FROM Clinic
WHERE IsDeleted = 0 AND Status = 'Rejected' AND ISNULL(StatusChangedAt, DateCreated) < @Cutoff;
UPDATE [User] SET Status = 'Inactive', IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE ClinicID IN (SELECT ClinicID FROM @Ids);
UPDATE Clinic SET IsDeleted = 1, UpdatedAt = GETDATE() WHERE ClinicID IN (SELECT ClinicID FROM @Ids);
SELECT COUNT(*) FROM @Ids;";
        return await ScalarInTransactionAsync(sql, p => p.AddWithValue("@Cutoff", now.AddDays(-Helpers.SettingsCache.GetInt(SettingKeys.RejectedSignupDays))));
    }

    // Helpers -----------------------------------------------------------------------------

    private static string Escape(string text) => text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static async Task<List<Clinic>> ReadAsync(string sql, Action<SqlParameterCollection> parameters, bool list)
    {
        var clinics = new List<Clinic>();
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command.Parameters);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            DateTime? Date(int i) => reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            var clinic = new Clinic
            {
                ClinicId = reader.GetInt32(0),
                ClinicName = reader.GetString(1),
                Address = Text(2),
                ContactNo = Text(3),
                Email = Text(4),
                Status = Text(5),
                DateCreated = Date(6),
                ConsultationFee = reader.GetDecimal(7),
                Remarks = Text(8),
                ApprovedBy = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                ApprovedAt = Date(10),
                StatusChangedAt = Date(11)
            };
            if (list)
            {
                clinic.AdminName = Text(12);
                clinic.AdminEmail = Text(13);
                clinic.AdminUsername = Text(14);
                clinic.UserCount = reader.GetInt32(15);
                clinic.DeviceCount = reader.GetInt32(16);
                clinic.LastSyncAt = Date(17);
            }
            clinics.Add(clinic);
        }
        return clinics;
    }

    private static async Task ExecuteAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        parameters(command.Parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarInTransactionAsync(string sql, Action<SqlParameterCollection> parameters)
    {
        await using var connection = await Database.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            parameters(command.Parameters);
            int result = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
