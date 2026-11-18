/*
    VetCare – Pharmacy schema and data conversion
    Source: Tasks/VetCare_Pharmacy_Module.md, sections 2 and 4.

    Medicine
    - Category widened to varchar(50). Existing rows: the category was the form (Capsule, Tablet …);
      it becomes the type (Antibiotic, Pain relief …). Units become the spec's names (Tablet, Capsule,
      Bottle, Vial, Tube, Sachet, mL).
    - Index on (ClinicID, MedicineName, Unit); "no duplicate name + unit" (rule 3) is checked in code.

    InventoryTransaction (the stock log)
    - Quantity is now signed: + added, − removed. Types: Stock In, Dispensed, Vaccine Used,
      Adjustment, Expired, Damaged. Existing 'Stock Out' rows become 'Vaccine Used' (negative);
      'Stock In' rows from same-day returns become 'Adjustment'.
    - One 'Stock In' "Opening balance" row per medicine, so each medicine's stock equals the sum of its log.

    Written for SQL Server 2008. Safe to run more than once; one transaction; -b stops on the first error.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\008_pharmacy_module.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. Medicine: categories and units -------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Medicine_Clinic_Category' AND object_id = OBJECT_ID(N'dbo.Medicine'))
    DROP INDEX IX_Medicine_Clinic_Category ON dbo.Medicine;
ALTER TABLE dbo.Medicine ALTER COLUMN Category varchar(50) NOT NULL;
GO

UPDATE dbo.Medicine SET Category = CASE
        WHEN Category = 'Vaccine' THEN 'Vaccine'
        WHEN MedicineName LIKE 'Amoxicillin%' OR MedicineName LIKE 'Metronidazole%' OR MedicineName LIKE 'Doxycycline%' THEN 'Antibiotic'
        WHEN MedicineName LIKE 'Carprofen%' OR MedicineName LIKE 'Meloxicam%' THEN 'Pain relief'
        WHEN MedicineName LIKE 'Ivermectin%' THEN 'Anti-parasitic'
        WHEN MedicineName LIKE 'Ketoconazole%' OR MedicineName LIKE 'Itraconazole%' OR MedicineName LIKE 'Miconazole%' THEN 'Antifungal'
        WHEN MedicineName LIKE '%vitamin%' THEN 'Supplement'
        WHEN Category = 'Topical' THEN 'Topical'
        ELSE 'Other' END
WHERE Category NOT IN ('Antibiotic', 'Antifungal', 'Vaccine', 'Dewormer', 'Anti-parasitic', 'Pain relief', 'Topical', 'Supplement', 'Other');

UPDATE dbo.Medicine SET Unit = CASE Unit
        WHEN 'cap' THEN 'Capsule' WHEN 'tab' THEN 'Tablet' WHEN 'btl' THEN 'Bottle' WHEN 'tube' THEN 'Tube'
        WHEN 'dose' THEN 'Vial' WHEN 'sachet' THEN 'Sachet' WHEN 'vial' THEN 'Vial' WHEN 'ml' THEN 'mL' ELSE Unit END
WHERE Unit IN ('cap', 'tab', 'btl', 'tube', 'dose', 'sachet', 'vial', 'ml');

-- The sample antifungal cream was filed under its form (Topical).
UPDATE dbo.Medicine SET Category = 'Antifungal' WHERE MedicineName = 'Ketoconazole cream' AND Category = 'Topical';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Medicine_Clinic_Name' AND object_id = OBJECT_ID(N'dbo.Medicine'))
    CREATE INDEX IX_Medicine_Clinic_Name ON dbo.Medicine (ClinicID, MedicineName, Unit);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Medicine_Clinic_Category' AND object_id = OBJECT_ID(N'dbo.Medicine'))
    CREATE INDEX IX_Medicine_Clinic_Category ON dbo.Medicine (ClinicID, Category);
GO

-- 2. InventoryTransaction: signed quantities and the spec's types ---------------------------------------
UPDATE dbo.InventoryTransaction SET TransactionType = 'Vaccine Used', Quantity = -ABS(Quantity)
WHERE TransactionType = 'Stock Out';

UPDATE dbo.InventoryTransaction SET TransactionType = 'Adjustment'
WHERE TransactionType = 'Stock In' AND Notes = 'Vaccination removed from record';
GO

-- 3. Opening balance: each medicine's stock = the sum of its log -----------------------------------------
INSERT INTO dbo.InventoryTransaction (ClinicID, MedicineID, TransactionType, Quantity, RecordID, Notes, CreatedBy, CreatedAt)
SELECT m.ClinicID, m.MedicineID, 'Stock In',
       m.StockQty - ISNULL((SELECT SUM(t.Quantity) FROM dbo.InventoryTransaction t WHERE t.MedicineID = m.MedicineID), 0),
       NULL, 'Opening balance',
       COALESCE((SELECT TOP 1 u.UserID FROM dbo.[User] u WHERE u.ClinicID = m.ClinicID AND u.RoleID = 4 ORDER BY u.UserID),
                (SELECT TOP 1 u.UserID FROM dbo.[User] u WHERE u.ClinicID = m.ClinicID ORDER BY u.UserID),
                (SELECT MIN(u.UserID) FROM dbo.[User] u)),
       m.CreatedAt
FROM dbo.Medicine m
WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryTransaction t WHERE t.MedicineID = m.MedicineID AND t.Notes = 'Opening balance')
  AND m.StockQty - ISNULL((SELECT SUM(t.Quantity) FROM dbo.InventoryTransaction t WHERE t.MedicineID = m.MedicineID), 0) <> 0;
GO

-- 4. Indexes ---------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTransaction_Type' AND object_id = OBJECT_ID(N'dbo.InventoryTransaction'))
    CREATE INDEX IX_InventoryTransaction_Type ON dbo.InventoryTransaction (ClinicID, TransactionType, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Prescription_IsDispensed' AND object_id = OBJECT_ID(N'dbo.Prescription'))
    CREATE INDEX IX_Prescription_IsDispensed ON dbo.Prescription (IsDispensed, MedicineID);
GO

COMMIT TRANSACTION;
GO

PRINT 'Pharmacy schema is up to date.';
GO
