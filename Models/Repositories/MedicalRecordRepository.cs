using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>Filters for the records list. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class MedicalRecordQuery
{
    public int ClinicId { get; set; }

    /// <summary>Matches the pet name, the owner's name or the diagnosis.</summary>
    public string Search { get; set; }

    public int? VetId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Counts for the Medical records summary cards.</summary>
public sealed class MedicalRecordSummary
{
    public int Today { get; set; }
    public int ThisWeek { get; set; }
    public int OngoingConsultations { get; set; }
    public int PrescriptionsNotDispensed { get; set; }
}

public enum RecordSaveResult
{
    Saved,

    /// <summary>The appointment / record no longer allows this (status changed, another vet, locked).</summary>
    NotAllowed,

    /// <summary>Vaccination rule 4: a vaccine has no stock left (Detail = its name).</summary>
    OutOfStock
}

/// <summary>
/// All SQL for <c>MedicalRecord</c>, its prescriptions, vaccinations and added notes. A record reaches
/// its clinic through the appointment (<c>Appointment.ClinicID</c>). Records are never deleted.
/// </summary>
public sealed class MedicalRecordRepository
{
    private const string SelectColumns = @"
r.RecordID, r.AppointmentID, r.PetID, r.VetID, r.Weight, r.Temperature, r.Diagnosis, r.Treatment, r.Notes,
r.RecordDate, r.CreatedAt, r.UpdatedAt, r.SyncStatus,
p.PetName, p.Species, p.Breed, p.Sex AS PetSex, p.BirthDate AS PetBirthDate,
o.OwnerID, o.FirstName AS OwnerFirstName, o.LastName AS OwnerLastName, o.ContactNo AS OwnerContactNo,
v.FirstName AS VetFirstName, v.LastName AS VetLastName,
a.AppointmentDate, a.AppointmentTime, a.Reason, a.Status AS AppointmentStatus,
(SELECT COUNT(*) FROM Prescription x WHERE x.RecordID = r.RecordID AND x.IsDeleted = 0) AS PrescriptionCount,
(SELECT COUNT(*) FROM Vaccination y WHERE y.RecordID = r.RecordID AND y.IsDeleted = 0) AS VaccinationCount";

    private const string FromJoins = @"
FROM MedicalRecord r
JOIN Appointment a ON a.AppointmentID = r.AppointmentID
JOIN Pet p ON p.PetID = r.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID
JOIN [User] v ON v.UserID = r.VetID";

    // Reading ------------------------------------------------------------------------------------

    /// <summary>One page of the clinic's records, newest first.</summary>
    public async Task<PagedResult<MedicalRecord>> SearchAsync(MedicalRecordQuery query)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand { Connection = connection };
        string where = "a.ClinicID = @ClinicID AND r.IsDeleted = 0";
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where += @" AND (p.PetName LIKE @Search OR o.FirstName + ' ' + o.LastName LIKE @Search
                OR o.LastName + ' ' + o.FirstName LIKE @Search OR r.Diagnosis LIKE @Search)";
            command.Parameters.AddWithValue("@Search", "%" + EscapeLike(query.Search.Trim()) + "%");
        }
        if (query.VetId != null)
        {
            where += " AND r.VetID = @VetID";
            command.Parameters.AddWithValue("@VetID", query.VetId.Value);
        }
        if (query.From != null)
        {
            where += " AND r.RecordDate >= @From";
            command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = query.From.Value.Date });
        }
        if (query.To != null)
        {
            where += " AND r.RecordDate < @To";
            command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = query.To.Value.Date.AddDays(1) });
        }

        command.CommandText = @"
WITH Matches AS (
    SELECT " + SelectColumns + @", ROW_NUMBER() OVER (ORDER BY r.RecordDate DESC, r.RecordID DESC) AS RowNum
    " + FromJoins + @"
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;

SELECT COUNT(*) " + FromJoins + " WHERE " + where + ";";
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<MedicalRecord>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }
        return new PagedResult<MedicalRecord>(list, total, page, pageSize);
    }

    public Task<MedicalRecord> GetByIdAsync(int clinicId, int recordId) =>
        GetOneAsync("r.RecordID = @ID", clinicId, recordId);

    public Task<MedicalRecord> GetByAppointmentAsync(int clinicId, int appointmentId) =>
        GetOneAsync("r.AppointmentID = @ID", clinicId, appointmentId);

    /// <summary>The pet's records, newest first (optionally without one, and limited).</summary>
    public async Task<IReadOnlyList<MedicalRecord>> GetPetHistoryAsync(int clinicId, int petId, int? excludeRecordId = null, int? take = null)
    {
        string top = take != null ? "TOP " + take.Value + " " : string.Empty;
        string sql = "SELECT " + top + SelectColumns + FromJoins + @"
            WHERE a.ClinicID = @ClinicID AND r.PetID = @PetID AND r.IsDeleted = 0
              AND (@Exclude IS NULL OR r.RecordID <> @Exclude)
            ORDER BY r.RecordDate DESC, r.RecordID DESC;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.AddWithValue("@Exclude", (object)excludeRecordId ?? DBNull.Value);

        var list = new List<MedicalRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Read(reader));
        return list;
    }

    public async Task<MedicalRecordSummary> GetSummaryAsync(int clinicId, DateTime today)
    {
        const string sql = @"
SELECT
  (SELECT COUNT(*) FROM MedicalRecord r JOIN Appointment a ON a.AppointmentID = r.AppointmentID
   WHERE a.ClinicID = @ClinicID AND r.IsDeleted = 0 AND r.RecordDate >= @Today AND r.RecordDate < @Tomorrow),
  (SELECT COUNT(*) FROM MedicalRecord r JOIN Appointment a ON a.AppointmentID = r.AppointmentID
   WHERE a.ClinicID = @ClinicID AND r.IsDeleted = 0 AND r.RecordDate >= @WeekStart AND r.RecordDate < @Tomorrow),
  (SELECT COUNT(*) FROM Appointment a
   WHERE a.ClinicID = @ClinicID AND a.IsDeleted = 0 AND a.Status = 'In Consultation'),
  (SELECT COUNT(*) FROM Prescription x JOIN MedicalRecord r ON r.RecordID = x.RecordID
   JOIN Appointment a ON a.AppointmentID = r.AppointmentID
   WHERE a.ClinicID = @ClinicID AND x.IsDeleted = 0 AND x.IsDispensed = 0);";

        var weekStart = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.DateTime) { Value = today.Date });
        command.Parameters.Add(new SqlParameter("@Tomorrow", SqlDbType.DateTime) { Value = today.Date.AddDays(1) });
        command.Parameters.Add(new SqlParameter("@WeekStart", SqlDbType.DateTime) { Value = weekStart });

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new MedicalRecordSummary
        {
            Today = reader.GetInt32(0),
            ThisWeek = reader.GetInt32(1),
            OngoingConsultations = reader.GetInt32(2),
            PrescriptionsNotDispensed = reader.GetInt32(3)
        };
    }

    public async Task<IReadOnlyList<RecordPrescription>> GetPrescriptionsAsync(int recordId)
    {
        const string sql = @"
SELECT x.PrescriptionID, x.RecordID, x.MedicineID, m.MedicineName, m.Unit, m.StockQty, x.Quantity, x.Dosage, x.Instructions, x.IsDispensed
FROM Prescription x JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE x.RecordID = @RecordID AND x.IsDeleted = 0
ORDER BY x.PrescriptionID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RecordID", recordId);

        var list = new List<RecordPrescription>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new RecordPrescription
            {
                PrescriptionId = reader.GetInt32(0),
                RecordId = reader.GetInt32(1),
                MedicineId = reader.GetInt32(2),
                MedicineName = reader.GetString(3),
                Unit = reader.GetString(4),
                StockQty = reader.GetInt32(5),
                Quantity = reader.GetInt32(6),
                Dosage = reader.IsDBNull(7) ? null : reader.GetString(7),
                Instructions = reader.IsDBNull(8) ? null : reader.GetString(8),
                IsDispensed = reader.GetBoolean(9)
            });
        return list;
    }

    public async Task<IReadOnlyList<RecordNote>> GetNotesAsync(int recordId)
    {
        const string sql = @"
SELECT n.NoteID, n.RecordID, n.VetID, u.FirstName, u.LastName, n.NoteText, n.CreatedAt
FROM MedicalRecordNote n JOIN [User] u ON u.UserID = n.VetID
WHERE n.RecordID = @RecordID
ORDER BY n.CreatedAt, n.NoteID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RecordID", recordId);

        var list = new List<RecordNote>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new RecordNote
            {
                NoteId = reader.GetInt32(0),
                RecordId = reader.GetInt32(1),
                VetId = reader.GetInt32(2),
                VetName = "Dr. " + Helpers.PersonName.Join(reader.GetString(3), reader.GetString(4)),
                NoteText = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        return list;
    }

    // Writing ------------------------------------------------------------------------------------

    /// <summary>
    /// Rule 10: saves the record, its (not yet dispensed) prescriptions and its vaccinations, updates
    /// Pet.Weight (rule 7) and, when <paramref name="complete"/>, marks the appointment Completed —
    /// all in one transaction. A new record needs the appointment In Consultation with this vet
    /// (rule 3); an existing one must belong to this vet (rule 11, the day is checked by the controller).
    /// </summary>
    public async Task<(RecordSaveResult Result, int RecordId, string Detail)> SaveConsultationAsync(int clinicId, MedicalRecord record,
        IReadOnlyList<RecordPrescription> prescriptions, IReadOnlyList<RecordVaccination> vaccinations, bool complete)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            SqlCommand Command(string sql)
            {
                var c = new SqlCommand(sql, connection, tx);
                return c;
            }

            // The appointment (locked for the rest of the transaction).
            string status;
            int apptVet, apptPet;
            await using (var c = Command(@"
SELECT Status, VetID, PetID FROM Appointment WITH (UPDLOCK)
WHERE AppointmentID = @A AND ClinicID = @C AND IsDeleted = 0;"))
            {
                c.Parameters.AddWithValue("@A", record.AppointmentId);
                c.Parameters.AddWithValue("@C", clinicId);
                await using var r = await c.ExecuteReaderAsync();
                if (!await r.ReadAsync()) { await r.CloseAsync(); await tx.RollbackAsync(); return (RecordSaveResult.NotAllowed, 0, null); }
                status = r.GetString(0);
                apptVet = r.GetInt32(1);
                apptPet = r.GetInt32(2);
            }

            // The existing record of this appointment, if any (rule 4: only one).
            int recordId = 0, recordVet = 0;
            await using (var c = Command("SELECT RecordID, VetID FROM MedicalRecord WITH (UPDLOCK) WHERE AppointmentID = @A AND IsDeleted = 0;"))
            {
                c.Parameters.AddWithValue("@A", record.AppointmentId);
                await using var r = await c.ExecuteReaderAsync();
                if (await r.ReadAsync()) { recordId = r.GetInt32(0); recordVet = r.GetInt32(1); }
            }

            bool allowed = recordId == 0
                ? status == AppointmentStatuses.InConsultation && apptVet == record.VetId
                : recordVet == record.VetId;
            if (!allowed || (complete && status != AppointmentStatuses.InConsultation))
            {
                await tx.RollbackAsync();
                return (RecordSaveResult.NotAllowed, recordId, null);
            }

            if (recordId == 0)
            {
                await using var c = Command(@"
INSERT INTO MedicalRecord (AppointmentID, PetID, VetID, Weight, Temperature, Diagnosis, Treatment, Notes, SyncStatus)
VALUES (@A, @Pet, @Vet, @Weight, @Temp, @Diagnosis, @Treatment, @Notes, 'Synced');
SELECT CAST(SCOPE_IDENTITY() AS int);");
                AddRecordParameters(c, record);
                c.Parameters.AddWithValue("@A", record.AppointmentId);
                c.Parameters.AddWithValue("@Pet", apptPet);
                c.Parameters.AddWithValue("@Vet", record.VetId);
                recordId = (int)await c.ExecuteScalarAsync();
            }
            else
            {
                await using var c = Command(@"
UPDATE MedicalRecord SET Weight = @Weight, Temperature = @Temp, Diagnosis = @Diagnosis, Treatment = @Treatment,
       Notes = @Notes, UpdatedAt = SYSDATETIME(), SyncStatus = 'Synced'
WHERE RecordID = @R;");
                AddRecordParameters(c, record);
                c.Parameters.AddWithValue("@R", recordId);
                await c.ExecuteNonQueryAsync();
            }

            // Prescriptions: dispensed ones stay; the rest are replaced by what the form holds.
            await using (var c = Command("DELETE FROM Prescription WHERE RecordID = @R AND IsDispensed = 0;"))
            {
                c.Parameters.AddWithValue("@R", recordId);
                await c.ExecuteNonQueryAsync();
            }
            foreach (var p in prescriptions.Where(p => !p.IsDispensed))
            {
                await using var c = Command(@"
INSERT INTO Prescription (RecordID, MedicineID, Quantity, Dosage, Instructions, SyncStatus)
VALUES (@R, @M, @Q, @Dosage, @Instructions, 'Synced');");
                c.Parameters.AddWithValue("@R", recordId);
                c.Parameters.AddWithValue("@M", p.MedicineId);
                c.Parameters.AddWithValue("@Q", p.Quantity);
                c.Parameters.AddWithValue("@Dosage", (object)p.Dosage ?? DBNull.Value);
                c.Parameters.AddWithValue("@Instructions", (object)p.Instructions ?? DBNull.Value);
                await c.ExecuteNonQueryAsync();
            }

            // Vaccinations given during this visit. Remember what was saved before: on a completed visit
            // those doses were already taken from stock and billed.
            var previousVaccines = new List<int>();
            await using (var c = Command("SELECT MedicineID FROM Vaccination WHERE RecordID = @R AND IsDeleted = 0;"))
            {
                c.Parameters.AddWithValue("@R", recordId);
                await using var r = await c.ExecuteReaderAsync();
                while (await r.ReadAsync()) previousVaccines.Add(r.GetInt32(0));
            }
            await using (var c = Command("DELETE FROM Vaccination WHERE RecordID = @R;"))
            {
                c.Parameters.AddWithValue("@R", recordId);
                await c.ExecuteNonQueryAsync();
            }
            foreach (var v in vaccinations)
            {
                await using var c = Command(@"
INSERT INTO Vaccination (PetID, RecordID, MedicineID, DoseNo, DateGiven, NextDueDate, GivenBy, SyncStatus)
SELECT @Pet, @R, @M, @Dose, CAST(RecordDate AS date), @Next, @Vet, 'Synced' FROM MedicalRecord WHERE RecordID = @R;");
                c.Parameters.AddWithValue("@Pet", apptPet);
                c.Parameters.AddWithValue("@R", recordId);
                c.Parameters.AddWithValue("@M", v.MedicineId);
                c.Parameters.AddWithValue("@Dose", v.DoseNo);
                c.Parameters.Add(new SqlParameter("@Next", SqlDbType.Date) { Value = (object)v.NextDueDate?.Date ?? DBNull.Value });
                c.Parameters.AddWithValue("@Vet", record.VetId);
                await c.ExecuteNonQueryAsync();
            }

            // Vaccination rules 4–6: stock out and bill item. Only once the visit is completed (a draft
            // re-save never deducts twice). Editing a completed record (same day) only charges the difference.
            bool charged = status == AppointmentStatuses.Completed;
            if (complete || charged)
            {
                var toGive = vaccinations.ToList();
                var toReturn = new List<int>();
                if (charged)
                {
                    foreach (int medicineId in previousVaccines)
                    {
                        int match = toGive.FindIndex(v => v.MedicineId == medicineId);
                        if (match >= 0) toGive.RemoveAt(match);          // unchanged dose
                        else toReturn.Add(medicineId);                     // removed or swapped
                    }
                }

                foreach (int medicineId in toReturn)
                    await VisitCharges.ReturnVaccineAsync(connection, tx, clinicId, record.AppointmentId, recordId, medicineId, record.VetId);
                foreach (var v in toGive)
                {
                    if (!await VisitCharges.GiveVaccineAsync(connection, tx, clinicId, record.AppointmentId, recordId, v.MedicineId, v.DoseNo, record.VetId))
                    {
                        await tx.RollbackAsync();
                        return (RecordSaveResult.OutOfStock, recordId, v.VaccineName);
                    }
                }
            }

            // Rule 7: the pet's latest weight.
            if (record.Weight != null)
            {
                await using var c = Command("UPDATE Pet SET Weight = @Weight, UpdatedAt = GETDATE() WHERE PetID = @Pet;");
                c.Parameters.Add(new SqlParameter("@Weight", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = record.Weight.Value });
                c.Parameters.AddWithValue("@Pet", apptPet);
                await c.ExecuteNonQueryAsync();
            }

            if (complete)
            {
                await using var c = Command(@"
UPDATE Appointment SET Status = 'Completed', CompletedAt = GETDATE(), UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE AppointmentID = @A AND Status = 'In Consultation';");
                c.Parameters.AddWithValue("@A", record.AppointmentId);
                if (await c.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    return (RecordSaveResult.NotAllowed, recordId, null);
                }
            }

            await tx.CommitAsync();
            record.RecordId = recordId;
            return (RecordSaveResult.Saved, recordId, null);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Rule 11: a correction added to a locked record.</summary>
    public async Task AddNoteAsync(int recordId, int vetId, string text)
    {
        const string sql = "INSERT INTO MedicalRecordNote (RecordID, VetID, NoteText) VALUES (@R, @V, @T);";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@R", recordId);
        command.Parameters.AddWithValue("@V", vetId);
        command.Parameters.AddWithValue("@T", text);
        await command.ExecuteNonQueryAsync();
    }

    // Helpers ------------------------------------------------------------------------------------

    private async Task<MedicalRecord> GetOneAsync(string condition, int clinicId, int id)
    {
        string sql = "SELECT " + SelectColumns + FromJoins + " WHERE " + condition + " AND a.ClinicID = @ClinicID AND r.IsDeleted = 0;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", id);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    private static void AddRecordParameters(SqlCommand c, MedicalRecord record)
    {
        c.Parameters.Add(new SqlParameter("@Weight", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object)record.Weight ?? DBNull.Value });
        c.Parameters.Add(new SqlParameter("@Temp", SqlDbType.Decimal) { Precision = 4, Scale = 1, Value = (object)record.Temperature ?? DBNull.Value });
        c.Parameters.Add(new SqlParameter("@Diagnosis", SqlDbType.VarChar, -1) { Value = record.Diagnosis });
        c.Parameters.Add(new SqlParameter("@Treatment", SqlDbType.VarChar, -1) { Value = record.Treatment });
        c.Parameters.Add(new SqlParameter("@Notes", SqlDbType.VarChar, -1) { Value = (object)record.Notes ?? DBNull.Value });
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static MedicalRecord Read(SqlDataReader reader)
    {
        string Text(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }
        decimal? Dec(string column)
        {
            int i = reader.GetOrdinal(column);
            return reader.IsDBNull(i) ? null : reader.GetDecimal(i);
        }
        int birth = reader.GetOrdinal("PetBirthDate");

        return new MedicalRecord
        {
            RecordId = reader.GetInt32(reader.GetOrdinal("RecordID")),
            AppointmentId = reader.GetInt32(reader.GetOrdinal("AppointmentID")),
            PetId = reader.GetInt32(reader.GetOrdinal("PetID")),
            VetId = reader.GetInt32(reader.GetOrdinal("VetID")),
            Weight = Dec("Weight"),
            Temperature = Dec("Temperature"),
            Diagnosis = Text("Diagnosis"),
            Treatment = Text("Treatment"),
            Notes = Text("Notes"),
            RecordDate = reader.GetDateTime(reader.GetOrdinal("RecordDate")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            SyncStatus = Text("SyncStatus"),
            PetName = Text("PetName"),
            Species = Text("Species"),
            Breed = Text("Breed"),
            PetSex = Text("PetSex"),
            PetBirthDate = reader.IsDBNull(birth) ? null : reader.GetDateTime(birth),
            OwnerId = reader.GetInt32(reader.GetOrdinal("OwnerID")),
            OwnerFirstName = Text("OwnerFirstName"),
            OwnerLastName = Text("OwnerLastName"),
            OwnerContactNo = Text("OwnerContactNo"),
            VetFirstName = Text("VetFirstName"),
            VetLastName = Text("VetLastName"),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            AppointmentTime = reader.GetTimeSpan(reader.GetOrdinal("AppointmentTime")),
            Reason = Text("Reason"),
            AppointmentStatus = Text("AppointmentStatus"),
            PrescriptionCount = reader.GetInt32(reader.GetOrdinal("PrescriptionCount")),
            VaccinationCount = reader.GetInt32(reader.GetOrdinal("VaccinationCount"))
        };
    }
}
