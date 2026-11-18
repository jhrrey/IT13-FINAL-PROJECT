namespace VetCare.Models.Entities;

/// <summary>Everything on the Super Admin Dashboard, loaded in one call (counts and statuses only).</summary>
public sealed class SuperAdminDashboardData
{
    public DateTime LoadedAt { get; set; }

    // Stat cards
    public int Clinics { get; set; }
    public int ActiveClinics { get; set; }
    public int PendingClinics { get; set; }
    public int InactiveClinics { get; set; }
    public DateTime? OldestPending { get; set; }
    public int Admins { get; set; }
    public int Vets { get; set; }
    public int Staff { get; set; }
    public int Users => Admins + Vets + Staff;
    public int Devices { get; set; }
    public int HealthyDevices { get; set; }
    public int FailedSyncsToday { get; set; }
    public int ConflictsToday { get; set; }

    /// <summary>The one clinic with failed syncs today, or null when there are none or several.</summary>
    public int? FailedSyncClinicId { get; set; }

    public IReadOnlyList<AttentionItem> Attention { get; set; } = Array.Empty<AttentionItem>();

    /// <summary>Last 6 months: label, new clinics, active clinics at the month's end.</summary>
    public IReadOnlyList<(string Month, int New, int Active)> Growth { get; set; } = Array.Empty<(string, int, int)>();

    /// <summary>Last 7 days: label, successful and failed syncs.</summary>
    public IReadOnlyList<(string Day, int Success, int Failed)> SyncDays { get; set; } = Array.Empty<(string, int, int)>();
    public double? AvgSyncSeconds { get; set; }
    public long RecordsSynced { get; set; }

    public IReadOnlyList<Clinic> RecentSignups { get; set; } = Array.Empty<Clinic>();
    public IReadOnlyList<ClinicSyncHealth> SyncHealth { get; set; } = Array.Empty<ClinicSyncHealth>();
    public PlatformUsage Usage { get; set; } = new();

    /// <summary>The last 5 System settings changes.</summary>
    public IReadOnlyList<SettingChange> SettingChanges { get; set; } = Array.Empty<SettingChange>();
}

public enum AttentionKind { PendingSignups, ClinicWithoutAdmin, FailingDevice, StaleDevice, ClinicNotSyncing, InactiveClinic }

/// <summary>One row of "Attention needed" (most urgent first).</summary>
public sealed record AttentionItem(AttentionKind Kind, string Title, string Detail, int? ClinicId, int? DeviceId = null);

/// <summary>A clinic's devices for "Clinic sync health".</summary>
public sealed class ClinicSyncHealth
{
    public int ClinicId { get; set; }
    public string ClinicName { get; set; }
    public string ClinicStatus { get; set; }
    public int Devices { get; set; }
    public int Healthy { get; set; }
    public int Stale { get; set; }
    public int Failing { get; set; }
    public DateTime? LastSync { get; set; }

    /// <summary>All good, Needs attention, Problem or Clinic inactive.</summary>
    public string Overall { get; set; }
}

/// <summary>Platform usage this month and last month (counts only, never money).</summary>
public sealed class PlatformUsage
{
    public int Appointments { get; set; }
    public int AppointmentsLast { get; set; }
    public int Pets { get; set; }
    public int PetsLast { get; set; }
    public int Records { get; set; }
    public int RecordsLast { get; set; }
    public int Bills { get; set; }
    public int BillsLast { get; set; }
}
