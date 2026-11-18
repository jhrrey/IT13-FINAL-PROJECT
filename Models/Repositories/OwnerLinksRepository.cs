using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;

namespace VetCare.Models.Repositories;

/// <summary>A pet shown on the owner's profile.</summary>
public sealed record OwnerPet(int PetId, string Name, string Species, string Breed, string Age, DateTime? NextVaccineDue);

/// <summary>An appointment of one of the owner's pets.</summary>
public sealed record OwnerAppointment(DateTime When, string PetName, string VetName, string Reason, string Status);

/// <summary>A bill of the owner (Billing module): Bill no., date, pet, total, paid, status.</summary>
public sealed record OwnerBill(int BillId, string BillNo, DateTime BillDate, string PetName, decimal Amount, decimal Paid, string Status)
{
    public decimal Balance => Status == "Void" ? 0 : Math.Max(0, Amount - Paid);
}

/// <summary>The quick stats on the owner profile.</summary>
public sealed record OwnerStats(int PetCount, DateTime? NextAppointment, decimal UnpaidBalance)
{
    public bool HasUnpaidBalance => UnpaidBalance > 0;
    public bool HasUpcomingAppointment => NextAppointment != null;
}

/// <summary>
/// Everything the Owners module shows from OTHER tables: pets, appointments and bills.
///
/// Always filtered by the clinic. The unpaid balance (Unpaid and Partial bills) drives the profile's stat chip,
/// the "With unpaid balance" card and the archive rule (owner rule 7, billing rule 15).
/// </summary>
public sealed class OwnerLinksRepository
{
    private readonly PetRepository _pets;
    private readonly PetLinksRepository _petLinks;

    public OwnerLinksRepository(PetRepository pets, PetLinksRepository petLinks)
    {
        _pets = pets;
        _petLinks = petLinks;
    }

    public bool PetsAvailable => true;
    public bool AppointmentsAvailable => true;
    public bool BillsAvailable => true;

    /// <summary>Pet count and up to two pet names per owner, for the list's Pets column.</summary>
    public async Task<IDictionary<int, (int Count, IReadOnlyList<string> Names)>> GetPetSummariesAsync(
        int clinicId, IReadOnlyCollection<int> ownerIds)
    {
        var names = await _pets.GetNamesByOwnersAsync(clinicId, ownerIds);
        return names.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Count, (IReadOnlyList<string>)pair.Value.Take(2).ToList()));
    }

    /// <summary>Owners of the clinic with an upcoming appointment / an unpaid balance (summary cards).</summary>
    public async Task<(int WithUpcomingAppointments, int WithUnpaidBalance)> GetClinicCountsAsync(int clinicId)
    {
        const string sql = @"
SELECT COUNT(DISTINCT o.OwnerID)
FROM Appointment a
JOIN Pet p ON p.PetID = a.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID
WHERE a.ClinicID = @ClinicID AND o.ClinicID = @ClinicID AND a.IsDeleted = 0 AND o.IsDeleted = 0
  AND a.Status IN ('Scheduled', 'Checked In', 'In Consultation') AND a.AppointmentDate >= @Today;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = DateTime.Today });
        int upcoming = (int)await command.ExecuteScalarAsync();

        await using var unpaid = new SqlCommand(@"
SELECT COUNT(DISTINCT b.OwnerID) FROM Bill b JOIN Owner o ON o.OwnerID = b.OwnerID
WHERE b.ClinicID = @ClinicID AND b.IsDeleted = 0 AND o.IsDeleted = 0 AND b.Status IN ('Unpaid', 'Partial');", connection);
        unpaid.Parameters.AddWithValue("@ClinicID", clinicId);
        return (upcoming, (int)await unpaid.ExecuteScalarAsync());
    }

    public async Task<OwnerStats> GetStatsAsync(int clinicId, int ownerId)
    {
        var pets = await _pets.GetByOwnerAsync(clinicId, ownerId);

        const string sql = @"
SELECT MIN(CAST(a.AppointmentDate AS datetime) + CAST(a.AppointmentTime AS datetime))
FROM Appointment a
JOIN Pet p ON p.PetID = a.PetID
WHERE a.ClinicID = @ClinicID AND p.OwnerID = @OwnerID AND a.IsDeleted = 0
  AND a.Status IN ('Scheduled', 'Checked In', 'In Consultation') AND a.AppointmentDate >= @Today;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@OwnerID", ownerId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = DateTime.Today });
        object next = await command.ExecuteScalarAsync();

        await using var balance = new SqlCommand(@"
SELECT ISNULL(SUM(x.Balance), 0) FROM (
    SELECT b.TotalAmount - ISNULL((SELECT SUM(pm.AmountPaid) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0), 0) AS Balance
    FROM Bill b WHERE b.ClinicID = @ClinicID AND b.OwnerID = @OwnerID AND b.IsDeleted = 0 AND b.Status IN ('Unpaid', 'Partial')) x;", connection);
        balance.Parameters.AddWithValue("@ClinicID", clinicId);
        balance.Parameters.AddWithValue("@OwnerID", ownerId);
        decimal unpaidBalance = (decimal)await balance.ExecuteScalarAsync();

        return new OwnerStats(pets.Count, next is DateTime when ? when : null, unpaidBalance);
    }

    public async Task<IReadOnlyList<OwnerPet>> GetPetsAsync(int clinicId, int ownerId)
    {
        var pets = await _pets.GetByOwnerAsync(clinicId, ownerId);
        var info = await _petLinks.GetListInfoAsync(clinicId, pets.Select(p => p.PetId).ToList());
        return pets.Select(p => new OwnerPet(p.PetId, p.PetName, p.Species, p.Breed, p.AgeText,
            info.TryGetValue(p.PetId, out var i) ? i.NextVaccineDue : null)).ToList();
    }

    /// <summary>Appointments of all the owner's pets, newest first.</summary>
    public async Task<IReadOnlyList<OwnerAppointment>> GetAppointmentsAsync(int clinicId, int ownerId)
    {
        const string sql = @"
SELECT a.AppointmentDate, a.AppointmentTime, p.PetName, v.FirstName, v.LastName, a.Reason, a.Status
FROM Appointment a
JOIN Pet p ON p.PetID = a.PetID
JOIN [User] v ON v.UserID = a.VetID
WHERE a.ClinicID = @ClinicID AND p.OwnerID = @OwnerID AND a.IsDeleted = 0
ORDER BY a.AppointmentDate DESC, a.AppointmentTime DESC;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@OwnerID", ownerId);

        var list = new List<OwnerAppointment>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new OwnerAppointment(
                reader.GetDateTime(0) + reader.GetTimeSpan(1),
                reader.GetString(2),
                "Dr. " + Helpers.PersonName.Join(reader.GetString(3), reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6)));
        return list;
    }

    /// <summary>The owner's bills, newest first.</summary>
    public async Task<IReadOnlyList<OwnerBill>> GetBillsAsync(int clinicId, int ownerId) =>
        (await new BillingRepository().GetOwnerBillsAsync(clinicId, ownerId))
        .Select(b => new OwnerBill(b.BillId, b.NumberText, b.BillDate, b.PetName, b.TotalAmount, b.Paid, b.Status)).ToList();
}
