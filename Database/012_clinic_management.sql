/*
    VetCare – Clinic Management module (Super Admin)
    Source: Tasks/VetCare_Clinic_Management_Module.md and Tasks/VetCare_Devices_Sync_Monitoring_Module.md (Device, SyncLog).

    Clinic:
      - ClinicName 100, Address 255, ContactNo 20 (spec sizes).
      - Remarks          the reject / deactivate reason.
      - ApprovedBy/At    who approved the sign-up and when.
      - StatusChangedAt  when the status last changed (rejected sign-ups are cleared 30 days after this).
      - UpdatedAt, IsDeleted (rejected sign-ups are soft-deleted; clinics with data are never deleted).
      Clinic e-mail uniqueness is checked by ClinicController (old rows may have no e-mail).

    Clinics registered before the approval screen existed were used right away while still marked Pending.
    Those that already have an active Administrator are marked Active (approved now), so nobody is locked out.

    Device / SyncLog: the clinic's registered computers and their sync history (INT ids like every other table).

    Written for SQL Server 2008. Safe to run more than once.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -I -i Database\012_clinic_management.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

-- Clinic: wider columns -------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Clinic', N'ClinicName') < 100 ALTER TABLE dbo.Clinic ALTER COLUMN ClinicName varchar(100) NOT NULL;
IF COL_LENGTH(N'dbo.Clinic', N'Address') < 255    ALTER TABLE dbo.Clinic ALTER COLUMN Address varchar(255) NULL;
IF COL_LENGTH(N'dbo.Clinic', N'ContactNo') < 20   ALTER TABLE dbo.Clinic ALTER COLUMN ContactNo varchar(20) NULL;
IF COL_LENGTH(N'dbo.Clinic', N'Status') < 20      ALTER TABLE dbo.Clinic ALTER COLUMN Status varchar(20) NULL;
GO

-- Clinic: new columns ---------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Clinic', N'Remarks') IS NULL
    ALTER TABLE dbo.Clinic ADD Remarks varchar(255) NULL;
IF COL_LENGTH(N'dbo.Clinic', N'ApprovedBy') IS NULL
    ALTER TABLE dbo.Clinic ADD ApprovedBy int NULL CONSTRAINT FK_Clinic_ApprovedBy REFERENCES dbo.[User](UserID);
IF COL_LENGTH(N'dbo.Clinic', N'ApprovedAt') IS NULL
    ALTER TABLE dbo.Clinic ADD ApprovedAt datetime NULL;
IF COL_LENGTH(N'dbo.Clinic', N'StatusChangedAt') IS NULL
    ALTER TABLE dbo.Clinic ADD StatusChangedAt datetime NULL;
IF COL_LENGTH(N'dbo.Clinic', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.Clinic ADD UpdatedAt datetime NULL;
IF COL_LENGTH(N'dbo.Clinic', N'IsDeleted') IS NULL
    ALTER TABLE dbo.Clinic ADD IsDeleted bit NOT NULL CONSTRAINT DF_Clinic_IsDeleted DEFAULT (0);
GO

-- Clinics already in use before approval existed -> Active -----------------------------------
UPDATE c SET Status = 'Active', ApprovedAt = GETDATE(), StatusChangedAt = GETDATE(), UpdatedAt = GETDATE()
FROM dbo.Clinic c
WHERE c.Status = 'Pending' AND c.ApprovedAt IS NULL
  AND EXISTS (SELECT 1 FROM dbo.[User] u
              WHERE u.ClinicID = c.ClinicID AND u.RoleID = 2 AND u.Status = 'Active' AND u.IsDeleted = 0);
GO

-- Device ----------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Device', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Device
    (
        DeviceID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Device PRIMARY KEY,
        ClinicID     int NOT NULL CONSTRAINT FK_Device_Clinic REFERENCES dbo.Clinic(ClinicID),
        DeviceName   varchar(100) NOT NULL,
        MachineCode  varchar(100) NOT NULL,
        LastSyncAt   datetime NULL,
        IsActive     bit NOT NULL CONSTRAINT DF_Device_IsActive DEFAULT (1),
        RegisteredAt datetime NOT NULL CONSTRAINT DF_Device_RegisteredAt DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_Device_MachineCode ON dbo.Device(MachineCode);
    CREATE INDEX IX_Device_Clinic ON dbo.Device(ClinicID);
END
GO

-- SyncLog ---------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SyncLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SyncLog
    (
        SyncLogID       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SyncLog PRIMARY KEY,
        DeviceID        int NOT NULL CONSTRAINT FK_SyncLog_Device REFERENCES dbo.Device(DeviceID),
        UserID          int NULL CONSTRAINT FK_SyncLog_User REFERENCES dbo.[User](UserID),
        Direction       varchar(10) NOT NULL CONSTRAINT CK_SyncLog_Direction CHECK (Direction IN ('Push', 'Pull')),
        StartedAt       datetime NOT NULL,
        FinishedAt      datetime NULL,
        RecordsSent     int NOT NULL CONSTRAINT DF_SyncLog_Sent DEFAULT (0),
        RecordsReceived int NOT NULL CONSTRAINT DF_SyncLog_Received DEFAULT (0),
        ConflictsFound  int NOT NULL CONSTRAINT DF_SyncLog_Conflicts DEFAULT (0),
        Status          varchar(20) NOT NULL CONSTRAINT CK_SyncLog_Status CHECK (Status IN ('Success', 'Failed')),
        ErrorMessage    varchar(max) NULL
    );
    CREATE INDEX IX_SyncLog_Device ON dbo.SyncLog(DeviceID, StartedAt);
END
GO

PRINT 'Clinic management schema is up to date.';
GO
