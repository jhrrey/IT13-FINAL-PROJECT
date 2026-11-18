/*
    VetCare – Vaccinations schema
    Source: Tasks/VetCare_Vaccination_Module.md, sections 2 and 4 (rules 5 and 6).

    - InventoryTransaction: the stock log. Giving a vaccine writes a 'Stock Out' row (rule 5); the
      Medicines & stock module will add 'Stock In' / 'Adjustment' rows.
    - Bill / BillItem (created by hand): money columns widened to decimal(10,2) — TotalAmount was
      decimal(6,2), so a bill couldn't go over 9,999.99. Vaccines are added as BillItems (rule 6).
    - Indexes for the due list (Vaccination.NextDueDate) and a visit's bill (Bill.AppointmentID).
    - Written for SQL Server 2008: no THROW, OFFSET/FETCH, IIF or CONCAT.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\007_vaccinations_module.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. InventoryTransaction ----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.InventoryTransaction', N'U') IS NULL
    CREATE TABLE dbo.InventoryTransaction (
        TransactionID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryTransaction PRIMARY KEY,
        ClinicID        int          NOT NULL CONSTRAINT FK_InventoryTransaction_Clinic REFERENCES dbo.Clinic (ClinicID),
        MedicineID      int          NOT NULL CONSTRAINT FK_InventoryTransaction_Medicine REFERENCES dbo.Medicine (MedicineID),
        TransactionType varchar(20)  NOT NULL,       -- 'Stock In', 'Stock Out', 'Adjustment'
        Quantity        int          NOT NULL,       -- always positive; the type says which way
        RecordID        int          NULL CONSTRAINT FK_InventoryTransaction_MedicalRecord REFERENCES dbo.MedicalRecord (RecordID),
        Notes           varchar(255) NULL,
        CreatedBy       int          NOT NULL CONSTRAINT FK_InventoryTransaction_CreatedBy REFERENCES dbo.[User] (UserID),
        CreatedAt       datetime     NOT NULL CONSTRAINT DF_InventoryTransaction_CreatedAt DEFAULT GETDATE(),
        SyncStatus      varchar(20)  NOT NULL CONSTRAINT DF_InventoryTransaction_SyncStatus DEFAULT 'Synced'
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryTransaction_MedicineID' AND object_id = OBJECT_ID(N'dbo.InventoryTransaction'))
    CREATE INDEX IX_InventoryTransaction_MedicineID ON dbo.InventoryTransaction (MedicineID);
GO

-- 2. Bill money columns ------------------------------------------------------------------------------
-- TotalAmount has a default (0); a default doesn't block ALTER COLUMN on a type change of the same family.
ALTER TABLE dbo.Bill     ALTER COLUMN TotalAmount decimal(10,2) NULL;
ALTER TABLE dbo.BillItem ALTER COLUMN UnitPrice   decimal(10,2) NOT NULL;
ALTER TABLE dbo.BillItem ALTER COLUMN Subtotal    decimal(10,2) NOT NULL;
GO

-- 3. Indexes ------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vaccination_NextDueDate' AND object_id = OBJECT_ID(N'dbo.Vaccination'))
    CREATE INDEX IX_Vaccination_NextDueDate ON dbo.Vaccination (NextDueDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bill_AppointmentID' AND object_id = OBJECT_ID(N'dbo.Bill'))
    CREATE INDEX IX_Bill_AppointmentID ON dbo.Bill (AppointmentID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BillItem_BillID' AND object_id = OBJECT_ID(N'dbo.BillItem'))
    CREATE INDEX IX_BillItem_BillID ON dbo.BillItem (BillID);
GO

COMMIT TRANSACTION;
GO

PRINT 'Vaccinations schema is up to date.';
GO
