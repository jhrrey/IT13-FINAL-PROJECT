using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

/// <summary>Filters for the medicines table. <see cref="ClinicId"/> is always set by the controller.</summary>
public sealed class MedicineQuery
{
    public int ClinicId { get; set; }
    public string Search { get; set; }
    public string Category { get; set; }
    public StockStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Counts for the Medicines &amp; stock summary cards.</summary>
public sealed class StockSummary
{
    public int TotalItems { get; set; }
    public int LowStock { get; set; }
    public int OutOfStock { get; set; }
    public int ExpiringSoon { get; set; }
}

public enum StockChangeResult
{
    Saved,
    NotFound,

    /// <summary>Rule 9: the change would take the stock below 0.</summary>
    BelowZero,

    /// <summary>Rule 12: archiving needs the stock at 0.</summary>
    StockNotZero
}

/// <summary>
/// All SQL for <c>Medicine</c>. The stock is never edited directly (rule 5): every change also writes
/// an <c>InventoryTransaction</c> in the same SQL transaction. Medicines are archived, never deleted.
/// </summary>
public sealed class MedicineRepository
{
    private const string Columns = @"
m.MedicineID, m.ClinicID, m.MedicineName, m.Category, m.Unit, m.StockQty, m.ReorderLevel, m.UnitPrice, m.ExpiryDate,
m.CreatedAt, m.UpdatedAt, m.IsDeleted, m.SyncStatus";

    /// <summary>0 in stock, 1 low, 2 out, 3 expiring soon, 4 expired (same order as <see cref="StockStatus"/>).</summary>
    private const string StatusCase = @"
CASE WHEN m.ExpiryDate < @Today THEN 4
     WHEN m.StockQty <= 0 THEN 2
     WHEN m.ExpiryDate <= @In30 THEN 3
     WHEN m.StockQty <= m.ReorderLevel THEN 1
     ELSE 0 END";

    /// <summary>The clinic's medicines (not archived), by name — consultation dropdowns and filters.</summary>
    public async Task<IReadOnlyList<Medicine>> GetActiveAsync(int clinicId)
    {
        string sql = "SELECT " + Columns + " FROM Medicine m WHERE m.ClinicID = @ClinicID AND m.IsDeleted = 0 ORDER BY m.MedicineName;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        var list = new List<Medicine>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Read(reader));
        return list;
    }

    /// <summary>One page of the medicines table, by name.</summary>
    public async Task<PagedResult<Medicine>> SearchAsync(MedicineQuery query, DateTime today)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);
        string where = "m.ClinicID = @ClinicID AND m.IsDeleted = 0";
        if (!string.IsNullOrWhiteSpace(query.Search)) where += " AND m.MedicineName LIKE @Search";
        if (!string.IsNullOrEmpty(query.Category)) where += " AND m.Category = @Category";
        if (query.Status != null) where += " AND (" + StatusCase + ") = @Status";

        string sql = @"
WITH Matches AS (
    SELECT " + Columns + @", ROW_NUMBER() OVER (ORDER BY m.MedicineName, m.MedicineID) AS RowNum
    FROM Medicine m WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*) FROM Medicine m WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        AddDates(command, today);
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);
        if (!string.IsNullOrWhiteSpace(query.Search))
            command.Parameters.AddWithValue("@Search", "%" + query.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");
        if (!string.IsNullOrEmpty(query.Category)) command.Parameters.AddWithValue("@Category", query.Category);
        if (query.Status != null) command.Parameters.AddWithValue("@Status", (int)query.Status.Value);

        var list = new List<Medicine>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add(Read(reader));
            await reader.NextResultAsync();
            await reader.ReadAsync();
            total = reader.GetInt32(0);
        }
        return new PagedResult<Medicine>(list, total, page, pageSize);
    }

    public async Task<Medicine> GetByIdAsync(int clinicId, int medicineId)
    {
        string sql = "SELECT " + Columns + " FROM Medicine m WHERE m.MedicineID = @ID AND m.ClinicID = @ClinicID AND m.IsDeleted = 0;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", medicineId);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task<StockSummary> GetSummaryAsync(int clinicId, DateTime today)
    {
        string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN s = 1 THEN 1 ELSE 0 END),
       SUM(CASE WHEN s = 2 THEN 1 ELSE 0 END),
       SUM(CASE WHEN s = 3 THEN 1 ELSE 0 END)
FROM (SELECT " + StatusCase + " AS s FROM Medicine m WHERE m.ClinicID = @ClinicID AND m.IsDeleted = 0) x;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        AddDates(command, today);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        int Value(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
        return new StockSummary { TotalItems = Value(0), LowStock = Value(1), OutOfStock = Value(2), ExpiringSoon = Value(3) };
    }

    /// <summary>Rule 3: another active medicine of the clinic with this name and unit.</summary>
    public async Task<bool> NameExistsAsync(int clinicId, string name, string unit, int? excludeId)
    {
        const string sql = @"
SELECT COUNT(*) FROM Medicine
WHERE ClinicID = @ClinicID AND IsDeleted = 0 AND MedicineName = @Name AND Unit = @Unit AND (@Exclude IS NULL OR MedicineID <> @Exclude);";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", clinicId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Unit", unit);
        command.Parameters.AddWithValue("@Exclude", (object)excludeId ?? DBNull.Value);
        return (int)await command.ExecuteScalarAsync() > 0;
    }

    /// <summary>Adds the medicine; a starting stock is saved as its first "Stock In".</summary>
    public async Task<int> InsertAsync(Medicine m, int startingStock, int userId)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            int id;
            await using (var c = new SqlCommand(@"
INSERT INTO Medicine (ClinicID, MedicineName, Category, Unit, StockQty, ReorderLevel, UnitPrice, ExpiryDate, SyncStatus)
VALUES (@ClinicID, @Name, @Category, @Unit, @Stock, @Reorder, @Price, @Expiry, 'Synced');
SELECT CAST(SCOPE_IDENTITY() AS int);", connection, tx))
            {
                AddEditable(c, m);
                c.Parameters.AddWithValue("@ClinicID", m.ClinicId);
                c.Parameters.AddWithValue("@Stock", startingStock);
                id = (int)await c.ExecuteScalarAsync();
            }
            if (startingStock > 0)
                await LogAsync(connection, tx, m.ClinicId, id, "Stock In", startingStock, "Starting stock", userId);
            await tx.CommitAsync();
            m.MedicineId = id;
            return id;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Saves the editable details. The stock is not touched here (rule 5).</summary>
    public async Task<bool> UpdateAsync(Medicine m)
    {
        const string sql = @"
UPDATE Medicine SET MedicineName = @Name, Category = @Category, Unit = @Unit, ReorderLevel = @Reorder, UnitPrice = @Price,
       ExpiryDate = @Expiry, UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE MedicineID = @ID AND ClinicID = @ClinicID AND IsDeleted = 0;";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddEditable(command, m);
        command.Parameters.AddWithValue("@ID", m.MedicineId);
        command.Parameters.AddWithValue("@ClinicID", m.ClinicId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Stock in: + quantity, an optional new expiry date, and a "Stock In" log row.</summary>
    public Task<StockChangeResult> StockInAsync(int clinicId, int medicineId, int quantity, DateTime? newExpiry, string remarks, int userId) =>
        ChangeStockAsync(clinicId, medicineId, quantity, "Stock In", remarks, userId, newExpiry);

    /// <summary>Count correction / Expired / Damaged with a signed quantity. Refused below 0 (rule 9).</summary>
    public Task<StockChangeResult> AdjustAsync(int clinicId, int medicineId, string type, int signedQuantity, string reason, int userId) =>
        ChangeStockAsync(clinicId, medicineId, signedQuantity, type, reason, userId, null);

    private static async Task<StockChangeResult> ChangeStockAsync(int clinicId, int medicineId, int signedQuantity, string type,
        string remarks, int userId, DateTime? newExpiry)
    {
        await using var connection = await Database.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            int stock;
            await using (var c = new SqlCommand("SELECT StockQty FROM Medicine WITH (UPDLOCK) WHERE MedicineID = @ID AND ClinicID = @C AND IsDeleted = 0;", connection, tx))
            {
                c.Parameters.AddWithValue("@ID", medicineId);
                c.Parameters.AddWithValue("@C", clinicId);
                if (await c.ExecuteScalarAsync() is not int s) { await tx.RollbackAsync(); return StockChangeResult.NotFound; }
                stock = s;
            }
            if (stock + signedQuantity < 0) { await tx.RollbackAsync(); return StockChangeResult.BelowZero; }

            await using (var c = new SqlCommand(@"
UPDATE Medicine SET StockQty = StockQty + @Q, ExpiryDate = COALESCE(@Expiry, ExpiryDate), UpdatedAt = GETDATE(), SyncStatus = 'Synced'
WHERE MedicineID = @ID;", connection, tx))
            {
                c.Parameters.AddWithValue("@Q", signedQuantity);
                c.Parameters.Add(new SqlParameter("@Expiry", SqlDbType.Date) { Value = (object)newExpiry?.Date ?? DBNull.Value });
                c.Parameters.AddWithValue("@ID", medicineId);
                await c.ExecuteNonQueryAsync();
            }
            await LogAsync(connection, tx, clinicId, medicineId, type, signedQuantity, remarks, userId);
            await tx.CommitAsync();
            return StockChangeResult.Saved;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Rule 12: archives the medicine, only when its stock is 0.</summary>
    public async Task<StockChangeResult> ArchiveAsync(int clinicId, int medicineId)
    {
        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM Medicine WHERE MedicineID = @ID AND ClinicID = @C AND IsDeleted = 0) SELECT 1;
ELSE IF EXISTS (SELECT 1 FROM Medicine WHERE MedicineID = @ID AND StockQty <> 0) SELECT 2;
ELSE BEGIN
    UPDATE Medicine SET IsDeleted = 1, UpdatedAt = GETDATE(), SyncStatus = 'Synced' WHERE MedicineID = @ID AND StockQty = 0;
    SELECT 0;
END";
        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ID", medicineId);
        command.Parameters.AddWithValue("@C", clinicId);
        return (int)await command.ExecuteScalarAsync() switch
        {
            0 => StockChangeResult.Saved,
            2 => StockChangeResult.StockNotZero,
            _ => StockChangeResult.NotFound
        };
    }

    /// <summary>Writes one stock-log row inside the caller's transaction.</summary>
    internal static async Task LogAsync(SqlConnection connection, SqlTransaction tx, int clinicId, int medicineId, string type,
        int signedQuantity, string remarks, int userId, int? recordId = null)
    {
        await using var c = new SqlCommand(@"
INSERT INTO InventoryTransaction (ClinicID, MedicineID, TransactionType, Quantity, RecordID, Notes, CreatedBy)
VALUES (@C, @M, @Type, @Q, @R, @Notes, @U);", connection, tx);
        c.Parameters.AddWithValue("@C", clinicId);
        c.Parameters.AddWithValue("@M", medicineId);
        c.Parameters.AddWithValue("@Type", type);
        c.Parameters.AddWithValue("@Q", signedQuantity);
        c.Parameters.AddWithValue("@R", (object)recordId ?? DBNull.Value);
        c.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(remarks) ? DBNull.Value : remarks.Length > 255 ? remarks.Substring(0, 255) : remarks);
        c.Parameters.AddWithValue("@U", userId);
        await c.ExecuteNonQueryAsync();
    }

    private static void AddDates(SqlCommand command, DateTime today)
    {
        command.Parameters.Add(new SqlParameter("@Today", SqlDbType.Date) { Value = today.Date });
        command.Parameters.Add(new SqlParameter("@In30", SqlDbType.Date) { Value = today.Date.AddDays(StockStatusHelper.ExpiringDays) });
    }

    private static void AddEditable(SqlCommand c, Medicine m)
    {
        c.Parameters.AddWithValue("@Name", m.MedicineName);
        c.Parameters.AddWithValue("@Category", m.Category);
        c.Parameters.AddWithValue("@Unit", m.Unit);
        c.Parameters.AddWithValue("@Reorder", m.ReorderLevel);
        c.Parameters.Add(new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = m.UnitPrice });
        c.Parameters.Add(new SqlParameter("@Expiry", SqlDbType.Date) { Value = (object)m.ExpiryDate?.Date ?? DBNull.Value });
    }

    private static Medicine Read(SqlDataReader r)
    {
        int expiry = r.GetOrdinal("ExpiryDate");
        return new Medicine
        {
            MedicineId = r.GetInt32(r.GetOrdinal("MedicineID")),
            ClinicId = r.GetInt32(r.GetOrdinal("ClinicID")),
            MedicineName = r.GetString(r.GetOrdinal("MedicineName")),
            Category = r.GetString(r.GetOrdinal("Category")),
            Unit = r.GetString(r.GetOrdinal("Unit")),
            StockQty = r.GetInt32(r.GetOrdinal("StockQty")),
            ReorderLevel = r.GetInt32(r.GetOrdinal("ReorderLevel")),
            UnitPrice = r.GetDecimal(r.GetOrdinal("UnitPrice")),
            ExpiryDate = r.IsDBNull(expiry) ? null : r.GetDateTime(expiry),
            CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            UpdatedAt = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
            IsDeleted = r.GetBoolean(r.GetOrdinal("IsDeleted")),
            SyncStatus = r.GetString(r.GetOrdinal("SyncStatus"))
        };
    }
}
