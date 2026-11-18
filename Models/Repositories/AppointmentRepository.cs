using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>
/// All SQL for the <c>Appointment</c> table. Every query is parameterized, filtered by clinic and
/// ignores deleted rows. Written for SQL Server 2008 (ROW_NUMBER paging, no THROW).
/// </summary>
public sealed class AppointmentRepository
{
    private const string SelectColumns = @"
a.AppointmentID, a.ClinicID, a.PetID, a.VetID, a.CreatedBy, a.AppointmentDate, a.AppointmentTime, a.Reason, a.Status,
a.CreatedAt, a.UpdatedAt, a.IsDeleted, a.SyncStatus, a.CheckedInAt, a.StartedAt, a.CompletedAt, a.CancelledAt, a.CancelReason,
COALESCE(p.PetName, a.GuestPetName) AS PetName, COALESCE(p.Species, a.GuestSpecies) AS Species, p.Breed, p.Sex AS PetSex,
a.GuestOwnerName, a.GuestContactNo, a.GuestPetName, a.GuestSpecies,
o.OwnerID, o.FirstName AS OwnerFirstName, o.LastName AS OwnerLastName, o.ContactNo AS OwnerContactNo,
v.FirstName AS VetFirstName, v.LastName AS VetLastName,
b.FirstName AS BookedByFirstName, b.LastName AS BookedByLastName,
(SELECT TOP 1 m.RecordID FROM MedicalRecord m WHERE m.AppointmentID = a.AppointmentID AND m.IsDeleted = 0 ORDER BY m.RecordID) AS MedicalRecordID";

    private const string FromJoins = @"
FROM Appointment a
LEFT JOIN Pet p ON p.PetID = a.PetID
LEFT JOIN Owner o ON o.OwnerID = p.OwnerID
JOIN [User] v ON v.UserID = a.VetID
LEFT JOIN [User] b ON b.UserID = a.CreatedBy";

    /// <summary>Active statuses as a SQL list (rule 4).</summary>
    private const string ActiveStatuses = "('Scheduled', 'Checked In', 'In Consultation')";

    // Reading ------------------------------------------------------------------------------------

    /// <summary>Every matching appointment in the date range, by date then time (queue, calendar, schedule).</summary>
    public async Task<IReadOnlyList<Appointment>> GetRangeAsync(AppointmentQuery query)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand { Connection = connection };
        string where = BuildWhere(command, query);
        command.CommandText = "SELECT " + SelectColumns + FromJoins + " WHERE " + where +
                              " ORDER BY a.AppointmentDate, a.AppointmentTime, v.LastName, a.AppointmentID;";

        var list = new List<Appointment>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Read(reader));
        return list;
    }

    /// <summary>One page of matching appointments, newest date first (List view).</summary>
    public async Task<PagedResult<Appointment>> SearchAsync(AppointmentQuery query)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Max(1, query.PageSize);

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand { Connection = connection };
        string where = BuildWhere(command, query);
        command.CommandText = @"
WITH Matches AS (
    SELECT " + SelectColumns + @",
           ROW_NUMBER() OVER (ORDER BY a.AppointmentDate DESC, a.AppointmentTime, a.AppointmentID) AS RowNum
    " + FromJoins + @"
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;

SELECT COUNT(*) " + FromJoins + " WHERE " + where + ";";
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<Appointment>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }

        return new PagedResult<Appointment>(list, total, page, pageSize);
    }

    /// <summary>The appointment if it belongs to <paramref name="clinicId"/>; otherwise null.</summary>
    public async Task<Appointment> GetByIdAsync(int clinicId, int appointmentId)
    {
        string sql = "SELECT " + SelectColumns + FromJoins +
                     " WHERE a.AppointmentID = @ID AND a.ClinicID = @ClinicID AND a.IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", appointmentId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    /// <summary>Counts for one day's summary cards (cancelled visits don't count as appointments).</summary>
    public async Task<AppointmentDaySummary> GetDaySummaryAsync(int clinicId, DateTime date, int? vetId)
    {
        const string sql = @"
SELECT SUM(CASE WHEN Status <> 'Cancelled' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Status = 'Checked In' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Status = 'In Consultation' THEN 1 ELSE 0 END),
       SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END)
FROM Appointment
WHERE ClinicID = @ClinicID AND AppointmentDate = @Date AND IsDeleted = 0
  AND (@VetID IS NULL OR VetID = @VetID);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = date.Date });
        command.Parameters.AddWithValue("@VetID", (object)vetId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int Value(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
        return new AppointmentDaySummary { Total = Value(0), Waiting = Value(1), InConsultation = Value(2), Completed = Value(3) };
    }

    /// <summary>Start times of the vet's active appointments that day (to grey out taken slots).</summary>
    public async Task<IReadOnlyList<TimeSpan>> GetActiveTimesAsync(int clinicId, int vetId, DateTime date, int? excludeAppointmentId)
    {
        string sql = @"
SELECT AppointmentTime FROM Appointment
WHERE ClinicID = @ClinicID AND VetID = @VetID AND AppointmentDate = @Date AND IsDeleted = 0
  AND Status IN " + ActiveStatuses + @"
  AND (@Exclude IS NULL OR AppointmentID <> @Exclude);";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@VetID", vetId);
        command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = date.Date });
        command.Parameters.AddWithValue("@Exclude", (object)excludeAppointmentId ?? DBNull.Value);

        var times = new List<TimeSpan>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) times.Add(reader.GetTimeSpan(0));
        return times;
    }

    /// <summary>Rule 10: true if the visit has a (not deleted) medical record.</summary>
    public async Task<bool> HasMedicalRecordAsync(int appointmentId)
    {
        const string sql = "SELECT COUNT(*) FROM MedicalRecord WHERE AppointmentID = @ID AND IsDeleted = 0;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", appointmentId);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>Rule 6: true if the pet is active and its owner is active and belongs to the clinic.</summary>
    public async Task<bool> PetBookableAsync(int clinicId, int petId)
    {
        const string sql = @"
SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE p.PetID = @PetID AND p.IsDeleted = 0 AND o.IsDeleted = 0 AND o.ClinicID = @ClinicID;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    // Writing ------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts the appointment unless the vet already has an active one in the same slot (rule 4).
    /// The check and the insert are one statement under a range lock, so two computers can't take
    /// the same slot at once. Walk-ins (<paramref name="exactTimeOnly"/>) only clash at the exact minute.
    /// </summary>
    private const string InsertSql = @"
INSERT INTO Appointment (ClinicID, PetID, VetID, CreatedBy, AppointmentDate, AppointmentTime, Reason, Status, SyncStatus, CheckedInAt,
                         GuestOwnerName, GuestContactNo, GuestPetName, GuestSpecies)
SELECT @ClinicID, @PetID, @VetID, @CreatedBy, @Date, @Time, @Reason, @Status, @SyncStatus, @CheckedInAt,
       @GuestOwnerName, @GuestContactNo, @GuestPetName, @GuestSpecies
WHERE NOT EXISTS (
    SELECT 1 FROM Appointment WITH (UPDLOCK, HOLDLOCK)
    WHERE VetID = @VetID AND AppointmentDate = @Date AND IsDeleted = 0
      AND Status IN " + ActiveStatuses + @"
      AND AppointmentTime >= @SlotStart AND AppointmentTime < @SlotEnd);
IF @@ROWCOUNT = 0 SELECT 0 ELSE SELECT CAST(SCOPE_IDENTITY() AS int);";

    public async Task<(SlotWriteResult Result, int AppointmentId)> InsertAsync(Appointment a, bool exactTimeOnly)
    {
        await using var connection = await Database.OpenAsync();
        return await InsertAsync(connection, null, a, exactTimeOnly, InsertSql);
    }

    private static async Task<(SlotWriteResult Result, int AppointmentId)> InsertAsync(SqlConnection connection, SqlTransaction tx,
        Appointment a, bool exactTimeOnly, string sql)
    {
        await using var command = new SqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("@ClinicID", a.ClinicId);
        AddPetParameters(command, a);
        command.Parameters.AddWithValue("@VetID", a.VetId);
        command.Parameters.AddWithValue("@CreatedBy", a.CreatedBy);
        command.Parameters.AddWithValue("@Reason", a.Reason);
        command.Parameters.AddWithValue("@Status", a.Status);
        command.Parameters.AddWithValue("@SyncStatus", a.SyncStatus ?? SyncStatuses.Synced);
        command.Parameters.Add(new SqlParameter("@CheckedInAt", SqlDbType.DateTime) { Value = (object)a.CheckedInAt ?? DBNull.Value });
        AddSlotParameters(command, a.AppointmentDate, a.AppointmentTime, exactTimeOnly);

        int id = (int)await command.ExecuteScalarAsync();
        if (id == 0) return (SlotWriteResult.SlotTaken, 0);
        a.AppointmentId = id;
        return (SlotWriteResult.Saved, id);
    }

    /// <summary>
    /// Walk-in for a new client (section 4.C): registers the owner and/or pet that aren't saved yet and inserts the
    /// checked-in appointment, all in one transaction. Nothing is saved if the vet was just booked at this minute.
    /// </summary>
    public async Task<(SlotWriteResult Result, int AppointmentId)> RegisterWalkInAsync(Owner newOwner, Pet newPet, Appointment a)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            if (newOwner != null)
            {
                await OwnerRepository.InsertAsync(newOwner, connection, tx);
                if (newPet != null) newPet.OwnerId = newOwner.OwnerId;
            }
            if (newPet != null) a.PetId = await PetRepository.InsertAsync(newPet, connection, tx);

            var result = await InsertAsync(connection, tx, a, exactTimeOnly: true, InsertSql);
            if (result.Result != SlotWriteResult.Saved)
            {
                await tx.RollbackAsync();
                if (newOwner != null) newOwner.OwnerId = 0;
                if (newPet != null) newPet.PetId = 0;
                return result;
            }
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public enum GuestCheckInResult
    {
        CheckedIn,

        /// <summary>Not a guest booking of this clinic that is Scheduled for today (changed meanwhile).</summary>
        NotAllowed,

        /// <summary>The picked existing owner or pet isn't an active one of this clinic.</summary>
        InvalidOwnerOrPet
    }

    /// <summary>
    /// Section 4.C2 / rule 8, in one transaction: registers the new owner and/or pet (or uses the matched ones),
    /// links the pet to the appointment, clears the guest fields and checks the appointment in.
    /// </summary>
    public async Task<GuestCheckInResult> CheckInGuestAsync(int clinicId, int appointmentId, DateTime today,
        int existingOwnerId, Owner newOwner, int existingPetId, Pet newPet)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var c = new SqlCommand(@"
SELECT COUNT(*) FROM Appointment WITH (UPDLOCK)
WHERE AppointmentID = @ID AND ClinicID = @ClinicID AND IsDeleted = 0 AND PetID IS NULL
  AND Status = 'Scheduled' AND AppointmentDate = @Today;", connection, tx))
            {
                c.Parameters.AddWithValue("@ID", appointmentId);
                c.Parameters.AddWithValue("@ClinicID", clinicId);
                c.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = today.Date });
                if ((int)await c.ExecuteScalarAsync() == 0) { await tx.RollbackAsync(); return GuestCheckInResult.NotAllowed; }
            }

            int ownerId = existingOwnerId;
            if (newOwner != null) ownerId = await OwnerRepository.InsertAsync(newOwner, connection, tx);
            else if (!await ExistsAsync(connection, tx, "SELECT COUNT(*) FROM Owner WHERE OwnerID = @A AND ClinicID = @C AND IsDeleted = 0", ownerId, clinicId))
            {
                await tx.RollbackAsync();
                return GuestCheckInResult.InvalidOwnerOrPet;
            }

            int petId = existingPetId;
            if (newPet != null)
            {
                newPet.OwnerId = ownerId;
                petId = await PetRepository.InsertAsync(newPet, connection, tx);
            }
            else if (!await ExistsAsync(connection, tx, @"SELECT COUNT(*) FROM Pet p JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE p.PetID = @A AND p.IsDeleted = 0 AND o.ClinicID = @C AND o.OwnerID = @O", petId, clinicId, ownerId))
            {
                await tx.RollbackAsync();
                return GuestCheckInResult.InvalidOwnerOrPet;
            }

            await using (var c = new SqlCommand(@"
UPDATE Appointment SET PetID = @PetID, WasGuest = 1, GuestOwnerName = NULL, GuestContactNo = NULL, GuestPetName = NULL, GuestSpecies = NULL,
       Status = 'Checked In', CheckedInAt = GETDATE(), UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE AppointmentID = @ID;", connection, tx))
            {
                c.Parameters.AddWithValue("@PetID", petId);
                c.Parameters.AddWithValue("@ID", appointmentId);
                await c.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return GuestCheckInResult.CheckedIn;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, SqlTransaction tx, string sql, int id, int clinicId, int ownerId = 0)
    {
        await using var c = new SqlCommand(sql, connection, tx);
        c.Parameters.AddWithValue("@A", id);
        c.Parameters.AddWithValue("@C", clinicId);
        c.Parameters.AddWithValue("@O", ownerId);
        return (int)await c.ExecuteScalarAsync() > 0;
    }

    /// <summary>
    /// Rule 15: guest bookings that ended Cancelled or No-show more than Retention.GuestInfoDays (30) days ago lose the owner's name and
    /// contact no. The row stays (date, vet, reason, status, pet name and species) for reports. Returns the rows cleared.
    /// </summary>
    public async Task<int> ClearOldGuestInfoAsync(int clinicId)
    {
        const string sql = @"
UPDATE Appointment SET GuestOwnerName = NULL, GuestContactNo = NULL, UpdatedAt = GETDATE()
WHERE ClinicID = @ClinicID AND PetID IS NULL AND Status IN ('Cancelled', 'No-show')
  AND (GuestOwnerName IS NOT NULL OR GuestContactNo IS NOT NULL)
  AND COALESCE(CancelledAt, UpdatedAt) < DATEADD(day, -@Days, GETDATE());";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@Days", VetCare.Helpers.SettingsCache.GetInt(SettingKeys.GuestInfoDays));
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Saves pet, vet, date, time and reason (edit and reschedule, rule 12) with the same slot check.
    /// Only for an appointment of the clinic that is still Scheduled or Checked In (rule 11).
    /// </summary>
    public async Task<SlotWriteResult> UpdateAsync(Appointment a)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM Appointment WHERE AppointmentID = @ID AND ClinicID = @ClinicID AND IsDeleted = 0
               AND Status IN ('Scheduled', 'Checked In'))
    SELECT 2;
ELSE
BEGIN
    UPDATE Appointment
    SET PetID = @PetID, VetID = @VetID, AppointmentDate = @Date, AppointmentTime = @Time, Reason = @Reason,
        GuestOwnerName = @GuestOwnerName, GuestContactNo = @GuestContactNo, GuestPetName = @GuestPetName, GuestSpecies = @GuestSpecies,
        UpdatedAt = GETDATE(), SyncStatus = @SyncStatus
    WHERE AppointmentID = @ID AND ClinicID = @ClinicID AND IsDeleted = 0 AND Status IN ('Scheduled', 'Checked In')
      AND NOT EXISTS (
          SELECT 1 FROM Appointment o WITH (UPDLOCK, HOLDLOCK)
          WHERE o.VetID = @VetID AND o.AppointmentDate = @Date AND o.IsDeleted = 0 AND o.AppointmentID <> @ID
            AND o.Status IN " + ActiveStatuses + @"
            AND o.AppointmentTime >= @SlotStart AND o.AppointmentTime < @SlotEnd);
    IF @@ROWCOUNT = 0 SELECT 1 ELSE SELECT 0;
END";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", a.AppointmentId);
        command.Parameters.AddWithValue("@ClinicID", a.ClinicId);
        AddPetParameters(command, a);
        command.Parameters.AddWithValue("@VetID", a.VetId);
        command.Parameters.AddWithValue("@Reason", a.Reason);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        // A checked-in walk-in keeps its exact minute; everything else is checked per slot.
        bool exact = a.AppointmentTime != TimeSlotHelper.SlotOf(a.AppointmentTime);
        AddSlotParameters(command, a.AppointmentDate, a.AppointmentTime, exact);

        return (int)await command.ExecuteScalarAsync() switch
        {
            0 => SlotWriteResult.Saved,
            1 => SlotWriteResult.SlotTaken,
            _ => SlotWriteResult.NotFound
        };
    }

    /// <summary>
    /// Moves the appointment from one of <paramref name="fromStatuses"/> to <paramref name="toStatus"/> and
    /// stamps the matching timeline column. Returns false if it wasn't in an allowed status (changed meanwhile).
    /// </summary>
    public async Task<bool> SetStatusAsync(int clinicId, int appointmentId, IReadOnlyCollection<string> fromStatuses,
        string toStatus, string cancelReason = null)
    {
        string stamp = toStatus switch
        {
            AppointmentStatuses.CheckedIn => ", CheckedInAt = GETDATE()",
            AppointmentStatuses.InConsultation => ", StartedAt = GETDATE()",
            AppointmentStatuses.Completed => ", CompletedAt = GETDATE()",
            AppointmentStatuses.Cancelled => ", CancelledAt = GETDATE(), CancelReason = @CancelReason",
            _ => string.Empty
        };
        var fromNames = fromStatuses.Select((s, i) => "@From" + i).ToList();

        string sql = "UPDATE Appointment SET Status = @To, UpdatedAt = GETDATE(), SyncStatus = @SyncStatus" + stamp + @"
WHERE AppointmentID = @ID AND ClinicID = @ClinicID AND IsDeleted = 0 AND Status IN (" + string.Join(", ", fromNames) + ");";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", appointmentId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@To", toStatus);
        command.Parameters.AddWithValue("@SyncStatus", SyncStatuses.Synced);
        command.Parameters.AddWithValue("@CancelReason", (object)cancelReason ?? DBNull.Value);
        int index = 0;
        foreach (string status in fromStatuses) command.Parameters.AddWithValue(fromNames[index++], status);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    // Helpers ------------------------------------------------------------------------------------

    /// <summary>PetID (empty for a guest) and the four guest fields (empty for a registered pet).</summary>
    private static void AddPetParameters(SqlCommand command, Appointment a)
    {
        bool guest = a.PetId == null;
        command.Parameters.AddWithValue("@PetID", (object)a.PetId ?? DBNull.Value);
        command.Parameters.AddWithValue("@GuestOwnerName", guest ? (object)a.GuestOwnerName ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@GuestContactNo", guest ? (object)a.GuestContactNo ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@GuestPetName", guest ? (object)a.GuestPetName ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@GuestSpecies", guest ? (object)a.GuestSpecies ?? DBNull.Value : DBNull.Value);
    }

    private static void AddSlotParameters(SqlCommand command, DateTime date, TimeSpan time, bool exactTimeOnly)
    {
        var slotStart = exactTimeOnly ? time : TimeSlotHelper.SlotOf(time);
        var slotEnd = exactTimeOnly ? time + TimeSpan.FromMinutes(1) : slotStart + TimeSlotHelper.SlotLength;
        command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = date.Date });
        command.Parameters.Add(new SqlParameter("@Time", SqlDbType.Time) { Value = time });
        command.Parameters.Add(new SqlParameter("@SlotStart", SqlDbType.Time) { Value = slotStart });
        command.Parameters.Add(new SqlParameter("@SlotEnd", SqlDbType.Time) { Value = slotEnd });
    }

    private static string BuildWhere(SqlCommand command, AppointmentQuery query)
    {
        string where = "a.ClinicID = @ClinicID AND a.IsDeleted = 0";
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);

        if (query.From != null)
        {
            where += " AND a.AppointmentDate >= @From";
            command.Parameters.Add(new SqlParameter("@From", SqlDbType.Date) { Value = query.From.Value.Date });
        }
        if (query.To != null)
        {
            where += " AND a.AppointmentDate <= @To";
            command.Parameters.Add(new SqlParameter("@To", SqlDbType.Date) { Value = query.To.Value.Date });
        }
        if (query.VetId != null)
        {
            where += " AND a.VetID = @VetID";
            command.Parameters.AddWithValue("@VetID", query.VetId.Value);
        }
        if (!string.IsNullOrEmpty(query.Status))
        {
            where += " AND a.Status = @Status";
            command.Parameters.AddWithValue("@Status", query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where += @" AND (p.PetName LIKE @Search OR a.GuestPetName LIKE @Search OR a.GuestOwnerName LIKE @Search
                OR o.FirstName + ' ' + o.LastName LIKE @Search
                OR o.LastName + ' ' + o.FirstName LIKE @Search)";
            command.Parameters.AddWithValue("@Search", "%" + EscapeLike(query.Search.Trim()) + "%");
        }
        return where;
    }

    /// <summary>Makes %, _ and [ match literally inside a LIKE pattern.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static Appointment Read(SqlDataReader reader)
    {
        string Text(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }
        DateTime? When(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetDateTime(i);
        }
        int record = reader.GetOrdinal("MedicalRecordID");

        return new Appointment
        {
            AppointmentId = reader.GetInt32(reader.GetOrdinal("AppointmentID")),
            ClinicId = reader.GetInt32(reader.GetOrdinal("ClinicID")),
            PetId = reader.IsDBNull(reader.GetOrdinal("PetID")) ? null : reader.GetInt32(reader.GetOrdinal("PetID")),
            GuestOwnerName = Text("GuestOwnerName"),
            GuestContactNo = Text("GuestContactNo"),
            GuestPetName = Text("GuestPetName"),
            GuestSpecies = Text("GuestSpecies"),
            VetId = reader.GetInt32(reader.GetOrdinal("VetID")),
            CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            AppointmentTime = reader.GetTimeSpan(reader.GetOrdinal("AppointmentTime")),
            Reason = Text("Reason"),
            Status = Text("Status"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
            SyncStatus = Text("SyncStatus"),
            CheckedInAt = When("CheckedInAt"),
            StartedAt = When("StartedAt"),
            CompletedAt = When("CompletedAt"),
            CancelledAt = When("CancelledAt"),
            CancelReason = Text("CancelReason"),
            PetName = Text("PetName"),
            Species = Text("Species"),
            Breed = Text("Breed"),
            PetSex = Text("PetSex"),
            OwnerId = reader.IsDBNull(reader.GetOrdinal("OwnerID")) ? 0 : reader.GetInt32(reader.GetOrdinal("OwnerID")),
            OwnerFirstName = Text("OwnerFirstName"),
            OwnerLastName = Text("OwnerLastName"),
            OwnerContactNo = Text("OwnerContactNo"),
            VetFirstName = Text("VetFirstName"),
            VetLastName = Text("VetLastName"),
            BookedByFirstName = Text("BookedByFirstName"),
            BookedByLastName = Text("BookedByLastName"),
            MedicalRecordId = reader.IsDBNull(record) ? null : reader.GetInt32(record)
        };
    }
}
