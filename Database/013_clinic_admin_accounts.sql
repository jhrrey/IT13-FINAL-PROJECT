/*
    VetCare – Clinic Admin Accounts module (Super Admin)
    Source: Tasks/VetCare_Clinic_Admin_Accounts_Module.md, "Optional ERD additions".

    User.LastLoginAt         when the account last signed in (shown for clinic admins).
    User.MustChangePassword  1 = the account has a temporary password and must set its own at the next sign-in.
                             Existing accounts keep 0.

    Written for SQL Server 2008. Safe to run more than once.
    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -I -i Database\013_clinic_admin_accounts.sql
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'dbo.[User]', N'LastLoginAt') IS NULL
    ALTER TABLE dbo.[User] ADD LastLoginAt datetime NULL;
GO

IF COL_LENGTH(N'dbo.[User]', N'MustChangePassword') IS NULL
    ALTER TABLE dbo.[User] ADD MustChangePassword bit NOT NULL CONSTRAINT DF_User_MustChangePassword DEFAULT (0);
GO

PRINT 'Clinic admin accounts schema is up to date.';
GO
