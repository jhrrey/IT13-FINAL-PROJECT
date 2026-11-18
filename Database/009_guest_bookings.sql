/*
    VetCare – Guest bookings
    Source: Tasks/VetCare_Appointments_Module.md (sections 1, 2, 4.B–C2, rules 3, 8, 15) and
            Tasks/VetCare_Project_Overview.md (section 5 and the Appointment relationship note).

    A new client is booked as a guest: only the owner's name, contact no., pet name and species are kept
    on the appointment, and PetID stays empty until check-in registers (or matches) the owner and pet.

    Appointment
    - PetID becomes nullable (the foreign key to Pet stays).
    - GuestOwnerName, GuestContactNo, GuestPetName, GuestSpecies (guest bookings only).
    - CK_Appointment_PetOrGuest: a booking has a pet, or a guest pet name and species. The guest name and
      contact no. aren't required by the constraint because rule 15 clears them after 30 days.
    - IX_Appointment_GuestContact for matching a guest's contact no.

    Written for SQL Server 2008. Safe to run more than once; one transaction; -b stops on the first error.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\009_guest_bookings.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;
GO

-- 1. PetID nullable (the index on PetID doesn't block the change) ------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Appointment') AND name = N'PetID' AND is_nullable = 0)
    ALTER TABLE dbo.Appointment ALTER COLUMN PetID int NULL;
GO

-- 2. Guest columns ---------------------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.Appointment', N'GuestOwnerName') IS NULL ALTER TABLE dbo.Appointment ADD GuestOwnerName varchar(100) NULL;
IF COL_LENGTH(N'dbo.Appointment', N'GuestContactNo') IS NULL ALTER TABLE dbo.Appointment ADD GuestContactNo varchar(20) NULL;
IF COL_LENGTH(N'dbo.Appointment', N'GuestPetName') IS NULL ALTER TABLE dbo.Appointment ADD GuestPetName varchar(50) NULL;
IF COL_LENGTH(N'dbo.Appointment', N'GuestSpecies') IS NULL ALTER TABLE dbo.Appointment ADD GuestSpecies varchar(30) NULL;
GO

-- 3. A pet, or a guest pet ---------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Appointment_PetOrGuest')
    ALTER TABLE dbo.Appointment WITH CHECK ADD CONSTRAINT CK_Appointment_PetOrGuest
        CHECK (PetID IS NOT NULL OR (GuestPetName IS NOT NULL AND GuestSpecies IS NOT NULL));
GO

-- 4. Index -------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Appointment_GuestContact' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE INDEX IX_Appointment_GuestContact ON dbo.Appointment (ClinicID, GuestContactNo);
GO

COMMIT TRANSACTION;
GO

PRINT 'Guest bookings schema is up to date.';
GO
