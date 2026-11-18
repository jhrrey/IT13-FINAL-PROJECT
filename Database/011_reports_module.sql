/*
    VetCare – Reports module
    Source: Tasks/VetCare_Reports_Module.md, report A5 (Guest booking report).

    Appointment.WasGuest bit: check-in clears a guest booking's guest columns once the owner and pet are registered,
    so this flag remembers that the visit started as a guest booking ("arrived & registered" in the report).
    Guest bookings still waiting, cancelled or no-show are found by PetID IS NULL.

    Written for SQL Server 2008. Safe to run more than once.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\011_reports_module.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'dbo.Appointment', N'WasGuest') IS NULL
    ALTER TABLE dbo.Appointment ADD WasGuest bit NOT NULL CONSTRAINT DF_Appointment_WasGuest DEFAULT (0);
GO

PRINT 'Reports schema is up to date.';
GO
