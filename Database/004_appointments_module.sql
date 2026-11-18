/*
    VetCare – Appointments schema
    Source: Tasks/VetCare_Appointments_Module.md, section 2 ("Data used").

    The Appointment table already exists (created by hand). Changes:
    - Status varchar(12) → varchar(20) NOT NULL, default 'Scheduled' ("In Consultation" is 15 letters).
    - Reason stays varchar(100) (by choice) but becomes required (rule 3).
    - UpdatedAt gets a default of GETDATE() and becomes required.
    - Status timeline: CheckedInAt, StartedAt, CompletedAt, CancelledAt, CancelReason.
    - Indexes: a vet's day (double-booking check, rule 4), the clinic's day, a pet's visits,
      and MedicalRecord by appointment (rule 10: completing needs a record).
    - Written for SQL Server 2008: no THROW, OFFSET/FETCH, IIF or CONCAT.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\004_appointments_module.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. Existing rows must have what becomes required -------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Appointment WHERE Reason IS NULL OR LTRIM(RTRIM(Reason)) = '')
    RAISERROR ('Some appointments have no reason (it becomes required). Migration stopped; nothing was changed.', 16, 1);
GO

UPDATE dbo.Appointment SET Status = 'Scheduled' WHERE Status IS NULL OR LTRIM(RTRIM(Status)) = '';
UPDATE dbo.Appointment SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;
GO

-- 2. Lengths and required columns ---------------------------------------------------------
ALTER TABLE dbo.Appointment ALTER COLUMN Status    varchar(20)  NOT NULL;
ALTER TABLE dbo.Appointment ALTER COLUMN Reason    varchar(100) NOT NULL;
ALTER TABLE dbo.Appointment ALTER COLUMN UpdatedAt datetime     NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.Appointment')
                 AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Appointment'), 'Status', 'ColumnId'))
    ALTER TABLE dbo.Appointment ADD CONSTRAINT DF_Appointment_Status DEFAULT 'Scheduled' FOR Status;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.Appointment')
                 AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Appointment'), 'UpdatedAt', 'ColumnId'))
    ALTER TABLE dbo.Appointment ADD CONSTRAINT DF_Appointment_UpdatedAt DEFAULT GETDATE() FOR UpdatedAt;
GO

-- 3. Status timeline ----------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Appointment', N'CheckedInAt') IS NULL  ALTER TABLE dbo.Appointment ADD CheckedInAt datetime NULL;
IF COL_LENGTH(N'dbo.Appointment', N'StartedAt') IS NULL    ALTER TABLE dbo.Appointment ADD StartedAt datetime NULL;
IF COL_LENGTH(N'dbo.Appointment', N'CompletedAt') IS NULL  ALTER TABLE dbo.Appointment ADD CompletedAt datetime NULL;
IF COL_LENGTH(N'dbo.Appointment', N'CancelledAt') IS NULL  ALTER TABLE dbo.Appointment ADD CancelledAt datetime NULL;
IF COL_LENGTH(N'dbo.Appointment', N'CancelReason') IS NULL ALTER TABLE dbo.Appointment ADD CancelReason varchar(255) NULL;
GO

-- 4. Indexes --------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Appointment_Vet_Date' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE INDEX IX_Appointment_Vet_Date ON dbo.Appointment (VetID, AppointmentDate, AppointmentTime);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Appointment_Clinic_Date' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE INDEX IX_Appointment_Clinic_Date ON dbo.Appointment (ClinicID, AppointmentDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Appointment_PetID' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE INDEX IX_Appointment_PetID ON dbo.Appointment (PetID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MedicalRecord_AppointmentID' AND object_id = OBJECT_ID(N'dbo.MedicalRecord'))
    CREATE INDEX IX_MedicalRecord_AppointmentID ON dbo.MedicalRecord (AppointmentID);
GO

COMMIT TRANSACTION;
GO

PRINT 'Appointment schema is up to date.';
GO
