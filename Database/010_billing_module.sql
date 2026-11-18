/*
    VetCare – Billing module ("Bills & payments")
    Source: Tasks/VetCare_Billing_Module.md, sections 2 and 4 (with the optional BillNo and Remarks columns).

    Clinic
    - ConsultationFee decimal(10,2), default ₱400: the consultation fee a new bill starts with (the cashier can change it).

    Bill
    - BillNo varchar(20): readable receipt number, e.g. VC-2026-0001 (unique per clinic; every bill has one).
    - Remarks varchar(255): the reason a bill was voided.
    - Status: Unpaid / Partial / Paid / Void, not null.

    BillItem / Payment: checks for item type, quantity, price, amount and payment method.

    Data: before this module, giving a vaccine or dispensing a medicine created an Unpaid bill automatically.
    Bills are now created by the cashier from a completed visit, so those automatic bills (no BillNo) are
    removed when they have no payments; their visits appear in "Ready to bill" with the same items.

    Written for SQL Server 2008. Safe to run more than once; one transaction; -b stops on the first error.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\010_billing_module.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRANSACTION;
GO

-- 1. Clinic consultation fee ------------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Clinic', N'ConsultationFee') IS NULL
    ALTER TABLE dbo.Clinic ADD ConsultationFee decimal(10,2) NOT NULL CONSTRAINT DF_Clinic_ConsultationFee DEFAULT (400);
GO

-- 2. Bill: BillNo, Remarks, Status --------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Bill', N'BillNo') IS NULL ALTER TABLE dbo.Bill ADD BillNo varchar(20) NULL;
IF COL_LENGTH(N'dbo.Bill', N'Remarks') IS NULL ALTER TABLE dbo.Bill ADD Remarks varchar(255) NULL;
GO

-- 3. Remove the automatic bills (no BillNo, no payments) ---------------------------------------------------
DECLARE @auto TABLE (BillID int PRIMARY KEY);
INSERT INTO @auto (BillID)
SELECT b.BillID FROM dbo.Bill b
WHERE b.BillNo IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.Payment p WHERE p.BillID = b.BillID);
DELETE FROM dbo.BillItem WHERE BillID IN (SELECT BillID FROM @auto);
DELETE FROM dbo.Bill WHERE BillID IN (SELECT BillID FROM @auto);
GO

-- Bills left from before this module (if any had payments) get a number, so every bill has one.
UPDATE dbo.Bill SET BillNo = 'OLD-' + RIGHT('00000' + CAST(BillID AS varchar(10)), 6)   -- outside the VC-yyyy- numbering
WHERE BillNo IS NULL;
GO

UPDATE dbo.Bill SET Status = 'Unpaid' WHERE Status IS NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bill') AND name = N'Status' AND is_nullable = 1)
    ALTER TABLE dbo.Bill ALTER COLUMN Status varchar(20) NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Bill')
               AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Bill'), 'Status', 'ColumnId'))
    ALTER TABLE dbo.Bill ADD CONSTRAINT DF_Bill_Status DEFAULT ('Unpaid') FOR Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Bill_Status')
    ALTER TABLE dbo.Bill WITH CHECK ADD CONSTRAINT CK_Bill_Status CHECK (Status IN ('Unpaid', 'Partial', 'Paid', 'Void'));
GO

-- 4. BillItem and Payment checks ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BillItem_Type')
    ALTER TABLE dbo.BillItem WITH CHECK ADD CONSTRAINT CK_BillItem_Type CHECK (ItemType IN ('Consultation', 'Medicine', 'Vaccine', 'Service'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BillItem_Amounts')
    ALTER TABLE dbo.BillItem WITH CHECK ADD CONSTRAINT CK_BillItem_Amounts CHECK (Quantity > 0 AND UnitPrice >= 0);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Payment_Amount')
    ALTER TABLE dbo.Payment WITH CHECK ADD CONSTRAINT CK_Payment_Amount CHECK (AmountPaid > 0);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Payment_Method')
    ALTER TABLE dbo.Payment WITH CHECK ADD CONSTRAINT CK_Payment_Method CHECK (PaymentMethod IN ('Cash', 'GCash', 'Maya', 'Card', 'Bank transfer'));
GO

-- 5. Indexes ---------------------------------------------------------------------------------------------------
-- Every bill gets a BillNo, so a plain unique index (a filtered one would make plain sqlcmd writes fail
-- with QUOTED_IDENTIFIER off).
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Bill_Clinic_BillNo' AND object_id = OBJECT_ID(N'dbo.Bill') AND has_filter = 1)
    DROP INDEX UX_Bill_Clinic_BillNo ON dbo.Bill;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Bill_Clinic_BillNo' AND object_id = OBJECT_ID(N'dbo.Bill'))
    CREATE UNIQUE INDEX UX_Bill_Clinic_BillNo ON dbo.Bill (ClinicID, BillNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bill_Clinic_Status' AND object_id = OBJECT_ID(N'dbo.Bill'))
    CREATE INDEX IX_Bill_Clinic_Status ON dbo.Bill (ClinicID, Status, BillDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bill_Appointment' AND object_id = OBJECT_ID(N'dbo.Bill'))
    CREATE INDEX IX_Bill_Appointment ON dbo.Bill (AppointmentID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payment_Bill' AND object_id = OBJECT_ID(N'dbo.Payment'))
    CREATE INDEX IX_Payment_Bill ON dbo.Payment (BillID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payment_Date' AND object_id = OBJECT_ID(N'dbo.Payment'))
    CREATE INDEX IX_Payment_Date ON dbo.Payment (PaymentDate);
GO

COMMIT TRANSACTION;
GO

-- 6. Number the next bills from 1 when no bill is left ---------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Bill) DBCC CHECKIDENT ('dbo.Bill', RESEED, 0) WITH NO_INFOMSGS;
IF NOT EXISTS (SELECT 1 FROM dbo.BillItem) DBCC CHECKIDENT ('dbo.BillItem', RESEED, 0) WITH NO_INFOMSGS;
GO

PRINT 'Billing schema is up to date.';
GO
