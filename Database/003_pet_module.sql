/*
    VetCare – Pet Profiles schema
    Source: Tasks/VetCare_Pet_Module.md, section 2 ("Data used").

    The Pet table already exists (created by hand). Small fixes only:
    - Species and Sex become required (rule 3).
    - UpdatedAt gets a default of GETDATE() (the same clock as CreatedAt) and becomes required.
    - Index on OwnerID: the Owners list and profile look pets up by owner.
    - Kept as they are: the datetime types, the CreatedAt / IsDeleted / SyncStatus defaults.
      There is no Status column: pets are archived (IsDeleted = 1), never deleted.
    - Written for SQL Server 2008: no THROW, OFFSET/FETCH, IIF or CONCAT.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\003_pet_module.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. Existing rows must have what becomes required ----------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pet WHERE Species IS NULL OR LTRIM(RTRIM(Species)) = ''
                                    OR Sex IS NULL OR LTRIM(RTRIM(Sex)) = '')
    RAISERROR ('Some pets have no species or sex (they become required). Migration stopped; nothing was changed.', 16, 1);
GO

UPDATE dbo.Pet SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;
GO

-- 2. Required columns -------------------------------------------------------------------------
ALTER TABLE dbo.Pet ALTER COLUMN Species   varchar(30) NOT NULL;
ALTER TABLE dbo.Pet ALTER COLUMN Sex       varchar(10) NOT NULL;
ALTER TABLE dbo.Pet ALTER COLUMN UpdatedAt datetime    NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.Pet')
                 AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Pet'), 'UpdatedAt', 'ColumnId'))
    ALTER TABLE dbo.Pet ADD CONSTRAINT DF_Pet_UpdatedAt DEFAULT GETDATE() FOR UpdatedAt;
GO

-- 3. Pets of an owner ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Pet_OwnerID' AND object_id = OBJECT_ID(N'dbo.Pet'))
    CREATE INDEX IX_Pet_OwnerID ON dbo.Pet (OwnerID);
GO

COMMIT TRANSACTION;
GO

PRINT 'Pet schema is up to date.';
GO
