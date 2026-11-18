using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>A completed visit with no bill yet (the "Ready to bill" tab).</summary>
public sealed class ReadyToBillVisit
{
    public int AppointmentId { get; set; }
    public DateTime VisitDate { get; set; }
    public TimeSpan VisitTime { get; set; }
    public string PetName { get; set; }
    public string Species { get; set; }
    public string OwnerName { get; set; }
    public string VetName { get; set; }
    public string Reason { get; set; }
    public int Vaccines { get; set; }
    public int Medicines { get; set; }

    /// <summary>"Consultation, 1 vaccine, 2 medicines".</summary>
    public string ItemsText
    {
        get
        {
            var parts = new List<string> { "Consultation" };
            if (Vaccines > 0) parts.Add(Vaccines + (Vaccines == 1 ? " vaccine" : " vaccines"));
            if (Medicines > 0) parts.Add(Medicines + (Medicines == 1 ? " medicine" : " medicines"));
            return string.Join(", ", parts);
        }
    }
}

/// <summary>What the Create bill page starts with: the visit, its vaccines and dispensed medicines, and the fee.</summary>
public sealed class BillDraft
{
    public int AppointmentId { get; set; }
    public string Status { get; set; }
    public DateTime VisitDate { get; set; }
    public TimeSpan VisitTime { get; set; }
    public string Reason { get; set; }
    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public string OwnerName { get; set; }
    public string OwnerContactNo { get; set; }
    public string VetName { get; set; }
    public decimal ConsultationFee { get; set; }

    /// <summary>Vaccine and Medicine items (read-only on the page).</summary>
    public List<BillItem> FixedItems { get; set; } = new();

    /// <summary>Prescriptions of the visit not dispensed yet (added to the bill when dispensed, rule 7).</summary>
    public int NotDispensed { get; set; }

    /// <summary>The visit's bill if it already has one (not Void).</summary>
    public int? ExistingBillId { get; set; }
}

public sealed class BillQuery
{
    public int ClinicId { get; set; }
    public string Search { get; set; }
    public string Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class PaymentQuery
{
    public int ClinicId { get; set; }
    public string Method { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class BillingSummary
{
    public decimal CollectedToday { get; set; }
    public int Unpaid { get; set; }
    public int Partial { get; set; }
    public decimal Outstanding { get; set; }
    public int ReadyToBill { get; set; }
}

public enum CreateBillResult { Created, AlreadyBilled, NotCompleted }

public enum BillChangeResult { Saved, NotFound, Locked }

public enum PaymentResult { Saved, NotFound, Locked, OverBalance }

/// <summary>
/// All SQL for Bill, BillItem and Payment. Every query is clinic-scoped. Totals and statuses are recomputed in
/// SQL (VisitCharges.RecomputeAsync) after every change (rules 4 and 11); payments are only ever inserted (rule 13).
/// </summary>
public sealed class BillingRepository
{
    private const string BillColumns = @"
b.BillID, b.ClinicID, b.AppointmentID, b.OwnerID, b.CashierID, b.BillNo, b.TotalAmount, b.Status, b.BillDate, b.Remarks, b.SyncStatus,
(SELECT ISNULL(SUM(pm.AmountPaid), 0) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0) AS Paid,
o.FirstName AS OwnerFirst, o.LastName AS OwnerLast, o.ContactNo,
p.PetID, p.PetName, p.Species, p.Breed,
v.FirstName AS VetFirst, v.LastName AS VetLast,
c.FirstName AS CashierFirst, c.LastName AS CashierLast,
a.AppointmentDate, a.AppointmentTime, a.Reason";

    private const string BillJoins = @"
FROM Bill b
JOIN Appointment a ON a.AppointmentID = b.AppointmentID
JOIN Owner o ON o.OwnerID = b.OwnerID
LEFT JOIN Pet p ON p.PetID = a.PetID
JOIN [User] v ON v.UserID = a.VetID
LEFT JOIN [User] c ON c.UserID = b.CashierID";

    // Ready to bill / prepare ----------------------------------------------------------------------------------

    /// <summary>Rule 3: completed visits with no bill (other than a voided one), oldest first.</summary>
    public async Task<IReadOnlyList<ReadyToBillVisit>> GetReadyToBillAsync(int clinicId, string search)
    {
        string sql = @"
SELECT a.AppointmentID, a.AppointmentDate, a.AppointmentTime, p.PetName, p.Species, o.FirstName, o.LastName,
       v.FirstName, v.LastName, a.Reason,
       (SELECT COUNT(*) FROM Vaccination x JOIN MedicalRecord r ON r.RecordID = x.RecordID
        WHERE r.AppointmentID = a.AppointmentID AND r.IsDeleted = 0 AND x.IsDeleted = 0),
       (SELECT COUNT(*) FROM Prescription x JOIN MedicalRecord r ON r.RecordID = x.RecordID
        WHERE r.AppointmentID = a.AppointmentID AND r.IsDeleted = 0 AND x.IsDeleted = 0 AND x.IsDispensed = 1)
FROM Appointment a
JOIN Pet p ON p.PetID = a.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID
JOIN [User] v ON v.UserID = a.VetID
WHERE a.ClinicID = @ClinicID AND a.IsDeleted = 0 AND a.Status = 'Completed'
  AND NOT EXISTS (SELECT 1 FROM Bill b WHERE b.AppointmentID = a.AppointmentID AND b.IsDeleted = 0 AND b.Status <> 'Void')" +
            (string.IsNullOrWhiteSpace(search) ? string.Empty : @"
  AND (p.PetName LIKE @Search OR o.FirstName + ' ' + o.LastName LIKE @Search OR o.LastName + ' ' + o.FirstName LIKE @Search)") + @"
ORDER BY a.AppointmentDate, a.AppointmentTime, a.AppointmentID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        if (!string.IsNullOrWhiteSpace(search)) command.Parameters.AddWithValue("@Search", Like(search));
        var list = new List<ReadyToBillVisit>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ReadyToBillVisit
            {
                AppointmentId = r.GetInt32(0),
                VisitDate = r.GetDateTime(1),
                VisitTime = r.GetTimeSpan(2),
                PetName = r.GetString(3),
                Species = r.GetString(4),
                OwnerName = PersonName.Join(r.GetString(5), r.GetString(6)),
                VetName = "Dr. " + PersonName.Join(r.GetString(7), r.GetString(8)),
                Reason = r.GetString(9),
                Vaccines = r.GetInt32(10),
                Medicines = r.GetInt32(11)
            });
        }
        return list;
    }

    /// <summary>The visit and what it used (section 3.B). Null if it isn't a visit of the clinic.</summary>
    public async Task<BillDraft> PrepareAsync(int clinicId, int appointmentId)
    {
        const string sql = @"
SELECT a.AppointmentID, a.Status, a.AppointmentDate, a.AppointmentTime, a.Reason, p.PetName, p.Species, p.Breed,
       o.FirstName, o.LastName, o.ContactNo, v.FirstName, v.LastName, cl.ConsultationFee,
       (SELECT TOP 1 b.BillID FROM Bill b WHERE b.AppointmentID = a.AppointmentID AND b.IsDeleted = 0 AND b.Status <> 'Void' ORDER BY b.BillID),
       (SELECT COUNT(*) FROM Prescription x JOIN MedicalRecord r ON r.RecordID = x.RecordID
        WHERE r.AppointmentID = a.AppointmentID AND r.IsDeleted = 0 AND x.IsDeleted = 0 AND x.IsDispensed = 0)
FROM Appointment a
JOIN Pet p ON p.PetID = a.PetID
JOIN Owner o ON o.OwnerID = p.OwnerID
JOIN [User] v ON v.UserID = a.VetID
JOIN Clinic cl ON cl.ClinicID = a.ClinicID
WHERE a.AppointmentID = @A AND a.ClinicID = @ClinicID AND a.IsDeleted = 0;

" + FixedItemsSql + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@A", appointmentId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        await using var r = await command.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var draft = new BillDraft
        {
            AppointmentId = r.GetInt32(0),
            Status = r.GetString(1),
            VisitDate = r.GetDateTime(2),
            VisitTime = r.GetTimeSpan(3),
            Reason = r.GetString(4),
            PetName = r.GetString(5),
            Species = r.GetString(6),
            Breed = r.IsDBNull(7) ? null : r.GetString(7),
            OwnerName = PersonName.Join(r.GetString(8), r.GetString(9)),
            OwnerContactNo = r.GetString(10),
            VetName = "Dr. " + PersonName.Join(r.GetString(11), r.GetString(12)),
            // System settings → Clinic.ConsultationFee (this clinic's own value, else the default); the old column is the fallback.
            ConsultationFee = Helpers.SettingsCache.ClinicId == clinicId ? Helpers.SettingsCache.GetMoney(SettingKeys.ConsultationFee) : r.GetDecimal(13),
            ExistingBillId = r.IsDBNull(14) ? null : r.GetInt32(14),
            NotDispensed = r.GetInt32(15)
        };
        await r.NextResultAsync();
        while (await r.ReadAsync())
            draft.FixedItems.Add(new BillItem { MedicineId = r.GetInt32(0), ItemType = r.GetString(1), Description = r.GetString(2), Quantity = r.GetInt32(3), UnitPrice = r.GetDecimal(4) });
        return draft;
    }

    /// <summary>Rule 6: the visit's vaccines and dispensed medicines, priced from the stock list.</summary>
    private const string FixedItemsSql = @"
SELECT m.MedicineID AS MedicineID, 'Vaccine' AS ItemType, LEFT(m.MedicineName + ' – Dose ' + CAST(x.DoseNo AS varchar(5)), 100) AS Description,
       1 AS Quantity, m.UnitPrice AS UnitPrice
FROM Vaccination x JOIN MedicalRecord r ON r.RecordID = x.RecordID JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE r.AppointmentID = @A AND r.IsDeleted = 0 AND x.IsDeleted = 0
UNION ALL
SELECT m.MedicineID, 'Medicine', LEFT(m.MedicineName + ' × ' + CAST(x.Quantity AS varchar(10)) + ' ' + m.Unit, 100), x.Quantity, m.UnitPrice
FROM Prescription x JOIN MedicalRecord r ON r.RecordID = x.RecordID JOIN Medicine m ON m.MedicineID = x.MedicineID
WHERE r.AppointmentID = @A AND r.IsDeleted = 0 AND x.IsDeleted = 0 AND x.IsDispensed = 1";

    // Create -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Rules 3, 4 and 6, in one transaction: the visit must be Completed and have no bill (checked under lock);
    /// gets the next BillNo of the year; saves the consultation / service items given and the vaccine and medicine
    /// items read from the database (so the bill matches the stock used); then the total.
    /// </summary>
    public async Task<(CreateBillResult Result, int BillId)> CreateAsync(int clinicId, int appointmentId, IReadOnlyList<BillItem> editableItems, int userId, DateTime now)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            int ownerId;
            await using (var c = new SqlCommand(@"
SELECT p.OwnerID FROM Appointment a WITH (UPDLOCK) JOIN Pet p ON p.PetID = a.PetID
WHERE a.AppointmentID = @A AND a.ClinicID = @C AND a.IsDeleted = 0 AND a.Status = 'Completed';", connection, tx))
            {
                c.Parameters.AddWithValue("@A", appointmentId);
                c.Parameters.AddWithValue("@C", clinicId);
                if (await c.ExecuteScalarAsync() is not int o) { await tx.RollbackAsync(); return (CreateBillResult.NotCompleted, 0); }
                ownerId = o;
            }
            await using (var c = new SqlCommand("SELECT COUNT(*) FROM Bill WITH (UPDLOCK, HOLDLOCK) WHERE AppointmentID = @A AND IsDeleted = 0 AND Status <> 'Void';", connection, tx))
            {
                c.Parameters.AddWithValue("@A", appointmentId);
                if ((int)await c.ExecuteScalarAsync() > 0) { await tx.RollbackAsync(); return (CreateBillResult.AlreadyBilled, 0); }
            }

            // Next number of the year for this clinic (locked so two cashiers can't get the same one).
            string prefix = BillNumberHelper.YearPrefix(now.Year);
            int next;
            await using (var c = new SqlCommand(@"
SELECT ISNULL(MAX(CAST(SUBSTRING(BillNo, LEN(@Prefix) + 1, 10) AS int)), 0) + 1
FROM Bill WITH (UPDLOCK, HOLDLOCK) WHERE ClinicID = @C AND BillNo LIKE @Like;", connection, tx))
            {
                c.Parameters.AddWithValue("@Prefix", prefix);
                c.Parameters.AddWithValue("@Like", prefix + "%");
                c.Parameters.AddWithValue("@C", clinicId);
                next = (int)await c.ExecuteScalarAsync();
            }

            int billId;
            await using (var c = new SqlCommand(@"
INSERT INTO Bill (ClinicID, AppointmentID, OwnerID, CashierID, BillNo, TotalAmount, Status, BillDate, SyncStatus)
VALUES (@C, @A, @O, @U, @No, 0, 'Unpaid', GETDATE(), 'Synced');
SELECT CAST(SCOPE_IDENTITY() AS int);", connection, tx))
            {
                c.Parameters.AddWithValue("@C", clinicId);
                c.Parameters.AddWithValue("@A", appointmentId);
                c.Parameters.AddWithValue("@O", ownerId);
                c.Parameters.AddWithValue("@U", userId);
                c.Parameters.AddWithValue("@No", BillNumberHelper.Format(now.Year, next));
                billId = (int)await c.ExecuteScalarAsync();
            }

            foreach (var item in editableItems) await InsertItemAsync(connection, tx, billId, item);
            await using (var c = new SqlCommand(@"
INSERT INTO BillItem (BillID, MedicineID, ItemType, Description, Quantity, UnitPrice, Subtotal)
SELECT @B, x.MedicineID, x.ItemType, x.Description, x.Quantity, x.UnitPrice, x.Quantity * x.UnitPrice
FROM (" + FixedItemsSql + @") x;", connection, tx))
            {
                c.Parameters.AddWithValue("@B", billId);
                c.Parameters.AddWithValue("@A", appointmentId);
                await c.ExecuteNonQueryAsync();
            }
            await VisitCharges.RecomputeAsync(connection, tx, billId);
            await tx.CommitAsync();
            return (CreateBillResult.Created, billId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static async Task InsertItemAsync(SqlConnection connection, SqlTransaction tx, int billId, BillItem item)
    {
        await using var c = new SqlCommand(@"
INSERT INTO BillItem (BillID, MedicineID, ItemType, Description, Quantity, UnitPrice, Subtotal)
VALUES (@B, NULL, @Type, @D, @Q, @P, @S);", connection, tx);
        c.Parameters.AddWithValue("@B", billId);
        c.Parameters.AddWithValue("@Type", item.ItemType);
        c.Parameters.AddWithValue("@D", item.Description);
        c.Parameters.AddWithValue("@Q", item.Quantity);
        c.Parameters.Add(new SqlParameter("@P", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.UnitPrice });
        c.Parameters.Add(new SqlParameter("@S", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.Subtotal });
        await c.ExecuteNonQueryAsync();
    }

    // Reading bills -------------------------------------------------------------------------------------------------

    public async Task<PagedResult<Bill>> SearchBillsAsync(BillQuery query)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);
        string where = "b.ClinicID = @ClinicID AND b.IsDeleted = 0";
        if (!string.IsNullOrWhiteSpace(query.Search))
            where += " AND (b.BillNo LIKE @Search OR p.PetName LIKE @Search OR o.FirstName + ' ' + o.LastName LIKE @Search OR o.LastName + ' ' + o.FirstName LIKE @Search)";
        if (!string.IsNullOrEmpty(query.Status)) where += " AND b.Status = @Status";
        if (query.From != null) where += " AND b.BillDate >= @From";
        if (query.To != null) where += " AND b.BillDate < @To";

        string sql = @"
WITH Matches AS (
    SELECT " + BillColumns + @", ROW_NUMBER() OVER (ORDER BY b.BillDate DESC, b.BillID DESC) AS RowNum
    " + BillJoins + @"
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*) " + BillJoins + " WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        if (!string.IsNullOrWhiteSpace(query.Search)) command.Parameters.AddWithValue("@Search", Like(query.Search));
        if (!string.IsNullOrEmpty(query.Status)) command.Parameters.AddWithValue("@Status", query.Status);
        AddRange(command, query.From, query.To);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<Bill>();
        int total;
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) list.Add(ReadBill(r));
            await r.NextResultAsync();
            await r.ReadAsync();
            total = r.GetInt32(0);
        }
        return new PagedResult<Bill>(list, total, page, pageSize);
    }

    /// <summary>The bill with its items and payments, or null.</summary>
    public async Task<Bill> GetByIdAsync(int clinicId, int billId)
    {
        string sql = "SELECT " + BillColumns + BillJoins + @" WHERE b.BillID = @ID AND b.ClinicID = @ClinicID AND b.IsDeleted = 0;
SELECT BillItemID, BillID, MedicineID, ItemType, Description, Quantity, UnitPrice FROM BillItem
WHERE BillID = @ID AND IsDeleted = 0
ORDER BY CASE ItemType WHEN 'Consultation' THEN 0 WHEN 'Vaccine' THEN 1 WHEN 'Medicine' THEN 2 ELSE 3 END, BillItemID;
" + PaymentSelect + " WHERE pm.BillID = @ID AND pm.IsDeleted = 0 ORDER BY pm.PaymentDate, pm.PaymentID;";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", billId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        await using var r = await command.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var bill = ReadBill(r);
        await r.NextResultAsync();
        var items = new List<BillItem>();
        while (await r.ReadAsync())
            items.Add(new BillItem
            {
                BillItemId = r.GetInt32(0),
                BillId = r.GetInt32(1),
                MedicineId = r.IsDBNull(2) ? null : r.GetInt32(2),
                ItemType = r.IsDBNull(3) ? BillItemTypes.Service : r.GetString(3),
                Description = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                Quantity = r.GetInt32(5),
                UnitPrice = r.GetDecimal(6)
            });
        await r.NextResultAsync();
        var payments = new List<Payment>();
        while (await r.ReadAsync()) payments.Add(ReadPayment(r));
        bill.Items = items;
        bill.Payments = payments;
        return bill;
    }

    /// <summary>The visit's bill (not Void), or null.</summary>
    public async Task<int?> GetBillIdForAppointmentAsync(int clinicId, int appointmentId)
    {
        const string sql = "SELECT TOP 1 BillID FROM Bill WHERE AppointmentID = @A AND ClinicID = @C AND IsDeleted = 0 AND Status <> 'Void' ORDER BY BillID;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@A", appointmentId);
        command.Parameters.AddWithValue("@C", clinicId);
        return await command.ExecuteScalarAsync() as int?;
    }

    public async Task<BillingSummary> GetSummaryAsync(int clinicId, DateTime today)
    {
        const string sql = @"
SELECT
 (SELECT ISNULL(SUM(pm.AmountPaid), 0) FROM Payment pm JOIN Bill b ON b.BillID = pm.BillID
  WHERE b.ClinicID = @C AND pm.IsDeleted = 0 AND pm.PaymentDate >= @Today AND pm.PaymentDate < @Tomorrow),
 (SELECT COUNT(*) FROM Bill WHERE ClinicID = @C AND IsDeleted = 0 AND Status = 'Unpaid'),
 (SELECT COUNT(*) FROM Bill WHERE ClinicID = @C AND IsDeleted = 0 AND Status = 'Partial'),
 (SELECT ISNULL(SUM(x.Balance), 0) FROM (
    SELECT b.TotalAmount - ISNULL((SELECT SUM(pm.AmountPaid) FROM Payment pm WHERE pm.BillID = b.BillID AND pm.IsDeleted = 0), 0) AS Balance
    FROM Bill b WHERE b.ClinicID = @C AND b.IsDeleted = 0 AND b.Status IN ('Unpaid', 'Partial')) x),
 (SELECT COUNT(*) FROM Appointment a WHERE a.ClinicID = @C AND a.IsDeleted = 0 AND a.Status = 'Completed' AND a.PetID IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Bill b WHERE b.AppointmentID = a.AppointmentID AND b.IsDeleted = 0 AND b.Status <> 'Void'));";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@C", clinicId);
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.DateTime) { Value = today.Date });
        command.Parameters.Add(new SqlParameter("@Tomorrow", SqlDbType.DateTime) { Value = today.Date.AddDays(1) });
        await using var r = await command.ExecuteReaderAsync();
        await r.ReadAsync();
        return new BillingSummary
        {
            CollectedToday = r.GetDecimal(0),
            Unpaid = r.GetInt32(1),
            Partial = r.GetInt32(2),
            Outstanding = r.GetDecimal(3),
            ReadyToBill = r.GetInt32(4)
        };
    }

    // Payments list ----------------------------------------------------------------------------------------------------

    private const string PaymentColumns = @"
SELECT pm.PaymentID, pm.BillID, b.BillNo, o.FirstName, o.LastName, pm.AmountPaid, pm.PaymentMethod, pm.ReferenceNo, pm.PaymentDate,
       u.FirstName AS ByFirst, u.LastName AS ByLast, pm.SyncStatus";

    private const string PaymentFrom = @"
FROM Payment pm
JOIN Bill b ON b.BillID = pm.BillID
JOIN Owner o ON o.OwnerID = b.OwnerID
LEFT JOIN [User] u ON u.UserID = pm.ReceivedBy";

    private const string PaymentSelect = PaymentColumns + PaymentFrom;

    /// <summary>One page of payments (newest first) and the total of every payment matching the filters.</summary>
    public async Task<(PagedResult<Payment> Page, decimal Total)> GetPaymentsAsync(PaymentQuery query)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);
        string where = "b.ClinicID = @ClinicID AND pm.IsDeleted = 0";
        if (!string.IsNullOrEmpty(query.Method)) where += " AND pm.PaymentMethod = @Method";
        if (query.From != null) where += " AND pm.PaymentDate >= @From";
        if (query.To != null) where += " AND pm.PaymentDate < @To";

        string sql = @"
WITH Matches AS (
" + PaymentColumns + ", ROW_NUMBER() OVER (ORDER BY pm.PaymentDate DESC, pm.PaymentID DESC) AS RowNum" + PaymentFrom + @"
WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*), ISNULL(SUM(pm.AmountPaid), 0) " + PaymentFrom + " WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        if (!string.IsNullOrEmpty(query.Method)) command.Parameters.AddWithValue("@Method", query.Method);
        AddRange(command, query.From, query.To);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<Payment>();
        int count;
        decimal total;
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) list.Add(ReadPayment(r));
            await r.NextResultAsync();
            await r.ReadAsync();
            count = r.GetInt32(0);
            total = r.GetDecimal(1);
        }
        return (new PagedResult<Payment>(list, count, page, pageSize), total);
    }

    // Changing bills ----------------------------------------------------------------------------------------------------

    /// <summary>Rule 8: adds a Consultation or Service item to an Unpaid bill.</summary>
    public Task<BillChangeResult> AddItemAsync(int clinicId, int billId, BillItem item) =>
        ChangeUnpaidBillAsync(clinicId, billId, (connection, tx) => InsertItemAsync(connection, tx, billId, item));

    /// <summary>Rule 8: edits a Consultation or Service item of an Unpaid bill (Medicine and Vaccine items can't change, rule 6).</summary>
    public Task<BillChangeResult> UpdateItemAsync(int clinicId, int billId, BillItem item) =>
        ChangeUnpaidBillAsync(clinicId, billId, async (connection, tx) =>
        {
            await using var c = new SqlCommand(@"
UPDATE BillItem SET Description = @D, Quantity = @Q, UnitPrice = @P, Subtotal = @S, UpdatedAt = SYSDATETIME()
WHERE BillItemID = @ID AND BillID = @B AND IsDeleted = 0 AND ItemType IN ('Consultation', 'Service');", connection, tx);
            c.Parameters.AddWithValue("@ID", item.BillItemId);
            c.Parameters.AddWithValue("@B", billId);
            c.Parameters.AddWithValue("@D", item.Description);
            c.Parameters.AddWithValue("@Q", item.Quantity);
            c.Parameters.Add(new SqlParameter("@P", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.UnitPrice });
            c.Parameters.Add(new SqlParameter("@S", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.Subtotal });
            await c.ExecuteNonQueryAsync();
        });

    /// <summary>Rule 8: removes (soft-deletes) a Consultation or Service item of an Unpaid bill.</summary>
    public Task<BillChangeResult> RemoveItemAsync(int clinicId, int billId, int billItemId) =>
        ChangeUnpaidBillAsync(clinicId, billId, async (connection, tx) =>
        {
            await using var c = new SqlCommand(@"
UPDATE BillItem SET IsDeleted = 1, UpdatedAt = SYSDATETIME()
WHERE BillItemID = @ID AND BillID = @B AND IsDeleted = 0 AND ItemType IN ('Consultation', 'Service');", connection, tx);
            c.Parameters.AddWithValue("@ID", billItemId);
            c.Parameters.AddWithValue("@B", billId);
            await c.ExecuteNonQueryAsync();
        });

    private static async Task<BillChangeResult> ChangeUnpaidBillAsync(int clinicId, int billId, Func<SqlConnection, SqlTransaction, Task> change)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            string status = await LockStatusAsync(connection, tx, clinicId, billId);
            if (status == null) { await tx.RollbackAsync(); return BillChangeResult.NotFound; }
            if (status != BillStatuses.Unpaid) { await tx.RollbackAsync(); return BillChangeResult.Locked; }
            await change(connection, tx);
            await VisitCharges.RecomputeAsync(connection, tx, billId);
            await tx.CommitAsync();
            return BillChangeResult.Saved;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Rules 9, 11 and 13, in one transaction: only an Unpaid or Partial bill; the amount can't be more than the
    /// balance (checked under lock); the payment is inserted and the status follows (Partial or Paid).
    /// </summary>
    public async Task<(PaymentResult Result, string NewStatus)> ReceivePaymentAsync(int clinicId, int billId, decimal amount,
        string method, string referenceNo, int userId)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            string status = await LockStatusAsync(connection, tx, clinicId, billId);
            if (status == null) { await tx.RollbackAsync(); return (PaymentResult.NotFound, null); }
            if (status is not (BillStatuses.Unpaid or BillStatuses.Partial)) { await tx.RollbackAsync(); return (PaymentResult.Locked, status); }

            decimal balance;
            await using (var c = new SqlCommand(@"
SELECT b.TotalAmount - ISNULL((SELECT SUM(AmountPaid) FROM Payment WHERE BillID = b.BillID AND IsDeleted = 0), 0)
FROM Bill b WHERE b.BillID = @B;", connection, tx))
            {
                c.Parameters.AddWithValue("@B", billId);
                balance = (decimal)await c.ExecuteScalarAsync();
            }
            if (amount > balance) { await tx.RollbackAsync(); return (PaymentResult.OverBalance, status); }

            await using (var c = new SqlCommand(@"
INSERT INTO Payment (BillID, ReceivedBy, AmountPaid, PaymentMethod, ReferenceNo, PaymentDate, SyncStatus)
VALUES (@B, @U, @Amount, @Method, @Ref, GETDATE(), 'Synced');", connection, tx))
            {
                c.Parameters.AddWithValue("@B", billId);
                c.Parameters.AddWithValue("@U", userId);
                c.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = amount });
                c.Parameters.AddWithValue("@Method", method);
                c.Parameters.AddWithValue("@Ref", string.IsNullOrWhiteSpace(referenceNo) ? DBNull.Value : referenceNo.Trim());
                await c.ExecuteNonQueryAsync();
            }
            await VisitCharges.RecomputeAsync(connection, tx, billId);
            string newStatus;
            await using (var c = new SqlCommand("SELECT Status FROM Bill WHERE BillID = @B;", connection, tx))
            {
                c.Parameters.AddWithValue("@B", billId);
                newStatus = (string)await c.ExecuteScalarAsync();
            }
            await tx.CommitAsync();
            return (PaymentResult.Saved, newStatus);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Rule 14: void an Unpaid bill with no payments; the reason goes to Remarks. Kept, never deleted.</summary>
    public async Task<BillChangeResult> VoidAsync(int clinicId, int billId, string reason)
    {
        const string sql = @"
UPDATE Bill SET Status = 'Void', Remarks = @Reason, UpdatedAt = SYSDATETIME()
WHERE BillID = @B AND ClinicID = @C AND IsDeleted = 0 AND Status = 'Unpaid'
  AND NOT EXISTS (SELECT 1 FROM Payment WHERE BillID = @B AND IsDeleted = 0);
SELECT @@ROWCOUNT;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@B", billId);
        command.Parameters.AddWithValue("@C", clinicId);
        command.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim());
        return (int)await command.ExecuteScalarAsync() > 0 ? BillChangeResult.Saved : BillChangeResult.Locked;
    }

    // Owner module --------------------------------------------------------------------------------------------------------

    /// <summary>The owner's bills, newest first (Owner profile → Billing history).</summary>
    public async Task<IReadOnlyList<Bill>> GetOwnerBillsAsync(int clinicId, int ownerId)
    {
        string sql = "SELECT " + BillColumns + BillJoins + " WHERE b.ClinicID = @ClinicID AND b.OwnerID = @O AND b.IsDeleted = 0 ORDER BY b.BillDate DESC, b.BillID DESC;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@O", ownerId);
        var list = new List<Bill>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadBill(r));
        return list;
    }

    // Helpers ---------------------------------------------------------------------------------------------------------------

    private static async Task<string> LockStatusAsync(SqlConnection connection, SqlTransaction tx, int clinicId, int billId)
    {
        await using var c = new SqlCommand("SELECT Status FROM Bill WITH (UPDLOCK) WHERE BillID = @B AND ClinicID = @C AND IsDeleted = 0;", connection, tx);
        c.Parameters.AddWithValue("@B", billId);
        c.Parameters.AddWithValue("@C", clinicId);
        return await c.ExecuteScalarAsync() as string;
    }

    private static void AddRange(SqlCommand command, DateTime? from, DateTime? to)
    {
        if (from != null) command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = from.Value.Date });
        if (to != null) command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = to.Value.Date.AddDays(1) });
    }

    private static string Like(string text) => "%" + text.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

    private static Bill ReadBill(SqlDataReader r)
    {
        string Text(string column) { int i = r.GetOrdinal(column); return r.IsDBNull(i) ? null : r.GetString(i); }
        int petOrdinal = r.GetOrdinal("PetID");
        return new Bill
        {
            BillId = r.GetInt32(r.GetOrdinal("BillID")),
            ClinicId = r.GetInt32(r.GetOrdinal("ClinicID")),
            AppointmentId = r.GetInt32(r.GetOrdinal("AppointmentID")),
            OwnerId = r.GetInt32(r.GetOrdinal("OwnerID")),
            CashierId = r.GetInt32(r.GetOrdinal("CashierID")),
            BillNo = Text("BillNo"),
            TotalAmount = r.IsDBNull(r.GetOrdinal("TotalAmount")) ? 0 : r.GetDecimal(r.GetOrdinal("TotalAmount")),
            Status = Text("Status"),
            BillDate = r.IsDBNull(r.GetOrdinal("BillDate")) ? DateTime.MinValue : r.GetDateTime(r.GetOrdinal("BillDate")),
            Remarks = Text("Remarks"),
            SyncStatus = Text("SyncStatus"),
            Paid = r.GetDecimal(r.GetOrdinal("Paid")),
            OwnerName = PersonName.Join(Text("OwnerFirst"), Text("OwnerLast")),
            OwnerContactNo = Text("ContactNo"),
            PetId = r.IsDBNull(petOrdinal) ? 0 : r.GetInt32(petOrdinal),
            PetName = Text("PetName"),
            Species = Text("Species"),
            Breed = Text("Breed"),
            VetName = "Dr. " + PersonName.Join(Text("VetFirst"), Text("VetLast")),
            CashierName = PersonName.Join(Text("CashierFirst"), Text("CashierLast")),
            VisitDate = r.GetDateTime(r.GetOrdinal("AppointmentDate")),
            VisitTime = r.GetTimeSpan(r.GetOrdinal("AppointmentTime")),
            Reason = Text("Reason")
        };
    }

    private static Payment ReadPayment(SqlDataReader r) => new()
    {
        PaymentId = r.GetInt32(0),
        BillId = r.GetInt32(1),
        BillNo = r.IsDBNull(2) ? "Bill #" + r.GetInt32(1) : r.GetString(2),
        OwnerName = PersonName.Join(r.GetString(3), r.GetString(4)),
        AmountPaid = r.GetDecimal(5),
        PaymentMethod = r.IsDBNull(6) ? PaymentMethods.Cash : r.GetString(6),
        ReferenceNo = r.IsDBNull(7) ? null : r.GetString(7),
        PaymentDate = r.IsDBNull(8) ? DateTime.MinValue : r.GetDateTime(8),
        ReceivedByName = r.IsDBNull(9) ? "—" : PersonName.Join(r.GetString(9), r.GetString(10)),
        SyncStatus = r.GetString(11)
    };
}
