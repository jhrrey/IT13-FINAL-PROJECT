namespace VetCare.Models.Entities;

/// <summary>A row of the <c>SystemSetting</c> table: the global value (ClinicId null) or one clinic's override.</summary>
public sealed class SystemSetting
{
    public int SettingId { get; set; }
    public int? ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string SettingKey { get; set; }
    public string SettingValue { get; set; }
    public string DefaultValue { get; set; }
    public string DataType { get; set; }
    public string Category { get; set; }
    public bool AllowClinicOverride { get; set; }
    public string UpdatedByName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A row of the <c>SettingChangeLog</c> table (Change history).</summary>
public sealed class SettingChange
{
    public string SettingKey { get; set; }
    public int? ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string OldValue { get; set; }
    public string NewValue { get; set; }
    public string ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; }
}

public enum SettingKind { Number, Toggle, Time, Money, Text, Fixed, Options }

/// <summary>
/// The settings list (System Settings module, section 3): label, description, category, input kind, unit,
/// allowed range, override flag and whether the app follows it yet. The database holds the values.
/// </summary>
public sealed record SettingDefinition(
    string Key, string Label, string Description, string Category, SettingKind Kind, string Default,
    double Min = 0, double Max = 0, string Unit = null, bool ClinicOverride = false, int MaxLength = 0,
    int[] Options = null, bool NotEnforcedYet = false)
{
    public string RangeText => Kind switch
    {
        SettingKind.Number => $"{Min:0}–{Max:0}",
        SettingKind.Money => $"₱{Min:N0}–₱{Max:N0}",
        SettingKind.Text when MaxLength > 0 => $"Up to {MaxLength} characters",
        _ => null
    };
}

public static class SettingKeys
{
    public const string SystemName = "General.SystemName";
    public const string SupportEmail = "General.SupportEmail";
    public const string SupportContactNo = "General.SupportContactNo";
    public const string TimeZone = "General.TimeZone";
    public const string Currency = "General.Currency";
    public const string PasswordMinLength = "Security.PasswordMinLength";
    public const string PasswordRequireMix = "Security.PasswordRequireMix";
    public const string SessionTimeoutMinutes = "Security.SessionTimeoutMinutes";
    public const string MaxFailedLogins = "Security.MaxFailedLogins";
    public const string LockMinutes = "Security.LockMinutes";
    public const string OfflineLoginMaxDays = "Security.OfflineLoginMaxDays";
    public const string ForceChangeAfterReset = "Security.ForceChangeAfterReset";
    public const string AutoSyncMinutes = "Sync.AutoSyncMinutes";
    public const string StaleAfterHours = "Sync.StaleAfterHours";
    public const string FailingAfterCount = "Sync.FailingAfterCount";
    public const string MaxDevicesPerClinic = "Sync.MaxDevicesPerClinic";
    public const string AllowSignup = "Registration.AllowSignup";
    public const string RequireApproval = "Registration.RequireApproval";
    public const string ConsultationFee = "Clinic.ConsultationFee";
    public const string OpenTime = "Clinic.OpenTime";
    public const string CloseTime = "Clinic.CloseTime";
    public const string SlotMinutes = "Clinic.SlotMinutes";
    public const string VaccineDueSoonDays = "Clinic.VaccineDueSoonDays";
    public const string ExpiringSoonDays = "Clinic.ExpiringSoonDays";
    public const string BillNoPrefix = "Receipts.BillNoPrefix";
    public const string FooterMessage = "Receipts.FooterMessage";
    public const string GuestInfoDays = "Retention.GuestInfoDays";
    public const string RejectedSignupDays = "Retention.RejectedSignupDays";
    public const string SyncLogDays = "Retention.SyncLogDays";
}

public static class SettingCatalog
{
    /// <summary>Categories in menu order: key, label, icon, description.</summary>
    public static readonly (string Key, string Label, string Icon, string Description)[] Categories =
    {
        ("General", "General", "sliders-horizontal", "The name and contact details clinics see."),
        ("Security", "Security", "shield", "Password and login rules for every user."),
        ("Sync", "Sync", "refresh-cw", "How often computers sync, and when a device needs attention."),
        ("Registration", "Clinic registration", "building-2", "Who can sign up, and whether you review them first."),
        ("ClinicDefaults", "Clinic defaults", "stethoscope", "Starting values for every clinic. Clinics can set their own."),
        ("Receipts", "Receipts & numbering", "receipt", "Bill numbers and the message on printed receipts."),
        ("Retention", "Data retention", "archive", "How long VetCare keeps temporary data before clearing it.")
    };

    public static readonly IReadOnlyList<SettingDefinition> All = new SettingDefinition[]
    {
        new(SettingKeys.SystemName, "System name", "Shown in titles and printouts.", "General", SettingKind.Text, "VetCare", Min: 3, MaxLength: 50),
        new(SettingKeys.SupportEmail, "Support email", "Where clinics can reach the VetCare team.", "General", SettingKind.Text, "support@vetcare.ph", MaxLength: 100),
        new(SettingKeys.SupportContactNo, "Support contact no.", "Phone number for clinic support.", "General", SettingKind.Text, "0917 000 0000", MaxLength: 20),
        new(SettingKeys.TimeZone, "Time zone", "All dates and times use this time zone.", "General", SettingKind.Fixed, "Asia/Manila"),
        new(SettingKeys.Currency, "Currency", "Amounts are shown in Philippine pesos.", "General", SettingKind.Fixed, "PHP"),

        new(SettingKeys.PasswordMinLength, "Minimum password length", "New and reset passwords must be at least this long.", "Security", SettingKind.Number, "8", 8, 32, "characters"),
        new(SettingKeys.PasswordRequireMix, "Require a letter and a number", "Passwords must contain at least one letter and one number.", "Security", SettingKind.Toggle, "true"),
        new(SettingKeys.SessionTimeoutMinutes, "Session timeout", "Sign out after this long without activity.", "Security", SettingKind.Number, "30", 5, 240, "minutes", ClinicOverride: true, NotEnforcedYet: true),
        new(SettingKeys.MaxFailedLogins, "Failed logins before lock", "Lock the account after this many wrong passwords in a row.", "Security", SettingKind.Number, "5", 3, 10, "tries", NotEnforcedYet: true),
        new(SettingKeys.LockMinutes, "Lock time", "How long a locked account waits before trying again.", "Security", SettingKind.Number, "15", 5, 120, "minutes", NotEnforcedYet: true),
        new(SettingKeys.OfflineLoginMaxDays, "Offline login allowed for", "Days since the computer's last sync that offline sign-in still works.", "Security", SettingKind.Number, "7", 1, 30, "days", NotEnforcedYet: true),
        new(SettingKeys.ForceChangeAfterReset, "Force password change after reset", "People given a temporary password must set their own at the next sign-in.", "Security", SettingKind.Toggle, "true"),

        new(SettingKeys.AutoSyncMinutes, "Auto-sync every", "How often each clinic computer checks in with the server.", "Sync", SettingKind.Number, "5", 1, 60, "minutes", ClinicOverride: true),
        new(SettingKeys.StaleAfterHours, "Mark device as Stale after", "A computer with no successful sync for this long needs attention.", "Sync", SettingKind.Number, "24", 1, 168, "hours"),
        new(SettingKeys.FailingAfterCount, "Mark device as Failing after", "A computer whose last syncs all failed, this many in a row.", "Sync", SettingKind.Number, "3", 2, 10, "failed syncs"),
        new(SettingKeys.MaxDevicesPerClinic, "Max devices per clinic", "New computers can't be registered past this number.", "Sync", SettingKind.Number, "5", 1, 50, "devices"),

        new(SettingKeys.AllowSignup, "Allow clinics to sign up", "Show “Create account” on the sign-in page.", "Registration", SettingKind.Toggle, "true"),
        new(SettingKeys.RequireApproval, "New clinics need approval", "Review each sign-up before the clinic can sign in.", "Registration", SettingKind.Toggle, "true"),

        new(SettingKeys.ConsultationFee, "Consultation fee", "The amount a new bill starts with. Old bills keep their amounts.", "ClinicDefaults", SettingKind.Money, "400.00", 0, 10000, ClinicOverride: true),
        new(SettingKeys.OpenTime, "Opening time", "The first appointment time of the day.", "ClinicDefaults", SettingKind.Time, "08:00", ClinicOverride: true),
        new(SettingKeys.CloseTime, "Closing time", "Appointments end before this time.", "ClinicDefaults", SettingKind.Time, "17:00", ClinicOverride: true),
        new(SettingKeys.SlotMinutes, "Appointment slot length", "The length of each appointment slot.", "ClinicDefaults", SettingKind.Options, "30", Unit: "min", ClinicOverride: true, Options: new[] { 15, 20, 30, 45, 60 }),
        new(SettingKeys.VaccineDueSoonDays, "Vaccine “Due soon” window", "Vaccines due within this many days show as Due soon.", "ClinicDefaults", SettingKind.Number, "30", 7, 90, "days", ClinicOverride: true),
        new(SettingKeys.ExpiringSoonDays, "Medicine “Expiring soon” window", "Medicines expiring within this many days are flagged.", "ClinicDefaults", SettingKind.Number, "30", 7, 180, "days", ClinicOverride: true),

        new(SettingKeys.BillNoPrefix, "Bill number prefix", "Letters at the start of every bill number (2–5 letters).", "Receipts", SettingKind.Text, "VC", Min: 2, MaxLength: 5, ClinicOverride: true),
        new(SettingKeys.FooterMessage, "Receipt footer message", "Printed at the bottom of every receipt.", "Receipts", SettingKind.Text, "Thank you for trusting VetCare!", MaxLength: 100, ClinicOverride: true),

        new(SettingKeys.GuestInfoDays, "Clear guest info of no-shows and cancellations after", "Guest names and numbers are removed after this many days.", "Retention", SettingKind.Number, "30", 7, 90, "days"),
        new(SettingKeys.RejectedSignupDays, "Remove rejected clinic sign-ups after", "Rejected sign-ups are cleared after this many days.", "Retention", SettingKind.Number, "30", 7, 90, "days"),
        new(SettingKeys.SyncLogDays, "Keep sync logs for", "Older logs are removed; a daily total per computer is kept.", "Retention", SettingKind.Number, "90", 30, 365, "days")
    };

    public static SettingDefinition Get(string key) => All.FirstOrDefault(d => d.Key == key);

    public static string CategoryLabel(string category) => Categories.FirstOrDefault(c => c.Key == category).Label ?? category;

    /// <summary>A stored value as people read it ("₱400.00", "8:00 AM", "On", "30 minutes").</summary>
    public static string Display(string key, string value)
    {
        var d = Get(key);
        if (d == null || value == null) return value ?? "—";
        switch (d.Kind)
        {
            case SettingKind.Toggle: return value == "true" ? "On" : "Off";
            case SettingKind.Money: return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m) ? Helpers.CurrencyHelper.Peso(m) : value;
            case SettingKind.Time: return TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var t) ? DateTime.Today.Add(t).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : value;
            case SettingKind.Number:
            case SettingKind.Options: return value + (d.Unit == null ? string.Empty : " " + d.Unit);
            case SettingKind.Fixed: return key == SettingKeys.Currency ? "PHP (₱)" : value;
            default: return value;
        }
    }
}
