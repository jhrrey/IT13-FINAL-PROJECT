namespace VetCare.Models.Entities;

/// <summary>The four summary cards on the Clinics page.</summary>
public sealed record ClinicSummary(int Total, int Active, int Pending, int Inactive);

/// <summary>Counts only for one clinic (rule 10: the Super Admin never sees records).</summary>
public sealed class ClinicOverview
{
    public int Admins { get; set; }
    public int Vets { get; set; }
    public int Staff { get; set; }
    public int Users => Admins + Vets + Staff;
    public int Owners { get; set; }
    public int Pets { get; set; }
    public int AppointmentsThisMonth { get; set; }
    public int Devices { get; set; }

    /// <summary>Active devices whose last successful sync is more than 24 hours ago (or never).</summary>
    public int StaleDevices { get; set; }

    /// <summary>The oldest last sync among the stale devices (null = never synced).</summary>
    public DateTime? OldestSync { get; set; }

    public DateTime? LastActivity { get; set; }
}

/// <summary>Another clinic that matches a sign-up or form (rule 3).</summary>
public sealed record ClinicDuplicate(int ClinicId, string ClinicName, string Status, string MatchedOn);

/// <summary>A row of the <c>Device</c> table (a registered clinic computer).</summary>
public sealed class ClinicDevice
{
    public int DeviceId { get; set; }
    public int ClinicId { get; set; }
    public string DeviceName { get; set; }
    public string MachineCode { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegisteredAt { get; set; }

    // Devices & Sync Monitoring ---------------------------------------------------------------------------

    /// <summary>Changes waiting on the computer, as it reported at its last sync.</summary>
    public int PendingChanges { get; set; }
    public DateTime? LastSyncAttemptAt { get; set; }

    /// <summary>"Replaced" or "Lost or stolen" when turned off.</summary>
    public string DeactivatedReason { get; set; }
    public string AppVersion { get; set; }
    public string ClinicName { get; set; }
    public string ClinicStatus { get; set; }

    /// <summary>How many of the last N syncs (N = Sync.FailingAfterCount) exist, and how many of them failed.</summary>
    public int RecentSyncs { get; set; }
    public int RecentFailed { get; set; }

    /// <summary>Healthy, Stale, Failing, Never synced or Inactive (SyncHealthHelper).</summary>
    public string Health { get; set; }
}

/// <summary>The summary cards of Devices & sync.</summary>
public sealed record SyncSummary(int Devices, int Healthy, int Stale, int Failing, int NeverSynced, int FailedToday, int ConflictsToday);

/// <summary>Sync log filters (Devices & sync → Sync log).</summary>
public sealed record SyncLogQuery(DateTime From, DateTime To, int? ClinicId, int? DeviceId, string Direction, string Status, int Page = 1, int PageSize = 20);

/// <summary>Clinic sync health (Devices & sync → Clinic sync health).</summary>
public sealed class ClinicSyncRow
{
    public int ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string ClinicStatus { get; set; }
    public int Devices { get; set; }
    public int Healthy { get; set; }
    public int Stale { get; set; }
    public int Failing { get; set; }
    public int FailedWeek { get; set; }
    public int ConflictsWeek { get; set; }
    public DateTime? LastSync { get; set; }
    public string Overall { get; set; }
}

/// <summary>A row of the <c>SyncLog</c> table, with the device and user names.</summary>
public sealed class SyncLogEntry
{
    public long SyncLogId { get; set; }
    public int DeviceId { get; set; }
    public string DeviceName { get; set; }
    public string UserName { get; set; }
    public string Direction { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int RecordsSent { get; set; }
    public int RecordsReceived { get; set; }
    public int ConflictsFound { get; set; }
    public string Status { get; set; }
    public string ErrorMessage { get; set; }
    public string ClinicName { get; set; }

    public double? DurationSeconds => FinishedAt == null ? null : (FinishedAt.Value - StartedAt).TotalSeconds;
}

/// <summary>Values from the Add / Edit clinic panel (the admin part only when adding).</summary>
public sealed class ClinicFormData
{
    public int? ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string Address { get; set; }
    public string ContactNo { get; set; }
    public string Email { get; set; }
    public string AdminFirstName { get; set; }
    public string AdminLastName { get; set; }
    public string AdminUsername { get; set; }
    public string AdminEmail { get; set; }
    public string Password { get; set; }
    public string ConfirmPassword { get; set; }
}
