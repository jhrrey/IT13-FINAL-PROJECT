using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>Which pets the due list shows.</summary>
public enum DueFilter
{
    /// <summary>Overdue, and due within 30 days.</summary>
    All,
    Overdue,
    Due7Days,
    Due30Days
}

/// <summary>Filters for the All records tab. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class VaccinationQuery
{
    public int ClinicId { get; set; }
    public string Search { get; set; }
    public int? MedicineId { get; set; }
    public int? VetId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Counts for the Vaccinations summary cards.</summary>
public sealed class VaccinationSummary
{
    public int GivenThisMonth { get; set; }
    public int DueIn30Days { get; set; }
    public int Overdue { get; set; }
    public int PetsUpToDate { get; set; }
}

public enum VaccinationSaveResult
{
    Saved,
    NotFound,

    /// <summary>Rule 4: the newly chosen vaccine has no stock.</summary>
    OutOfStock
}

/// <summary>
/// All SQL for <c>Vaccination</c>. A vaccination reaches its clinic through Pet → Owner.ClinicID.
/// New vaccinations are written with the medical record (one transaction, MedicalRecordRepository).
/// Records are never deleted.
/// </summary>
public sealed class VaccinationRepository
{
    private const string SelectColumns = @"
v.VaccinationID, v.PetID, v.RecordID, v.MedicineID, m.MedicineName, v.DoseNo, v.DateGiven, v.NextDueDate,
v.GivenBy, u.FirstName AS VetFirstName, u.LastName AS VetLastName, v.CreatedAt, v.SyncStatus,
p.PetName, p.Species, p.Breed, o.OwnerID, o.FirstName AS OwnerFirstName, o.LastName AS OwnerLastName, o.ContactNo AS OwnerContactNo";

    private const string Joins = @"
JOIN Medicine m ON m.MedicineID = v.MedicineID
JOIN [User] u ON u.UserID = v.GivenBy
JOIN Pet p ON p.PetID = v.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID";

    /// <summary>The latest dose of each vaccine per pet (the dose whose NextDueDate matters).</summary>
    private const string LatestDoses = @"
WITH Latest AS (
    SELECT v.*, ROW_NUMBER() OVER (PARTITION BY v.PetID, v.MedicineID ORDER BY v.DateGiven DESC, v.VaccinationID DESC) AS rn
    FROM Vaccination v
    WHERE v.IsDeleted = 0
)";

    // Lists -------------------------------------------------------------------------------------------

    /// <summary>All vaccines the pet received in this clinic, newest first.</summary>
    public Task<IReadOnlyList<RecordVaccination>> GetByPetAsync(int clinicId, int petId) =>
        ReadAsync("SELECT " + SelectColumns + " FROM Vaccination v" + Joins +
                  " WHERE v.PetID = @PetID AND v.IsDeleted = 0 ORDER BY v.DateGiven DESC, v.VaccinationID DESC;",
            c => { c.Parameters.AddWithValue("@ClinicID", clinicId); c.Parameters.AddWithValue("@PetID", petId); });

    /// <summary>The vaccines given during one visit.</summary>
    public Task<IReadOnlyList<RecordVaccination>> GetByRecordAsync(int clinicId, int recordId) =>
        ReadAsync("SELECT " + SelectColumns + " FROM Vaccination v" + Joins +
                  " WHERE v.RecordID = @RecordID AND v.IsDeleted = 0 ORDER BY v.VaccinationID;",
            c => { c.Parameters.AddWithValue("@ClinicID", clinicId); c.Parameters.AddWithValue("@RecordID", recordId); });

    public async Task<RecordVaccination> GetByIdAsync(int clinicId, int vaccinationId) =>
        (await ReadAsync("SELECT " + SelectColumns + " FROM Vaccination v" + Joins + " WHERE v.VaccinationID = @ID AND v.IsDeleted = 0;",
            c => { c.Parameters.AddWithValue("@ClinicID", clinicId); c.Parameters.AddWithValue("@ID", vaccinationId); })).FirstOrDefault();

    /// <summary>
    /// The follow-up list: the latest dose of each vaccine per active pet, due within 30 days or overdue
    /// (narrowed by <paramref name="filter"/>), overdue first then nearest due date.
    /// </summary>
    public Task<IReadOnlyList<RecordVaccination>> GetDueListAsync(int clinicId, DueFilter filter, int? medicineId, string species, DateTime today)
    {
        string range = filter switch
        {
            DueFilter.Overdue => "v.NextDueDate < @Today",
            DueFilter.Due7Days => "v.NextDueDate >= @Today AND v.NextDueDate <= @In7",
            DueFilter.Due30Days => "v.NextDueDate >= @Today AND v.NextDueDate <= @In30",
            _ => "v.NextDueDate <= @In30"
        };
        string sql = LatestDoses + @"
SELECT " + SelectColumns + @"
FROM Latest v" + Joins + @"
WHERE v.rn = 1 AND v.NextDueDate IS NOT NULL AND " + range + @"
  AND p.IsDeleted = 0 AND o.IsDeleted = 0
  AND (@MedicineID IS NULL OR v.MedicineID = @MedicineID)
  AND (@Species IS NULL OR p.Species = @Species)
ORDER BY v.NextDueDate, p.PetName;";

        return ReadAsync(sql, c =>
        {
            c.Parameters.AddWithValue("@ClinicID", clinicId);
            c.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = today.Date });
            c.Parameters.Add(new SqlParameter("@In7", SqlDbType.Date) { Value = today.Date.AddDays(7) });
            c.Parameters.Add(new SqlParameter("@In30", SqlDbType.Date) { Value = today.Date.AddDays(VetCare.Helpers.VaccineScheduleHelper.DueSoonDays) });
            c.Parameters.AddWithValue("@MedicineID", (object)medicineId ?? DBNull.Value);
            c.Parameters.AddWithValue("@Species", (object)species ?? DBNull.Value);
        });
    }

    /// <summary>One page of every vaccine given, newest first (All records tab).</summary>
    public async Task<PagedResult<RecordVaccination>> SearchAsync(VaccinationQuery query)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);
        string where = "v.IsDeleted = 0";
        if (!string.IsNullOrWhiteSpace(query.Search))
            where += " AND (p.PetName LIKE @Search OR o.FirstName + ' ' + o.LastName LIKE @Search OR o.LastName + ' ' + o.FirstName LIKE @Search)";
        if (query.MedicineId != null) where += " AND v.MedicineID = @MedicineID";
        if (query.VetId != null) where += " AND v.GivenBy = @VetID";
        if (query.From != null) where += " AND v.DateGiven >= @From";
        if (query.To != null) where += " AND v.DateGiven <= @To";

        string sql = @"
WITH Matches AS (
    SELECT " + SelectColumns + @", ROW_NUMBER() OVER (ORDER BY v.DateGiven DESC, v.VaccinationID DESC) AS RowNum
    FROM Vaccination v" + Joins + @"
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*) FROM Vaccination v" + Joins + " WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);
        if (!string.IsNullOrWhiteSpace(query.Search))
            command.Parameters.AddWithValue("@Search", "%" + query.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");
        if (query.MedicineId != null) command.Parameters.AddWithValue("@MedicineID", query.MedicineId.Value);
        if (query.VetId != null) command.Parameters.AddWithValue("@VetID", query.VetId.Value);
        if (query.From != null) command.Parameters.Add(new SqlParameter("@From", SqlDbType.Date) { Value = query.From.Value.Date });
        if (query.To != null) command.Parameters.Add(new SqlParameter("@To", SqlDbType.Date) { Value = query.To.Value.Date });

        var list = new List<RecordVaccination>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }
        return new PagedResult<RecordVaccination>(list, total, page, pageSize);
    }

    // Counts --------------------------------------------------------------------------------------------

    public async Task<VaccinationSummary> GetSummaryAsync(int clinicId, DateTime today)
    {
        string sql = LatestDoses + @",
Active AS (
    SELECT l.PetID, l.NextDueDate FROM Latest l
    JOIN Pet p ON p.PetID = l.PetID AND p.IsDeleted = 0
    JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID AND o.IsDeleted = 0
    WHERE l.rn = 1
)
SELECT
  (SELECT COUNT(*) FROM Vaccination v JOIN Pet p ON p.PetID = v.PetID JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID
   WHERE v.IsDeleted = 0 AND v.DateGiven >= @MonthStart AND v.DateGiven < @NextMonth),
  (SELECT COUNT(*) FROM Active WHERE NextDueDate >= @Today AND NextDueDate <= @In30),
  (SELECT COUNT(*) FROM Active WHERE NextDueDate < @Today),
  (SELECT COUNT(DISTINCT PetID) FROM Active a
   WHERE NOT EXISTS (SELECT 1 FROM Active b WHERE b.PetID = a.PetID AND b.NextDueDate <= @In30));";

        var monthStart = new DateTime(today.Year, today.Month, 1);
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = today.Date });
        command.Parameters.Add(new SqlParameter("@In30", SqlDbType.Date) { Value = today.Date.AddDays(VetCare.Helpers.VaccineScheduleHelper.DueSoonDays) });
        command.Parameters.Add(new SqlParameter("@MonthStart", SqlDbType.Date) { Value = monthStart });
        command.Parameters.Add(new SqlParameter("@NextMonth", SqlDbType.Date) { Value = monthStart.AddMonths(1) });

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new VaccinationSummary
        {
            GivenThisMonth = reader.GetInt32(0),
            DueIn30Days = reader.GetInt32(1),
            Overdue = reader.GetInt32(2),
            PetsUpToDate = reader.GetInt32(3)
        };
    }

    /// <summary>PetID → the earliest next due date among the latest dose of each vaccine.</summary>
    public async Task<IDictionary<int, DateTime>> GetNextDueByPetsAsync(int clinicId, IReadOnlyCollection<int> petIds)
    {
        var result = new Dictionary<int, DateTime>();
        if (petIds.Count == 0) return result;
        var names = petIds.Select((id, i) => "@P" + i).ToList();
        string sql = LatestDoses + @"
SELECT l.PetID, MIN(l.NextDueDate) FROM Latest l
JOIN Pet p ON p.PetID = l.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID
WHERE l.rn = 1 AND l.NextDueDate IS NOT NULL AND l.PetID IN (" + string.Join(", ", names) + @")
GROUP BY l.PetID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        int index = 0;
        foreach (int id in petIds) command.Parameters.AddWithValue(names[index++], id);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result[reader.GetInt32(0)] = reader.GetDateTime(1);
        return result;
    }

    /// <summary>Pets of the clinic whose next vaccine (latest dose per vaccine) falls in this calendar month.</summary>
    public async Task<int> CountDueThisMonthAsync(int clinicId, DateTime today)
    {
        string sql = LatestDoses + @"
SELECT COUNT(DISTINCT l.PetID) FROM Latest l
JOIN Pet p ON p.PetID = l.PetID AND p.IsDeleted = 0
JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID AND o.IsDeleted = 0
WHERE l.rn = 1 AND l.NextDueDate >= @MonthStart AND l.NextDueDate < @NextMonth;";

        var monthStart = new DateTime(today.Year, today.Month, 1);
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@MonthStart", SqlDbType.Date) { Value = monthStart });
        command.Parameters.Add(new SqlParameter("@NextMonth", SqlDbType.Date) { Value = monthStart.AddMonths(1) });
        return (int)await command.ExecuteScalarAsync();
    }

    // Editing (rule 10) -----------------------------------------------------------------------------------

    /// <summary>
    /// Saves a same-day correction (vaccine, dose no., next due date). If the vaccine changes on a
    /// completed visit, the old dose goes back to stock and the bill, and the new one is taken (rule 4–6).
    /// The caller has checked that the signed-in vet gave it today.
    /// </summary>
    public async Task<(VaccinationSaveResult Result, string Detail)> UpdateAsync(int clinicId, RecordVaccination changed, int userId)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            int oldMedicine, recordId, appointmentId;
            string status;
            await using (var c = new SqlCommand(@"
SELECT v.MedicineID, v.RecordID, r.AppointmentID, a.Status
FROM Vaccination v WITH (UPDLOCK)
JOIN MedicalRecord r ON r.RecordID = v.RecordID
JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @C
WHERE v.VaccinationID = @ID AND v.IsDeleted = 0 AND v.GivenBy = @U;", connection, tx))
            {
                c.Parameters.AddWithValue("@ID", changed.VaccinationId);
                c.Parameters.AddWithValue("@C", clinicId);
                c.Parameters.AddWithValue("@U", userId);
                await using var r = await c.ExecuteReaderAsync();
                if (!await r.ReadAsync()) { await r.CloseAsync(); await tx.RollbackAsync(); return (VaccinationSaveResult.NotFound, null); }
                oldMedicine = r.GetInt32(0);
                recordId = r.GetInt32(1);
                appointmentId = r.GetInt32(2);
                status = r.GetString(3);
            }

            if (oldMedicine != changed.MedicineId && status == AppointmentStatuses.Completed)
            {
                await VisitCharges.ReturnVaccineAsync(connection, tx, clinicId, appointmentId, recordId, oldMedicine, userId);
                if (!await VisitCharges.GiveVaccineAsync(connection, tx, clinicId, appointmentId, recordId, changed.MedicineId, changed.DoseNo, userId))
                {
                    await tx.RollbackAsync();
                    return (VaccinationSaveResult.OutOfStock, changed.VaccineName);
                }
            }

            await using (var c = new SqlCommand(@"
UPDATE Vaccination SET MedicineID = @M, DoseNo = @Dose, NextDueDate = @Next, UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE VaccinationID = @ID;", connection, tx))
            {
                c.Parameters.AddWithValue("@M", changed.MedicineId);
                c.Parameters.AddWithValue("@Dose", changed.DoseNo);
                c.Parameters.Add(new SqlParameter("@Next", SqlDbType.Date) { Value = (object)changed.NextDueDate?.Date ?? DBNull.Value });
                c.Parameters.AddWithValue("@ID", changed.VaccinationId);
                await c.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return (VaccinationSaveResult.Saved, null);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // Reading -----------------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<RecordVaccination>> ReadAsync(string sql, Action<SqlCommand> addParameters)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        addParameters(command);

        var list = new List<RecordVaccination>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Read(reader));
        return list;
    }

    private static RecordVaccination Read(SqlDataReader r)
    {
        string Text(string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? null : r.GetString(i);
        }
        int record = r.GetOrdinal("RecordID"), next = r.GetOrdinal("NextDueDate");
        return new RecordVaccination
        {
            VaccinationId = r.GetInt32(r.GetOrdinal("VaccinationID")),
            PetId = r.GetInt32(r.GetOrdinal("PetID")),
            RecordId = r.IsDBNull(record) ? null : r.GetInt32(record),
            MedicineId = r.GetInt32(r.GetOrdinal("MedicineID")),
            VaccineName = Text("MedicineName"),
            DoseNo = r.GetInt32(r.GetOrdinal("DoseNo")),
            DateGiven = r.GetDateTime(r.GetOrdinal("DateGiven")),
            NextDueDate = r.IsDBNull(next) ? null : r.GetDateTime(next),
            GivenBy = r.GetInt32(r.GetOrdinal("GivenBy")),
            GivenByName = "Dr. " + Helpers.PersonName.Join(Text("VetFirstName"), Text("VetLastName")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            SyncStatus = Text("SyncStatus"),
            PetName = Text("PetName"),
            Species = Text("Species"),
            Breed = Text("Breed"),
            OwnerId = r.GetInt32(r.GetOrdinal("OwnerID")),
            OwnerFirstName = Text("OwnerFirstName"),
            OwnerLastName = Text("OwnerLastName"),
            OwnerContactNo = Text("OwnerContactNo")
        };
    }
}
