using Microsoft.Data.SqlClient;

namespace VetCare.Models.Repositories;

/// <summary>
/// Stock and billing for what a visit uses, run inside the caller's transaction:
/// <list type="bullet">
/// <item>giving a vaccine lowers <c>Medicine.StockQty</c> by 1 (only if in stock and not expired) and logs
/// 'Vaccine Used'; taking one back (removed the same day) does the reverse;</item>
/// <item>bills are created by the cashier (Billing module). If the visit already has an open bill (Unpaid or
/// Partial), the vaccine or dispensed medicine is added to it right away (Billing rule 7); otherwise Billing
/// picks it up when the bill is created.</item>
/// </list>
/// </summary>
internal static class VisitCharges
{
    /// <summary>Uses one dose of the vaccine. Returns false (nothing changed) when it is out of stock or expired.</summary>
    public static async Task<bool> GiveVaccineAsync(SqlConnection connection, SqlTransaction tx, int clinicId,
        int appointmentId, int recordId, int medicineId, int doseNo, int userId)
    {
        // Vaccination rule 4 and pharmacy rule 10: at least 1 in stock and not expired — checked and lowered in one statement.
        await using (var c = new SqlCommand(@"
UPDATE Medicine SET StockQty = StockQty - 1, UpdatedAt = GETDATE()
WHERE MedicineID = @M AND ClinicID = @C AND IsDeleted = 0 AND StockQty >= 1
  AND (ExpiryDate IS NULL OR ExpiryDate >= CAST(GETDATE() AS date));", connection, tx))
        {
            c.Parameters.AddWithValue("@M", medicineId);
            c.Parameters.AddWithValue("@C", clinicId);
            if (await c.ExecuteNonQueryAsync() == 0) return false;
        }

        // The stock log ("Vaccine Used", −1).
        await using (var c = new SqlCommand(@"
INSERT INTO InventoryTransaction (ClinicID, MedicineID, TransactionType, Quantity, RecordID, Notes, CreatedBy)
VALUES (@C, @M, 'Vaccine Used', -1, @R, 'Vaccine given', @U);", connection, tx))
        {
            c.Parameters.AddWithValue("@C", clinicId);
            c.Parameters.AddWithValue("@M", medicineId);
            c.Parameters.AddWithValue("@R", recordId);
            c.Parameters.AddWithValue("@U", userId);
            await c.ExecuteNonQueryAsync();
        }

        // Billing rule 7: add it to the visit's open bill, if there is one.
        int? billId = await OpenBillAsync(connection, tx, appointmentId);
        if (billId != null)
        {
            await using (var c = new SqlCommand(@"
INSERT INTO BillItem (BillID, MedicineID, ItemType, Description, Quantity, UnitPrice, Subtotal)
SELECT @B, MedicineID, 'Vaccine', LEFT(MedicineName + ' – Dose ' + CAST(@Dose AS varchar(5)), 100), 1, UnitPrice, UnitPrice
FROM Medicine WHERE MedicineID = @M;", connection, tx))
            {
                c.Parameters.AddWithValue("@B", billId.Value);
                c.Parameters.AddWithValue("@M", medicineId);
                c.Parameters.AddWithValue("@Dose", doseNo);
                await c.ExecuteNonQueryAsync();
            }
            await RecomputeAsync(connection, tx, billId.Value);
        }
        return true;
    }

    /// <summary>Takes back one dose (removed or replaced the same day): stock +1, an 'Adjustment' row, and its open-bill item removed.</summary>
    public static async Task ReturnVaccineAsync(SqlConnection connection, SqlTransaction tx, int clinicId,
        int appointmentId, int recordId, int medicineId, int userId)
    {
        await using (var c = new SqlCommand(@"
UPDATE Medicine SET StockQty = StockQty + 1, UpdatedAt = GETDATE() WHERE MedicineID = @M AND ClinicID = @C;
INSERT INTO InventoryTransaction (ClinicID, MedicineID, TransactionType, Quantity, RecordID, Notes, CreatedBy)
VALUES (@C, @M, 'Adjustment', 1, @R, 'Vaccination removed from record', @U);", connection, tx))
        {
            c.Parameters.AddWithValue("@C", clinicId);
            c.Parameters.AddWithValue("@M", medicineId);
            c.Parameters.AddWithValue("@R", recordId);
            c.Parameters.AddWithValue("@U", userId);
            await c.ExecuteNonQueryAsync();
        }

        // Only an Unpaid bill's items change (Billing rule 12: paid bills are locked).
        int? billId = await OpenBillAsync(connection, tx, appointmentId, unpaidOnly: true);
        if (billId == null) return;

        await using (var c = new SqlCommand(@"
UPDATE BillItem SET IsDeleted = 1, UpdatedAt = SYSDATETIME()
WHERE BillItemID = (SELECT TOP 1 BillItemID FROM BillItem
                    WHERE BillID = @B AND MedicineID = @M AND ItemType = 'Vaccine' AND IsDeleted = 0 ORDER BY BillItemID DESC);", connection, tx))
        {
            c.Parameters.AddWithValue("@B", billId.Value);
            c.Parameters.AddWithValue("@M", medicineId);
            await c.ExecuteNonQueryAsync();
        }
        await RecomputeAsync(connection, tx, billId.Value);
    }

    /// <summary>Adds a dispensed medicine to the visit's open bill (Billing rule 7). No open bill: nothing to do yet.</summary>
    public static async Task AddItemAsync(SqlConnection connection, SqlTransaction tx, int clinicId, int appointmentId,
        int? medicineId, string itemType, string description, int quantity, decimal unitPrice)
    {
        int? billId = await OpenBillAsync(connection, tx, appointmentId);
        if (billId == null) return;
        await using (var c = new SqlCommand(@"
INSERT INTO BillItem (BillID, MedicineID, ItemType, Description, Quantity, UnitPrice, Subtotal)
VALUES (@B, @M, @Type, @Description, @Q, @Price, @Subtotal);", connection, tx))
        {
            c.Parameters.AddWithValue("@B", billId.Value);
            c.Parameters.AddWithValue("@M", (object)medicineId ?? DBNull.Value);
            c.Parameters.AddWithValue("@Type", itemType);
            c.Parameters.AddWithValue("@Description", description.Length > 100 ? description.Substring(0, 100) : description);
            c.Parameters.AddWithValue("@Q", quantity);
            c.Parameters.Add(new SqlParameter("@Price", System.Data.SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = unitPrice });
            c.Parameters.Add(new SqlParameter("@Subtotal", System.Data.SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = unitPrice * quantity });
            await c.ExecuteNonQueryAsync();
        }
        await RecomputeAsync(connection, tx, billId.Value);
    }

    /// <summary>The visit's open bill (Unpaid or Partial; only Unpaid with <paramref name="unpaidOnly"/>), locked for the update.</summary>
    private static async Task<int?> OpenBillAsync(SqlConnection connection, SqlTransaction tx, int appointmentId, bool unpaidOnly = false)
    {
        await using var c = new SqlCommand(@"
SELECT TOP 1 BillID FROM Bill WITH (UPDLOCK)
WHERE AppointmentID = @A AND IsDeleted = 0 AND Status IN ('Unpaid', @Partial) ORDER BY BillID;", connection, tx);
        c.Parameters.AddWithValue("@A", appointmentId);
        c.Parameters.AddWithValue("@Partial", unpaidOnly ? "Unpaid" : "Partial");
        return await c.ExecuteScalarAsync() as int?;
    }

    /// <summary>
    /// Billing rules 4 and 11: Total = sum of the item subtotals; the status follows the payments
    /// (Unpaid = none, Partial = some, Paid = nothing left). A Void bill is left alone.
    /// </summary>
    internal static async Task RecomputeAsync(SqlConnection connection, SqlTransaction tx, int billId)
    {
        await using var c = new SqlCommand(@"
DECLARE @Total decimal(10,2) = (SELECT ISNULL(SUM(Subtotal), 0) FROM BillItem WHERE BillID = @B AND IsDeleted = 0);
DECLARE @Paid decimal(10,2) = (SELECT ISNULL(SUM(AmountPaid), 0) FROM Payment WHERE BillID = @B AND IsDeleted = 0);
UPDATE Bill SET TotalAmount = @Total,
       Status = CASE WHEN @Paid <= 0 THEN 'Unpaid' WHEN @Paid >= @Total THEN 'Paid' ELSE 'Partial' END,
       UpdatedAt = SYSDATETIME()
WHERE BillID = @B AND Status <> 'Void';", connection, tx);
        c.Parameters.AddWithValue("@B", billId);
        await c.ExecuteNonQueryAsync();
    }
}
