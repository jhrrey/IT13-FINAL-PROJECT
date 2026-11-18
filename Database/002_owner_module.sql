/*
    VetCare – Owner Records schema
    Source: Tasks/VetCare_Owner_Module.md, section 2 ("Data used").

    - Owner: FirstName/LastName 50, ContactNo 20 (required), Email 100, Address 255,
      DateRegistered required (set automatically), and the sync/archive fields
      CreatedAt, UpdatedAt, IsDeleted, SyncStatus.
    - Index on (ClinicID, ContactNo) for the duplicate-contact check (a warning, not a block).
    - IDs stay INT (same decision as the User table).
    - Written for SQL Server 2008: no THROW, OFFSET/FETCH, IIF or CONCAT.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\002_owner_module.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. New columns ---------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Owner', N'CreatedAt') IS NULL
    ALTER TABLE dbo.Owner ADD CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Owner_CreatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.Owner', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.Owner ADD UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Owner_UpdatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.Owner', N'IsDeleted') IS NULL
    ALTER TABLE dbo.Owner ADD IsDeleted bit NOT NULL CONSTRAINT DF_Owner_IsDeleted DEFAULT 0;
IF COL_LENGTH(N'dbo.Owner', N'SyncStatus') IS NULL
    ALTER TABLE dbo.Owner ADD SyncStatus varchar(20) NOT NULL CONSTRAINT DF_Owner_SyncStatus DEFAULT 'Synced';
GO

-- 2. Existing rows must have what becomes required --------------------------------------
UPDATE dbo.Owner SET DateRegistered = GETDATE() WHERE DateRegistered IS NULL;
IF EXISTS (SELECT 1 FROM dbo.Owner WHERE ContactNo IS NULL OR LTRIM(RTRIM(ContactNo)) = '')
    RAISERROR ('Some owners have no contact number (it becomes required). Migration stopped; nothing was changed.', 16, 1);
GO

-- 3. Lengths and required columns --------------------------------------------------------
-- The index uses ContactNo, so drop it before changing the column and recreate it after.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Owner_Clinic_ContactNo' AND object_id = OBJECT_ID(N'dbo.Owner'))
    DROP INDEX IX_Owner_Clinic_ContactNo ON dbo.Owner;

ALTER TABLE dbo.Owner ALTER COLUMN FirstName      varchar(50)  NOT NULL;
ALTER TABLE dbo.Owner ALTER COLUMN LastName       varchar(50)  NOT NULL;
ALTER TABLE dbo.Owner ALTER COLUMN ContactNo      varchar(20)  NOT NULL;
ALTER TABLE dbo.Owner ALTER COLUMN Email          varchar(100) NULL;
ALTER TABLE dbo.Owner ALTER COLUMN Address        varchar(255) NULL;
ALTER TABLE dbo.Owner ALTER COLUMN DateRegistered datetime     NOT NULL;

-- DateRegistered already had a default of GETDATE() (from the original table); add one if missing.
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.Owner')
                 AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Owner'), 'DateRegistered', 'ColumnId'))
    ALTER TABLE dbo.Owner ADD CONSTRAINT DF_Owner_DateRegistered DEFAULT GETDATE() FOR DateRegistered;
GO

-- 4. Duplicate-contact lookups within a clinic (rule 5) ------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Owner_Clinic_ContactNo' AND object_id = OBJECT_ID(N'dbo.Owner'))
    CREATE INDEX IX_Owner_Clinic_ContactNo ON dbo.Owner (ClinicID, ContactNo);
GO

COMMIT TRANSACTION;
GO

PRINT 'Owner schema is up to date.';
GO
