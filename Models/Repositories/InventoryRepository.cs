using System.Data;
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;

namespace VetCare.Models.Repositories;

/// <summary>One row of the stock log (<c>InventoryTransaction</c>), with names for display.</summary>
public sealed class StockLogEntry
{
    public int TransactionId { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; }
    public string Unit { get; set; }
    public string TransactionType { get; set; }

    /// <summary>Signed: + added, − removed.</summary>
    public int Quantity { get; set; }

    public int? RecordId { get; set; }
    public string Remarks { get; set; }
    public string DoneBy { get; set; }
    public DateTime Date { get; set; }
    public string SyncStatus { get; set; }

    public string QuantityText => (Quantity > 0 ? "+" : Quantity < 0 ? "−" : string.Empty) + Math.Abs(Quantity);
}

public sealed class StockLogQuery
{
    public int ClinicId { get; set; }
    public int? MedicineId { get; set; }
    public string Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Reads the stock log (rule 11: every stock change is shown with who did it and why).</summary>
public sealed class InventoryRepository
{
    public async Task<PagedResult<StockLogEntry>> GetHistoryAsync(StockLogQuery query)
    {
        int page = Math.Max(1, query.Page), pageSize = Math.Max(1, query.PageSize);
        string where = "t.ClinicID = @ClinicID";
        if (query.MedicineId != null) where += " AND t.MedicineID = @MedicineID";
        if (!string.IsNullOrEmpty(query.Type)) where += " AND t.TransactionType = @Type";
        if (query.From != null) where += " AND t.CreatedAt >= @From";
        if (query.To != null) where += " AND t.CreatedAt < @To";

        string sql = @"
WITH Matches AS (
    SELECT t.TransactionID, t.MedicineID, m.MedicineName, m.Unit, t.TransactionType, t.Quantity, t.RecordID, t.Notes,
           u.FirstName, u.LastName, t.CreatedAt, t.SyncStatus,
           ROW_NUMBER() OVER (ORDER BY t.CreatedAt DESC, t.TransactionID DESC) AS RowNum
    FROM InventoryTransaction t
    JOIN Medicine m ON m.MedicineID = t.MedicineID
    LEFT JOIN [User] u ON u.UserID = t.CreatedBy
    WHERE " + where + @"
)
SELECT * FROM Matches WHERE RowNum BETWEEN @FirstRow AND @LastRow ORDER BY RowNum;
SELECT COUNT(*) FROM InventoryTransaction t WHERE " + where + ";";

        await using var connection = await Database.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ClinicID", query.ClinicId);
        if (query.MedicineId != null) command.Parameters.AddWithValue("@MedicineID", query.MedicineId.Value);
        if (!string.IsNullOrEmpty(query.Type)) command.Parameters.AddWithValue("@Type", query.Type);
        if (query.From != null) command.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = query.From.Value.Date });
        if (query.To != null) command.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = query.To.Value.Date.AddDays(1) });
        command.Parameters.AddWithValue("@FirstRow", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@LastRow", page * pageSize);

        var list = new List<StockLogEntry>();
        int total;
        await using (var r = await command.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                list.Add(new StockLogEntry
                {
                    TransactionId = r.GetInt32(0),
                    MedicineId = r.GetInt32(1),
                    MedicineName = r.GetString(2),
                    Unit = r.GetString(3),
                    TransactionType = r.GetString(4),
                    Quantity = r.GetInt32(5),
                    RecordId = r.IsDBNull(6) ? null : r.GetInt32(6),
                    Remarks = r.IsDBNull(7) ? null : r.GetString(7),
                    DoneBy = r.IsDBNull(8) ? "—" : Helpers.PersonName.Join(r.GetString(8), r.GetString(9)),
                    Date = r.GetDateTime(10),
                    SyncStatus = r.GetString(11)
                });
            }
            await r.NextResultAsync();
            await r.ReadAsync();
            total = r.GetInt32(0);
        }
        return new PagedResult<StockLogEntry>(list, total, page, pageSize);
    }
}
