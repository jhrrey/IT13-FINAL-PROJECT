/*
    VetCare – User Management schema
    Source: Tasks/VetCare_User_Management_Module.md, section 2 ("Data used").

    - Adds the fixed Role table (1 Super Admin, 2 Administrator, 3 Veterinarian, 4 Cashier/Staff).
    - User: RoleName -> RoleID (FK), FullName -> FirstName + LastName, wider columns,
      sync/audit fields (CreatedAt, UpdatedAt, IsDeleted, SyncStatus), unique Username and Email.
    - Clinic.Email widened to 100 (registration copies the administrator's email into it).
    - IDs stay INT (decided with the project owner).
    - Written for SQL Server 2008 (the version on this PC): no THROW, OFFSET/FETCH, IIF or CONCAT.

    Existing users are kept: RoleID is taken from RoleName, and FullName is split at the
    last space ("Jhon Rey Depacaquibo" -> "Jhon Rey" / "Depacaquibo").

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\001_user_management.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. Role table (fixed data) ------------------------------------------------------------
IF OBJECT_ID(N'dbo.Role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Role
    (
        RoleID   int         NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
        RoleName varchar(20) NOT NULL CONSTRAINT UQ_Role_RoleName UNIQUE
    );
END;

MERGE dbo.Role AS target
USING (VALUES (1, 'Super Admin'), (2, 'Administrator'), (3, 'Veterinarian'), (4, 'Cashier/Staff'))
      AS source (RoleID, RoleName)
ON target.RoleID = source.RoleID
WHEN NOT MATCHED THEN INSERT (RoleID, RoleName) VALUES (source.RoleID, source.RoleName);
GO

-- 2. New User columns -------------------------------------------------------------------
IF COL_LENGTH(N'dbo.[User]', N'RoleID') IS NULL
    ALTER TABLE dbo.[User] ADD RoleID int NULL;
IF COL_LENGTH(N'dbo.[User]', N'FirstName') IS NULL
    ALTER TABLE dbo.[User] ADD FirstName varchar(50) NULL;
IF COL_LENGTH(N'dbo.[User]', N'LastName') IS NULL
    ALTER TABLE dbo.[User] ADD LastName varchar(50) NULL;
IF COL_LENGTH(N'dbo.[User]', N'CreatedAt') IS NULL
    ALTER TABLE dbo.[User] ADD CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_User_CreatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.[User]', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.[User] ADD UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_User_UpdatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.[User]', N'IsDeleted') IS NULL
    ALTER TABLE dbo.[User] ADD IsDeleted bit NOT NULL CONSTRAINT DF_User_IsDeleted DEFAULT 0;
IF COL_LENGTH(N'dbo.[User]', N'SyncStatus') IS NULL
    ALTER TABLE dbo.[User] ADD SyncStatus varchar(20) NOT NULL CONSTRAINT DF_User_SyncStatus DEFAULT 'Synced';
GO

-- 3. Copy existing data into the new columns (only while the old columns still exist) --
IF COL_LENGTH(N'dbo.[User]', N'RoleName') IS NOT NULL
    EXEC (N'
        UPDATE u SET RoleID = r.RoleID
        FROM dbo.[User] u JOIN dbo.Role r ON r.RoleName = u.RoleName
        WHERE u.RoleID IS NULL;');

IF COL_LENGTH(N'dbo.[User]', N'FullName') IS NOT NULL
    EXEC (N'
        UPDATE dbo.[User]
        SET FirstName = CASE WHEN CHARINDEX('' '', LTRIM(RTRIM(FullName))) = 0
                             THEN LTRIM(RTRIM(FullName))
                             ELSE LEFT(LTRIM(RTRIM(FullName)),
                                       LEN(LTRIM(RTRIM(FullName))) - CHARINDEX('' '', REVERSE(LTRIM(RTRIM(FullName)))))
                        END,
            LastName  = CASE WHEN CHARINDEX('' '', LTRIM(RTRIM(FullName))) = 0
                             THEN ''''
                             ELSE RIGHT(LTRIM(RTRIM(FullName)),
                                        CHARINDEX('' '', REVERSE(LTRIM(RTRIM(FullName)))) - 1)
                        END
        WHERE FirstName IS NULL;');

UPDATE dbo.[User] SET Status = 'Active' WHERE Status IS NULL;
GO

-- Stop if anything could not be mapped (unknown role name, user without email).
-- (RAISERROR, not THROW: this server is SQL Server 2008. With sqlcmd -b the script stops here
--  and the open transaction is rolled back when the session ends.)
IF EXISTS (SELECT 1 FROM dbo.[User] WHERE RoleID IS NULL OR FirstName IS NULL OR LastName IS NULL)
    RAISERROR ('Some users have no RoleID or name after the backfill. Migration stopped; nothing was changed.', 16, 1);
IF EXISTS (SELECT 1 FROM dbo.[User] WHERE Email IS NULL OR LTRIM(RTRIM(Email)) = '')
    RAISERROR ('Some users have no email (Email becomes required). Migration stopped; nothing was changed.', 16, 1);
GO

-- 4. Required columns, new lengths, foreign key ----------------------------------------
ALTER TABLE dbo.[User] ALTER COLUMN RoleID    int          NOT NULL;
ALTER TABLE dbo.[User] ALTER COLUMN FirstName varchar(50)  NOT NULL;
ALTER TABLE dbo.[User] ALTER COLUMN LastName  varchar(50)  NOT NULL;
ALTER TABLE dbo.[User] ALTER COLUMN Username  varchar(50)  NOT NULL;
ALTER TABLE dbo.[User] ALTER COLUMN Email     varchar(100) NOT NULL;
ALTER TABLE dbo.[User] ALTER COLUMN ContactNo varchar(20)  NULL;
ALTER TABLE dbo.[User] ALTER COLUMN Status    varchar(20)  NOT NULL;
ALTER TABLE dbo.Clinic ALTER COLUMN Email     varchar(100) NULL;

IF OBJECT_ID(N'dbo.DF_User_Status', N'D') IS NULL
    ALTER TABLE dbo.[User] ADD CONSTRAINT DF_User_Status DEFAULT 'Active' FOR Status;

IF OBJECT_ID(N'dbo.FK_User_Role', N'F') IS NULL
    ALTER TABLE dbo.[User] ADD CONSTRAINT FK_User_Role FOREIGN KEY (RoleID) REFERENCES dbo.Role (RoleID);
GO

-- 5. Remove the old columns --------------------------------------------------------------
IF COL_LENGTH(N'dbo.[User]', N'RoleName') IS NOT NULL
    ALTER TABLE dbo.[User] DROP COLUMN RoleName;
IF COL_LENGTH(N'dbo.[User]', N'FullName') IS NOT NULL
    ALTER TABLE dbo.[User] DROP COLUMN FullName;
GO

-- 6. Username and email are unique across the whole system (rule 4) -------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_User_Username' AND object_id = OBJECT_ID(N'dbo.[User]'))
    CREATE UNIQUE INDEX UX_User_Username ON dbo.[User] (Username);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_User_Email' AND object_id = OBJECT_ID(N'dbo.[User]'))
    CREATE UNIQUE INDEX UX_User_Email ON dbo.[User] (Email);
GO

COMMIT TRANSACTION;
GO

PRINT 'User management schema is up to date.';
GO
