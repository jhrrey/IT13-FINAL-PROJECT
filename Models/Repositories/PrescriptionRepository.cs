using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>A prescription with what the Prescriptions page, the dispense dialog and the label show.</summary>
public sealed class PharmacyPrescription
{
    public int PrescriptionId { get; set; }
    public int RecordId { get; set; }
    public int AppointmentId { get; set; }
    public DateTime RecordDate { get; set; }
    public int PetId { get; set; }
    public string PetName { get; set; }
    public string Species { get; set; }
    public int OwnerId { get; set; }
    public string OwnerName { get; set; }
    public string OwnerContactNo { get; set; }
    public int VetId { get; set; }
    public string VetName { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; }
    public string Unit { get; set; }
    public int StockQty { get; set; }
    public int ReorderLevel { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public string Dosage { get; set; }
    public string Instructions { get; set; }
    public bool IsDispensed { get; set; }
    public DateTime? DispensedAt { get; set; }
    public string DispensedByName { get; set; }
    public string SyncStatus { get; set; }

    /// <summary>"Dr. Grace Lim".</summary>
    public string VetDisplay => "Dr. " + VetName;

    public decimal Amount => UnitPrice * Quantity;

    /// <summary>Rule 10.</summary>
    public bool IsExpired => ExpiryDate != null && ExpiryDate.Value.Date < DateTime.Today;

    /// <summary>Rule 6: enough stock for the whole quantity.</summary>
    public bool EnoughStock => StockQty >= Quantity;

    public bool CanDispense => !IsDispensed && EnoughStock && !IsExpired;

    /// <summary>"14 Capsule" (the unit name as stored).</summary>
    public string QuantityText => Quantity + " " + Unit;

    /// <summary>Medicine as a stock item, for the status helper.</summary>
    public Medicine AsMedicine() => new()
    {
        MedicineId = MedicineId,
        MedicineName = MedicineName,
        Unit = Unit,
        StockQty = StockQty,
        ReorderLevel = ReorderLevel,
        UnitPrice = UnitPrice,
        ExpiryDate = ExpiryDate
    };
}

public sealed class PrescriptionSummary
{
    public int ToDispense { get; set; }
    public int DispensedToday { get; set; }
    public int WaitingForStock { get; set; }
}

public enum DispenseResult
{
    Saved,
    NotFound,
    AlreadyDispensed,
    NotEnoughStock,
    Expired
}

/// <summary>
/// Prescriptions for the pharmacy: the "To dispense" list (completed visits only, so the vet is no longer
/// editing them), the dispensed history, and dispensing in one SQL transaction (rule 7).
/// Prescriptions have no ClinicID; the clinic comes from the visit (Appointment.ClinicID).
/// </summary>
public sealed class PrescriptionRepository
{
    private const string Select = @"
SELECT x.PrescriptionID, x.RecordID, a.AppointmentID, r.RecordDate, p.PetID, p.PetName, p.Species,
       o.OwnerID, o.FirstName AS OwnerFirst, o.LastName AS OwnerLast, o.ContactNo,
       v.UserID AS VetID, v.FirstName AS VetFirst, v.LastName AS VetLast,
       m.MedicineID, m.MedicineName, m.Unit, m.StockQty, m.ReorderLevel, m.UnitPrice, m.ExpiryDate,
       x.Quantity, x.Dosage, x.Instructions, x.IsDispensed, x.DispensedAt,
       d.FirstName AS ByFirst, d.LastName AS ByLast, x.SyncStatus";

    private const string From = @"
FROM Prescription x
JOIN MedicalRecord r ON r.RecordID = x.RecordID AND r.IsDeleted = 0
JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @ClinicID
JOIN Pet p ON p.PetID = r.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID
JOIN [User] v ON v.UserID = r.VetID
JOIN Medicine m ON m.MedicineID = x.MedicineID
LEFT JOIN [User] d ON d.UserID = x.DispensedBy
WHERE x.IsDeleted = 0 AND a.Status = 'Completed'";

    /// <summary>Waiting prescriptions, oldest visit first, rows of one visit together.</summary>
    public async Task<IReadOnlyList<PharmacyPrescription>> GetToDispenseAsync(int clinicId, string search, int? vetId, DateTime? from, DateTime? to)
    {
        string sql = Select + From + " AND x.IsDispensed = 0" + Filters(search, vetId, from, to, "r.RecordDate") +
                     " ORDER BY r.RecordDate, a.AppointmentID, x.PrescriptionID;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddFilters(command, clinicId, search, vetId, from, to);
        return await ReadAllAsync(command);
    }

    /// <summary>One page of dispensed prescriptions, newest first.</summary>
    public async Task<PagedResult<PharmacyPrescription>> GetDispensedAsync(int clinicId, string search, int? vetId, DateTime? from, DateTime? to, int page, int pageSize = 10)
    {
        page = Math.Max(1, page);
        string filters = " AND x.IsDispensed = 1" + Filters(search, vetId, from, to, "x.DispensedAt");
        string sql = @"
WITH Matches AS (" + Select + ", ROW_NUMBER() OVER (ORDER BY x.DispensedAt DESC, x.PrescriptionID DESC) AS RowNum" + From + filters + @")
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*)" + From + filters + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddFilters(command, clinicId, search, vetId, from, to);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<PharmacyPrescription>();
        int total;
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) list.Add(Read(r));
            await r.NextResultAsync();
            await r.ReadAsync();
            total = r.GetInt32(0);
        }
        return new PagedResult<PharmacyPrescription>(list, total, page, pageSize);
    }

    public async Task<PharmacyPrescription> GetByIdAsync(int clinicId, int prescriptionId)
    {
        string sql = Select + From + " AND x.PrescriptionID = @ID;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@ID", prescriptionId);
        return (await ReadAllAsync(command)).FirstOrDefault();
    }

    public async Task<PrescriptionSummary> GetSummaryAsync(int clinicId, DateTime today)
    {
        const string sql = @"
SELECT SUM(CASE WHEN x.IsDispensed = 0 THEN 1 ELSE 0 END),
       SUM(CASE WHEN x.IsDispensed = 1 AND x.DispensedAt >= @Today AND x.DispensedAt < @Tomorrow THEN 1 ELSE 0 END),
       SUM(CASE WHEN x.IsDispensed = 0 AND (m.StockQty < x.Quantity OR m.ExpiryDate < @Today) THEN 1 ELSE 0 END)
FROM Prescription x
JOIN MedicalRecord r ON r.RecordID = x.RecordID AND r.IsDeleted = 0
JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @ClinicID
JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE x.IsDeleted = 0 AND a.Status = 'Completed';";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.DateTime) { Value = today.Date });
        command.Parameters.Add(new SqlParameter("@Tomorrow", SqlDbType.DateTime) { Value = today.Date.AddDays(1) });
        await using var r = await command.ExecuteReaderAsync();
        await r.ReadAsync();
        int Value(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
        return new PrescriptionSummary { ToDispense = Value(0), DispensedToday = Value(1), WaitingForStock = Value(2) };
    }

    /// <summary>
    /// Rules 6 and 7, in one transaction: checks (not dispensed, enough stock, not expired) under update locks,
    /// marks it dispensed, lowers the stock, logs "Dispensed" (−qty) and adds a Medicine item to the visit's bill.
    /// </summary>
    public async Task<DispenseResult> DispenseAsync(int clinicId, int prescriptionId, int userId)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            int medicineId, quantity, appointmentId, recordId, stock;
            bool dispensed;
            DateTime? expiry;
            decimal price;
            string name, unit;
            await using (var c = new SqlCommand(@"
SELECT x.MedicineID, x.Quantity, x.IsDispensed, a.AppointmentID, x.RecordID, m.StockQty, m.ExpiryDate, m.UnitPrice, m.MedicineName, m.Unit
FROM Prescription x WITH (UPDLOCK)
JOIN MedicalRecord r ON r.RecordID = x.RecordID AND r.IsDeleted = 0
JOIN Appointment a ON a.AppointmentID = r.AppointmentID AND a.ClinicID = @C AND a.Status = 'Completed'
JOIN Medicine m WITH (UPDLOCK) ON m.MedicineID = x.MedicineID AND m.ClinicID = @C
WHERE x.PrescriptionID = @P AND x.IsDeleted = 0;", connection, tx))
            {
                c.Parameters.AddWithValue("@P", prescriptionId);
                c.Parameters.AddWithValue("@C", clinicId);
                await using var r = await c.ExecuteReaderAsync();
                if (!await r.ReadAsync()) { await r.CloseAsync(); await tx.RollbackAsync(); return DispenseResult.NotFound; }
                medicineId = r.GetInt32(0);
                quantity = r.GetInt32(1);
                dispensed = r.GetBoolean(2);
                appointmentId = r.GetInt32(3);
                recordId = r.GetInt32(4);
                stock = r.GetInt32(5);
                expiry = r.IsDBNull(6) ? null : r.GetDateTime(6);
                price = r.GetDecimal(7);
                name = r.GetString(8);
                unit = r.GetString(9);
            }

            DispenseResult? refused = dispensed ? DispenseResult.AlreadyDispensed
                : expiry != null && expiry.Value.Date < DateTime.Today ? DispenseResult.Expired
                : stock < quantity ? DispenseResult.NotEnoughStock
                : null;
            if (refused != null) { await tx.RollbackAsync(); return refused.Value; }

            await using (var c = new SqlCommand(@"
UPDATE Prescription SET IsDispensed = 1, DispensedAt = GETDATE(), DispensedBy = @U, UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE PrescriptionID = @P AND IsDispensed = 0;
UPDATE Medicine SET StockQty = StockQty - @Q, UpdatedAt = GETDATE(), SyncStatus = 'Synced' WHERE MedicineID = @M;", connection, tx))
            {
                c.Parameters.AddWithValue("@U", userId);
                c.Parameters.AddWithValue("@P", prescriptionId);
                c.Parameters.AddWithValue("@Q", quantity);
                c.Parameters.AddWithValue("@M", medicineId);
                await c.ExecuteNonQueryAsync();
            }
            await MedicineRepository.LogAsync(connection, tx, clinicId, medicineId, "Dispensed", -quantity,
                "Prescription #" + prescriptionId, userId, recordId);
            await VisitCharges.AddItemAsync(connection, tx, clinicId, appointmentId, medicineId, "Medicine",
                name + " × " + quantity + " " + unit, quantity, price);

            await tx.CommitAsync();
            return DispenseResult.Saved;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static string Filters(string search, int? vetId, DateTime? from, DateTime? to, string dateColumn)
    {
        string sql = string.Empty;
        if (!string.IsNullOrWhiteSpace(search))
            sql += @" AND (p.PetName LIKE @Search OR m.MedicineName LIKE @Search
                      OR o.FirstName + ' ' + o.LastName LIKE @Search OR o.LastName + ' ' + o.FirstName LIKE @Search)";
        if (vetId != null) sql += " AND r.VetID = @VetID";
        if (from != null) sql += " AND " + dateColumn + " >= @From";
        if (to != null) sql += " AND " + dateColumn + " < @To";
        return sql;
    }

    private static void AddFilters(SqlCommand command, int clinicId, string search, int? vetId, DateTime? from, DateTime? to)
    {
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        if (!string.IsNullOrWhiteSpace(search))
            command.Parameters.AddWithValue("@Search", "%" + search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");
        if (vetId != null) command.Parameters.AddWithValue("@VetID", vetId.Value);
        if (from != null) command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = from.Value.Date });
        if (to != null) command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = to.Value.Date.AddDays(1) });
    }

    private static async Task<IReadOnlyList<PharmacyPrescription>> ReadAllAsync(SqlCommand command)
    {
        var list = new List<PharmacyPrescription>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r));
        return list;
    }

    private static PharmacyPrescription Read(SqlDataReader r)
    {
        string Text(string column) { int i = r.GetOrdinal(column); return r.IsDBNull(i) ? null : r.GetString(i); }
        DateTime? Date(string column) { int i = r.GetOrdinal(column); return r.IsDBNull(i) ? null : r.GetDateTime(i); }
        string byFirst = Text("ByFirst");
        return new PharmacyPrescription
        {
            PrescriptionId = r.GetInt32(r.GetOrdinal("PrescriptionID")),
            RecordId = r.GetInt32(r.GetOrdinal("RecordID")),
            AppointmentId = r.GetInt32(r.GetOrdinal("AppointmentID")),
            RecordDate = r.GetDateTime(r.GetOrdinal("RecordDate")),
            PetId = r.GetInt32(r.GetOrdinal("PetID")),
            PetName = Text("PetName"),
            Species = Text("Species"),
            OwnerId = r.GetInt32(r.GetOrdinal("OwnerID")),
            OwnerName = Helpers.PersonName.Join(Text("OwnerFirst"), Text("OwnerLast")),
            OwnerContactNo = Text("ContactNo"),
            VetId = r.GetInt32(r.GetOrdinal("VetID")),
            VetName = Helpers.PersonName.Join(Text("VetFirst"), Text("VetLast")),
            MedicineId = r.GetInt32(r.GetOrdinal("MedicineID")),
            MedicineName = Text("MedicineName"),
            Unit = Text("Unit"),
            StockQty = r.GetInt32(r.GetOrdinal("StockQty")),
            ReorderLevel = r.GetInt32(r.GetOrdinal("ReorderLevel")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("UnitPrice")),
            ExpiryDate = Date("ExpiryDate"),
            Quantity = r.GetInt32(r.GetOrdinal("Quantity")),
            Dosage = Text("Dosage"),
            Instructions = Text("Instructions"),
            IsDispensed = r.GetBoolean(r.GetOrdinal("IsDispensed")),
            DispensedAt = Date("DispensedAt"),
            DispensedByName = byFirst == null ? null : Helpers.PersonName.Join(byFirst, Text("ByLast")),
            SyncStatus = Text("SyncStatus")
        };
    }
}
