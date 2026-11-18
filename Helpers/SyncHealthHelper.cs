using VetCare.Models.Entities;

namespace VetCare.Helpers;

/// <summary>
/// Device health rules (Devices & Sync rule 7, Super Admin Dashboard rule 4), read from System settings:
/// Sync.StaleAfterHours (24) and Sync.FailingAfterCount (3).
/// </summary>
public static class SyncHealthHelper
{
    public const int DefaultStaleHours = 24;
    public const int DefaultFailingCount = 3;

    public static int StaleHours => SettingsCache.GetInt(SettingKeys.StaleAfterHours) is > 0 and var h ? h : DefaultStaleHours;
    public static int FailingCount => SettingsCache.GetInt(SettingKeys.FailingAfterCount) is > 0 and var n ? n : DefaultFailingCount;

    /// <summary>Active clinics whose devices haven't synced for this many days need attention.</summary>
    public const int ClinicSilentDays = 3;

    public const string AllGood = "All good";
    public const string NeedsAttention = "Needs attention";
    public const string Problem = "Problem";
    public const string ClinicInactive = "Clinic inactive";

    // Device health (calculated, not saved).
    public const string Healthy = "Healthy";
    public const string Stale = "Stale";
    public const string Failing = "Failing";
    public const string NeverSynced = "Never synced";
    public const string Inactive = "Inactive";

    /// <summary>Healthy, Stale, Failing, Never synced or Inactive (device switched off, or its clinic isn't Active).</summary>
    public static string DeviceHealth(bool isActive, string clinicStatus, DateTime? lastSync, int recentSyncs, int recentFailed, DateTime now)
    {
        if (!isActive || clinicStatus != "Active") return Inactive;
        if (recentSyncs >= FailingCount && recentFailed == recentSyncs) return Failing;
        if (lastSync == null) return NeverSynced;
        if (lastSync < now.AddHours(-StaleHours)) return Stale;
        return Healthy;
    }

    /// <summary>Problems first: Failing, Stale, Never synced, Healthy, Inactive.</summary>
    public static int HealthRank(string health) => health switch { Failing => 0, Stale => 1, NeverSynced => 2, Healthy => 3, _ => 4 };

    public static string Overall(string clinicStatus, int failing, int stale) =>
        clinicStatus != "Active" ? ClinicInactive : failing > 0 ? Problem : stale > 0 ? NeedsAttention : AllGood;

    /// <summary>Sort order: Problem, Needs attention, Clinic inactive, All good.</summary>
    public static int Rank(string overall) => overall switch { Problem => 0, NeedsAttention => 1, ClinicInactive => 2, _ => 3 };
}
