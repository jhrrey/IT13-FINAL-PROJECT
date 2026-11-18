/*
    VetCare – Medical Records schema
    Source: Tasks/VetCare_Medical_Records_Module.md, section 2 ("Data used").

    MedicalRecord (created by hand) – fixes:
    - Weight decimal(3,2) → decimal(5,2) (max was 9.99 kg), Temperature decimal(2,1) → decimal(4,1)
      (max was 9.9 °C).
    - Diagnosis and Treatment become required (rule 5); RecordDate and UpdatedAt required with defaults.
    - One record per appointment (rule 4): unique index on AppointmentID.

    New tables (the consultation saves into them):
    - Medicine: the clinic's stock list. Category 'Vaccine' marks vaccines (rule 9). The Medicines &
      stock screens come with that module; until then rows are added by SQL (see 006_sample_medicines.sql).
    - Prescription: medicines prescribed in a record (dispensed later by Cashier/Staff).
    - Vaccination: vaccines given in a record, with the next due date.
    - MedicalRecordNote: corrections added after a record is locked (rule 11).

    IDs stay INT like the other tables. Written for SQL Server 2008: no THROW, OFFSET/FETCH, IIF or CONCAT.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\005_medical_records_module.sql
    Safe to run more than once. Everything runs in one transaction; -b stops on the first error.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. MedicalRecord --------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.MedicalRecord WHERE Diagnosis IS NULL OR Treatment IS NULL)
    RAISERROR ('Some medical records have no diagnosis or treatment (they become required). Migration stopped; nothing was changed.', 16, 1);
IF EXISTS (SELECT AppointmentID FROM dbo.MedicalRecord GROUP BY AppointmentID HAVING COUNT(*) > 1)
    RAISERROR ('Some appointments have more than one medical record (only one is allowed). Migration stopped; nothing was changed.', 16, 1);
GO

UPDATE dbo.MedicalRecord SET RecordDate = CreatedAt WHERE RecordDate IS NULL;
UPDATE dbo.MedicalRecord SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MedicalRecord_AppointmentID' AND object_id = OBJECT_ID(N'dbo.MedicalRecord'))
    DROP INDEX IX_MedicalRecord_AppointmentID ON dbo.MedicalRecord;

ALTER TABLE dbo.MedicalRecord ALTER COLUMN Weight      decimal(5,2) NULL;
ALTER TABLE dbo.MedicalRecord ALTER COLUMN Temperature decimal(4,1) NULL;
ALTER TABLE dbo.MedicalRecord ALTER COLUMN Diagnosis   varchar(max) NOT NULL;
ALTER TABLE dbo.MedicalRecord ALTER COLUMN Treatment   varchar(max) NOT NULL;
ALTER TABLE dbo.MedicalRecord ALTER COLUMN RecordDate  datetime     NOT NULL;
ALTER TABLE dbo.MedicalRecord ALTER COLUMN UpdatedAt   datetime2    NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.MedicalRecord')
                 AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MedicalRecord'), 'UpdatedAt', 'ColumnId'))
    ALTER TABLE dbo.MedicalRecord ADD CONSTRAINT DF_MedicalRecord_UpdatedAt DEFAULT SYSDATETIME() FOR UpdatedAt;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MedicalRecord_AppointmentID' AND object_id = OBJECT_ID(N'dbo.MedicalRecord'))
    CREATE UNIQUE INDEX UX_MedicalRecord_AppointmentID ON dbo.MedicalRecord (AppointmentID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MedicalRecord_PetID' AND object_id = OBJECT_ID(N'dbo.MedicalRecord'))
    CREATE INDEX IX_MedicalRecord_PetID ON dbo.MedicalRecord (PetID);
GO

-- 2. Medicine (clinic stock list) ------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Medicine', N'U') IS NULL
    CREATE TABLE dbo.Medicine (
        MedicineID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Medicine PRIMARY KEY,
        ClinicID     int           NOT NULL CONSTRAINT FK_Medicine_Clinic REFERENCES dbo.Clinic (ClinicID),
        MedicineName varchar(100)  NOT NULL,
        Category     varchar(30)   NOT NULL,          -- Tablet, Capsule, Syrup, Injection, Topical, Vaccine, Other
        Unit         varchar(20)   NOT NULL,          -- tab, cap, ml, tube, dose …
        StockQty     int           NOT NULL CONSTRAINT DF_Medicine_StockQty DEFAULT 0,
        ReorderLevel int           NOT NULL CONSTRAINT DF_Medicine_ReorderLevel DEFAULT 0,
        UnitPrice    decimal(10,2) NOT NULL CONSTRAINT DF_Medicine_UnitPrice DEFAULT 0,
        ExpiryDate   date          NULL,
        CreatedAt    datetime      NOT NULL CONSTRAINT DF_Medicine_CreatedAt DEFAULT GETDATE(),
        UpdatedAt    datetime      NOT NULL CONSTRAINT DF_Medicine_UpdatedAt DEFAULT GETDATE(),
        IsDeleted    bit           NOT NULL CONSTRAINT DF_Medicine_IsDeleted DEFAULT 0,
        SyncStatus   varchar(20)   NOT NULL CONSTRAINT DF_Medicine_SyncStatus DEFAULT 'Synced'
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Medicine_Clinic_Category' AND object_id = OBJECT_ID(N'dbo.Medicine'))
    CREATE INDEX IX_Medicine_Clinic_Category ON dbo.Medicine (ClinicID, Category);
GO

-- 3. Prescription ----------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Prescription', N'U') IS NULL
    CREATE TABLE dbo.Prescription (
        PrescriptionID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prescription PRIMARY KEY,
        RecordID       int          NOT NULL CONSTRAINT FK_Prescription_MedicalRecord REFERENCES dbo.MedicalRecord (RecordID),
        MedicineID     int          NOT NULL CONSTRAINT FK_Prescription_Medicine REFERENCES dbo.Medicine (MedicineID),
        Quantity       int          NOT NULL CONSTRAINT CK_Prescription_Quantity CHECK (Quantity > 0),
        Dosage         varchar(100) NULL,
        Instructions   varchar(255) NULL,
        IsDispensed    bit          NOT NULL CONSTRAINT DF_Prescription_IsDispensed DEFAULT 0,
        DispensedAt    datetime     NULL,
        DispensedBy    int          NULL CONSTRAINT FK_Prescription_DispensedBy REFERENCES dbo.[User] (UserID),
        CreatedAt      datetime     NOT NULL CONSTRAINT DF_Prescription_CreatedAt DEFAULT GETDATE(),
        UpdatedAt      datetime     NOT NULL CONSTRAINT DF_Prescription_UpdatedAt DEFAULT GETDATE(),
        IsDeleted      bit          NOT NULL CONSTRAINT DF_Prescription_IsDeleted DEFAULT 0,
        SyncStatus     varchar(20)  NOT NULL CONSTRAINT DF_Prescription_SyncStatus DEFAULT 'Synced'
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Prescription_RecordID' AND object_id = OBJECT_ID(N'dbo.Prescription'))
    CREATE INDEX IX_Prescription_RecordID ON dbo.Prescription (RecordID);
GO

-- 4. Vaccination -----------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Vaccination', N'U') IS NULL
    CREATE TABLE dbo.Vaccination (
        VaccinationID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vaccination PRIMARY KEY,
        PetID         int         NOT NULL CONSTRAINT FK_Vaccination_Pet REFERENCES dbo.Pet (PetID),
        RecordID      int         NULL CONSTRAINT FK_Vaccination_MedicalRecord REFERENCES dbo.MedicalRecord (RecordID),
        MedicineID    int         NOT NULL CONSTRAINT FK_Vaccination_Medicine REFERENCES dbo.Medicine (MedicineID),
        DoseNo        int         NOT NULL CONSTRAINT DF_Vaccination_DoseNo DEFAULT 1,
        DateGiven     date        NOT NULL,
        NextDueDate   date        NULL,
        GivenBy       int         NOT NULL CONSTRAINT FK_Vaccination_GivenBy REFERENCES dbo.[User] (UserID),
        CreatedAt     datetime    NOT NULL CONSTRAINT DF_Vaccination_CreatedAt DEFAULT GETDATE(),
        UpdatedAt     datetime    NOT NULL CONSTRAINT DF_Vaccination_UpdatedAt DEFAULT GETDATE(),
        IsDeleted     bit         NOT NULL CONSTRAINT DF_Vaccination_IsDeleted DEFAULT 0,
        SyncStatus    varchar(20) NOT NULL CONSTRAINT DF_Vaccination_SyncStatus DEFAULT 'Synced'
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vaccination_PetID' AND object_id = OBJECT_ID(N'dbo.Vaccination'))
    CREATE INDEX IX_Vaccination_PetID ON dbo.Vaccination (PetID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vaccination_RecordID' AND object_id = OBJECT_ID(N'dbo.Vaccination'))
    CREATE INDEX IX_Vaccination_RecordID ON dbo.Vaccination (RecordID);
GO

-- 5. MedicalRecordNote (corrections after a record is locked) --------------------------------------
IF OBJECT_ID(N'dbo.MedicalRecordNote', N'U') IS NULL
    CREATE TABLE dbo.MedicalRecordNote (
        NoteID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicalRecordNote PRIMARY KEY,
        RecordID   int          NOT NULL CONSTRAINT FK_MedicalRecordNote_MedicalRecord REFERENCES dbo.MedicalRecord (RecordID),
        VetID      int          NOT NULL CONSTRAINT FK_MedicalRecordNote_Vet REFERENCES dbo.[User] (UserID),
        NoteText   varchar(max) NOT NULL,
        CreatedAt  datetime     NOT NULL CONSTRAINT DF_MedicalRecordNote_CreatedAt DEFAULT GETDATE(),
        SyncStatus varchar(20)  NOT NULL CONSTRAINT DF_MedicalRecordNote_SyncStatus DEFAULT 'Synced'
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MedicalRecordNote_RecordID' AND object_id = OBJECT_ID(N'dbo.MedicalRecordNote'))
    CREATE INDEX IX_MedicalRecordNote_RecordID ON dbo.MedicalRecordNote (RecordID);
GO

COMMIT TRANSACTION;
GO

PRINT 'Medical records schema is up to date.';
GO
