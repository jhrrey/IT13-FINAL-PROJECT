/*
    VetCare – Devices & Sync Monitoring + System Settings
    Source: Tasks/VetCare_Devices_Sync_Monitoring_Module.md, Tasks/VetCare_System_Settings_Module.md.

    Device:          PendingChanges, LastSyncAttemptAt, DeactivatedReason (Replaced / Lost or stolen), AppVersion.
                     MachineCode is unique per clinic (one computer can serve two clinics).
    SyncLogDaily:    daily totals per device, kept when sync logs older than the retention period are removed.
    SystemSetting:   ClinicID NULL = the global value; a row with a ClinicID is that clinic's override.
                     DefaultValue is the seed value used by "Reset to default".
    SettingChangeLog: every change (old → new, who, when) for the Change history tab.

    Written for SQL Server 2008. Safe to run more than once.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -I -i Database\014_devices_settings.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

-- Device ---------------------------------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Device', N'PendingChanges') IS NULL
    ALTER TABLE dbo.Device ADD PendingChanges int NOT NULL CONSTRAINT DF_Device_PendingChanges DEFAULT (0);
IF COL_LENGTH(N'dbo.Device', N'LastSyncAttemptAt') IS NULL
    ALTER TABLE dbo.Device ADD LastSyncAttemptAt datetime NULL;
IF COL_LENGTH(N'dbo.Device', N'DeactivatedReason') IS NULL
    ALTER TABLE dbo.Device ADD DeactivatedReason varchar(50) NULL;
IF COL_LENGTH(N'dbo.Device', N'AppVersion') IS NULL
    ALTER TABLE dbo.Device ADD AppVersion varchar(20) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Device_MachineCode' AND object_id = OBJECT_ID(N'dbo.Device'))
    DROP INDEX UX_Device_MachineCode ON dbo.Device;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Device_Clinic_MachineCode' AND object_id = OBJECT_ID(N'dbo.Device'))
    CREATE UNIQUE INDEX UX_Device_Clinic_MachineCode ON dbo.Device(ClinicID, MachineCode);
GO

-- SyncLogDaily ----------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SyncLogDaily', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SyncLogDaily
    (
        DeviceID        int NOT NULL CONSTRAINT FK_SyncLogDaily_Device REFERENCES dbo.Device(DeviceID),
        [Day]           date NOT NULL,
        Successes       int NOT NULL CONSTRAINT DF_SyncLogDaily_S DEFAULT (0),
        Failures        int NOT NULL CONSTRAINT DF_SyncLogDaily_F DEFAULT (0),
        RecordsSent     int NOT NULL CONSTRAINT DF_SyncLogDaily_RS DEFAULT (0),
        RecordsReceived int NOT NULL CONSTRAINT DF_SyncLogDaily_RR DEFAULT (0),
        Conflicts       int NOT NULL CONSTRAINT DF_SyncLogDaily_C DEFAULT (0),
        CONSTRAINT PK_SyncLogDaily PRIMARY KEY (DeviceID, [Day])
    );
END
GO

-- SystemSetting ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SystemSetting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemSetting
    (
        SettingID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemSetting PRIMARY KEY,
        ClinicID            int NULL CONSTRAINT FK_SystemSetting_Clinic REFERENCES dbo.Clinic(ClinicID),
        SettingKey          varchar(100) NOT NULL,
        SettingValue        varchar(255) NOT NULL,
        DefaultValue        varchar(255) NOT NULL,
        DataType            varchar(20) NOT NULL CONSTRAINT CK_SystemSetting_Type CHECK (DataType IN ('Number', 'Text', 'Boolean', 'Time', 'Money')),
        Category            varchar(50) NOT NULL,
        AllowClinicOverride bit NOT NULL CONSTRAINT DF_SystemSetting_Override DEFAULT (0),
        UpdatedBy           int NULL CONSTRAINT FK_SystemSetting_User REFERENCES dbo.[User](UserID),
        CreatedAt           datetime NOT NULL CONSTRAINT DF_SystemSetting_Created DEFAULT (GETDATE()),
        UpdatedAt           datetime NULL,
        IsDeleted           bit NOT NULL CONSTRAINT DF_SystemSetting_Deleted DEFAULT (0),
        SyncStatus          varchar(20) NOT NULL CONSTRAINT DF_SystemSetting_Sync DEFAULT ('Synced')
    );
    -- NULLs count as equal here, so: one global row per key, at most one override per clinic.
    CREATE UNIQUE INDEX UX_SystemSetting_Key_Clinic ON dbo.SystemSetting(SettingKey, ClinicID);
END
GO

IF OBJECT_ID(N'dbo.SettingChangeLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SettingChangeLog
    (
        ChangeID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SettingChangeLog PRIMARY KEY,
        SettingKey varchar(100) NOT NULL,
        ClinicID   int NULL CONSTRAINT FK_SettingChangeLog_Clinic REFERENCES dbo.Clinic(ClinicID),
        OldValue   varchar(255) NULL,
        NewValue   varchar(255) NULL,
        ChangedBy  int NULL CONSTRAINT FK_SettingChangeLog_User REFERENCES dbo.[User](UserID),
        ChangedAt  datetime NOT NULL CONSTRAINT DF_SettingChangeLog_At DEFAULT (GETDATE())
    );
    CREATE INDEX IX_SettingChangeLog_At ON dbo.SettingChangeLog(ChangedAt);
END
GO

-- Seed the global values (spec defaults) ------------------------------------------------------------------------
DECLARE @S TABLE (K varchar(100), V varchar(255), T varchar(20), C varchar(50), O bit);
INSERT INTO @S VALUES
 ('General.SystemName', 'VetCare', 'Text', 'General', 0),
 ('General.SupportEmail', 'support@vetcare.ph', 'Text', 'General', 0),
 ('General.SupportContactNo', '0917 000 0000', 'Text', 'General', 0),
 ('General.TimeZone', 'Asia/Manila', 'Text', 'General', 0),
 ('General.Currency', 'PHP', 'Text', 'General', 0),
 ('Security.PasswordMinLength', '8', 'Number', 'Security', 0),
 ('Security.PasswordRequireMix', 'true', 'Boolean', 'Security', 0),
 ('Security.SessionTimeoutMinutes', '30', 'Number', 'Security', 1),
 ('Security.MaxFailedLogins', '5', 'Number', 'Security', 0),
 ('Security.LockMinutes', '15', 'Number', 'Security', 0),
 ('Security.OfflineLoginMaxDays', '7', 'Number', 'Security', 0),
 ('Security.ForceChangeAfterReset', 'true', 'Boolean', 'Security', 0),
 ('Sync.AutoSyncMinutes', '5', 'Number', 'Sync', 1),
 ('Sync.StaleAfterHours', '24', 'Number', 'Sync', 0),
 ('Sync.FailingAfterCount', '3', 'Number', 'Sync', 0),
 ('Sync.MaxDevicesPerClinic', '5', 'Number', 'Sync', 0),
 ('Registration.AllowSignup', 'true', 'Boolean', 'Registration', 0),
 ('Registration.RequireApproval', 'true', 'Boolean', 'Registration', 0),
 ('Clinic.ConsultationFee', '400.00', 'Money', 'ClinicDefaults', 1),
 ('Clinic.OpenTime', '08:00', 'Time', 'ClinicDefaults', 1),
 ('Clinic.CloseTime', '17:00', 'Time', 'ClinicDefaults', 1),
 ('Clinic.SlotMinutes', '30', 'Number', 'ClinicDefaults', 1),
 ('Clinic.VaccineDueSoonDays', '30', 'Number', 'ClinicDefaults', 1),
 ('Clinic.ExpiringSoonDays', '30', 'Number', 'ClinicDefaults', 1),
 ('Receipts.BillNoPrefix', 'VC', 'Text', 'Receipts', 1),
 ('Receipts.FooterMessage', 'Thank you for trusting VetCare!', 'Text', 'Receipts', 1),
 ('Retention.GuestInfoDays', '30', 'Number', 'Retention', 0),
 ('Retention.RejectedSignupDays', '30', 'Number', 'Retention', 0),
 ('Retention.SyncLogDays', '90', 'Number', 'Retention', 0);

INSERT INTO dbo.SystemSetting (ClinicID, SettingKey, SettingValue, DefaultValue, DataType, Category, AllowClinicOverride)
SELECT NULL, s.K, s.V, s.V, s.T, s.C, s.O FROM @S s
WHERE NOT EXISTS (SELECT 1 FROM dbo.SystemSetting x WHERE x.SettingKey = s.K AND x.ClinicID IS NULL);

-- Clinics that already charge a different consultation fee keep it, as their override.
INSERT INTO dbo.SystemSetting (ClinicID, SettingKey, SettingValue, DefaultValue, DataType, Category, AllowClinicOverride)
SELECT c.ClinicID, 'Clinic.ConsultationFee', CONVERT(varchar(20), c.ConsultationFee), '400.00', 'Money', 'ClinicDefaults', 1
FROM dbo.Clinic c
WHERE c.ConsultationFee <> 400 AND c.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.SystemSetting x WHERE x.SettingKey = 'Clinic.ConsultationFee' AND x.ClinicID = c.ClinicID);
GO

PRINT 'Devices and settings schema is up to date.';
GO
