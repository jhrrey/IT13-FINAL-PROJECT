using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;

namespace VetCare.Models.Repositories;

/// <summary>A consultation on the pet's medical-history timeline.</summary>
public sealed record PetVisit(int RecordId, DateTime Date, string VetName, string Diagnosis, string Treatment,
    decimal? Weight, decimal? Temperature);

/// <summary>A vaccine given to the pet (filled once Vaccinations exists).</summary>
public sealed record PetVaccination(string Vaccine, int DoseNo, DateTime DateGiven, DateTime? NextDue, string GivenBy);

/// <summary>An appointment of the pet.</summary>
public sealed record PetAppointment(DateTime When, string VetName, string Reason, string Status);

/// <summary>A medicine prescribed to the pet (filled once Prescriptions exists).</summary>
public sealed record PetPrescription(DateTime Date, string Medicine, string Dosage, int Quantity, bool Dispensed);

/// <summary>One point of the weight trend: a visit date and the weight recorded then.</summary>
public sealed record PetWeight(DateTime Date, decimal Weight);

/// <summary>The quick stats on the pet profile.</summary>
public sealed record PetStats(DateTime? LastVisit, DateTime? NextAppointment, DateTime? NextVaccineDue, int TotalVisits)
{
    public bool HasUpcomingAppointment => NextAppointment != null;
}

/// <summary>
/// Everything the Pets module shows from OTHER tables. Appointments and medical records are real
/// (Appointment, MedicalRecord); vaccinations and prescriptions return empty results until those
/// tables exist. Every query reaches the clinic through Pet → Owner.ClinicID.
///
/// Rule 6 of the Pets module (a vet's new consultation weight updates Pet.Weight) belongs to the
/// Medical records module.
/// </summary>
public sealed class PetLinksRepository
{
    private const string PetInClinic = @"
JOIN Pet p ON p.PetID = {0}.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID AND o.ClinicID = @ClinicID";

    public bool MedicalRecordsAvailable => true;
    public bool AppointmentsAvailable => true;
    public bool VaccinationsAvailable => true;
    public bool PrescriptionsAvailable => true;

    private readonly VaccinationRepository _vaccinations = new();

    /// <summary>Pets of the clinic with a vaccine due this calendar month (summary card).</summary>
    public Task<int> GetVaccinesDueThisMonthAsync(int clinicId) => _vaccinations.CountDueThisMonthAsync(clinicId, DateTime.Today);

    /// <summary>PetID → next vaccine due date and last visit, for the cards and table of one page.</summary>
    public async Task<IDictionary<int, (DateTime? NextVaccineDue, DateTime? LastVisit)>> GetListInfoAsync(
        int clinicId, IReadOnlyCollection<int> petIds)
    {
        var result = new Dictionary<int, (DateTime?, DateTime?)>();
        if (petIds.Count == 0) return result;

        var names = petIds.Select((id, i) => "@P" + i).ToList();
        string sql = @"
SELECT a.PetID, MAX(a.AppointmentDate)
FROM Appointment a
WHERE a.ClinicID = @ClinicID AND a.IsDeleted = 0 AND a.Status = 'Completed'
  AND a.PetID IN (" + string.Join(", ", names) + @")
GROUP BY a.PetID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        int index = 0;
        foreach (int id in petIds) command.Parameters.AddWithValue(names[index++], id);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result[reader.GetInt32(0)] = (null, reader.GetDateTime(1));
        await reader.CloseAsync();

        // Next vaccine due (latest dose of each vaccine).
        var due = await _vaccinations.GetNextDueByPetsAsync(clinicId, petIds);
        foreach (var (petId, nextDue) in due)
            result[petId] = (nextDue, result.TryGetValue(petId, out var info) ? info.Item2 : null);
        return result;
    }

    /// <summary>Last visit and total visits (completed appointments), and the next active appointment.</summary>
    public async Task<PetStats> GetStatsAsync(int clinicId, int petId)
    {
        const string sql = @"
SELECT MAX(CASE WHEN Status = 'Completed' THEN AppointmentDate END),
       SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END),
       MIN(CASE WHEN Status IN ('Scheduled', 'Checked In', 'In Consultation') AND AppointmentDate >= @Now
                THEN CAST(AppointmentDate AS datetime) + CAST(AppointmentTime AS datetime) END)
FROM Appointment
WHERE ClinicID = @ClinicID AND PetID = @PetID AND IsDeleted = 0;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);
        command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTime) { Value = TodayStart() });

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        DateTime? lastVisit = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
        int visits = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        DateTime? next = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        await reader.CloseAsync();
        var due = await _vaccinations.GetNextDueByPetsAsync(clinicId, new[] { petId });
        return new PetStats(lastVisit, next, due.TryGetValue(petId, out var nextDue) ? nextDue : null, visits);
    }

    /// <summary>Consultations, newest first.</summary>
    public async Task<IReadOnlyList<PetVisit>> GetMedicalHistoryAsync(int clinicId, int petId)
    {
        string sql = @"
SELECT m.RecordID, COALESCE(m.RecordDate, m.CreatedAt), v.FirstName, v.LastName, m.Diagnosis, m.Treatment, m.Weight, m.Temperature
FROM MedicalRecord m
" + string.Format(PetInClinic, "m") + @"
JOIN [User] v ON v.UserID = m.VetID
WHERE m.PetID = @PetID AND m.IsDeleted = 0
ORDER BY COALESCE(m.RecordDate, m.CreatedAt) DESC, m.RecordID DESC;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);

        var visits = new List<PetVisit>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            visits.Add(new PetVisit(
                reader.GetInt32(0),
                reader.GetDateTime(1),
                "Dr. " + Helpers.PersonName.Join(reader.GetString(2), reader.GetString(3)),
                reader.IsDBNull(4) ? "Consultation" : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7)));
        }
        return visits;
    }

    /// <summary>Vaccines given to the pet, newest first.</summary>
    public async Task<IReadOnlyList<PetVaccination>> GetVaccinationsAsync(int clinicId, int petId) =>
        (await _vaccinations.GetByPetAsync(clinicId, petId))
            .Select(v => new PetVaccination(v.VaccineName, v.DoseNo, v.DateGiven, v.NextDueDate, v.GivenByName))
            .ToList();

    /// <summary>The pet's appointments, newest first.</summary>
    public async Task<IReadOnlyList<PetAppointment>> GetAppointmentsAsync(int clinicId, int petId)
    {
        const string sql = @"
SELECT a.AppointmentDate, a.AppointmentTime, v.FirstName, v.LastName, a.Reason, a.Status
FROM Appointment a
JOIN [User] v ON v.UserID = a.VetID
WHERE a.ClinicID = @ClinicID AND a.PetID = @PetID AND a.IsDeleted = 0
ORDER BY a.AppointmentDate DESC, a.AppointmentTime DESC;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);

        var list = new List<PetAppointment>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new PetAppointment(
                reader.GetDateTime(0) + reader.GetTimeSpan(1),
                "Dr. " + Helpers.PersonName.Join(reader.GetString(2), reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5)));
        return list;
    }

    /// <summary>Medicines prescribed to the pet, newest first.</summary>
    public async Task<IReadOnlyList<PetPrescription>> GetPrescriptionsAsync(int clinicId, int petId)
    {
        const string sql = @"
SELECT r.RecordDate, m.MedicineName, x.Dosage, x.Quantity, x.IsDispensed
FROM Prescription x
JOIN MedicalRecord r ON r.RecordID = x.RecordID
JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @ClinicID
JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE r.PetID = @PetID AND x.IsDeleted = 0 AND r.IsDeleted = 0
ORDER BY r.RecordDate DESC, x.PrescriptionID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);

        var list = new List<PetPrescription>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new PetPrescription(reader.GetDateTime(0), reader.GetString(1),
                reader.IsDBNull(2) ? "—" : reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4)));
        return list;
    }

    /// <summary>Weights from consultations (MedicalRecord.Weight), oldest first.</summary>
    public async Task<IReadOnlyList<PetWeight>> GetWeightHistoryAsync(int clinicId, int petId)
    {
        string sql = @"
SELECT COALESCE(m.RecordDate, m.CreatedAt), m.Weight
FROM MedicalRecord m
" + string.Format(PetInClinic, "m") + @"
WHERE m.PetID = @PetID AND m.IsDeleted = 0 AND m.Weight IS NOT NULL
ORDER BY COALESCE(m.RecordDate, m.CreatedAt), m.RecordID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@PetID", petId);

        var weights = new List<PetWeight>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) weights.Add(new PetWeight(reader.GetDateTime(0), reader.GetDecimal(1)));
        return weights;
    }

    /// <summary>
    /// "Upcoming" counts from the start of today, so a visit later today (or one still in progress)
    /// blocks archiving.
    /// </summary>
    private static DateTime TodayStart() => DateTime.Today;
}
