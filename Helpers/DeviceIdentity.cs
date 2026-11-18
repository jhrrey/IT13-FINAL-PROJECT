namespace VetCare.Helpers;

/// <summary>
/// This computer as a VetCare device (Devices & Sync section 3): a machine code created on first run and kept on the
/// computer, its name, and the app version. The signed-in device and a missed sync are remembered here too.
/// </summary>
public static class DeviceIdentity
{
    private const string MachineCodeKey = "vetcare.machineCode";
    private const string MissedSyncKey = "vetcare.missedSyncAt";

    /// <summary>A unique code for this computer ("VC-3F2A9C41D0B7"), created once and kept on the PC.</summary>
    public static string MachineCode
    {
        get
        {
            string code = Preferences.Default.Get(MachineCodeKey, string.Empty);
            if (string.IsNullOrEmpty(code))
            {
                code = "VC-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
                Preferences.Default.Set(MachineCodeKey, code);
            }
            return code;
        }
    }

    /// <summary>The Windows computer name, used as the first device name (the Administrator can rename it).</summary>
    public static string ComputerName
    {
        get
        {
            string name = Environment.MachineName;
            return string.IsNullOrWhiteSpace(name) ? "Clinic computer" : name.Length > 100 ? name.Substring(0, 100) : name;
        }
    }

    public static string AppVersion
    {
        get
        {
            try { return AppInfo.Current.VersionString; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>The device this sign-in uses (null for the Super Admin).</summary>
    public static int? CurrentDeviceId { get; set; }

    /// <summary>When a sync check first couldn't reach the server (saved on the PC until it's logged).</summary>
    public static DateTime? MissedSyncAt
    {
        get
        {
            long ticks = Preferences.Default.Get(MissedSyncKey, 0L);
            return ticks > 0 ? new DateTime(ticks) : null;
        }
        set
        {
            if (value == null) Preferences.Default.Remove(MissedSyncKey);
            else Preferences.Default.Set(MissedSyncKey, value.Value.Ticks);
        }
    }
}
