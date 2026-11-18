using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;

namespace VetCare.Controllers;

/// <summary>
/// The sync check (Devices & Sync section 6). VetCare saves straight to the central server, so a "sync" here is the
/// computer checking in: it reports its pending changes, downloads the settings, and is logged in the SyncLog.
/// It runs at sign-in and every Sync.AutoSyncMinutes. A computer that was turned off (or whose clinic was) is refused
/// (rule 3); a check that couldn't reach the server is logged as Failed once the server answers again.
/// </summary>
public sealed class SyncAgent
{
    private readonly UserSession _session;
    private readonly DeviceRepository _devices = new();
    private readonly DashboardRepository _dashboard = new();
    private bool _running;

    public SyncAgent(UserSession session) => _session = session;

    /// <summary>Raised when the server refuses this computer; the text explains why.</summary>
    public event EventHandler<string> Refused;

    public async Task RunAsync()
    {
        if (_running || DeviceIdentity.CurrentDeviceId is not int deviceId || _session.ClinicId is not int clinicId) return;
        _running = true;
        var started = DateTime.Now;
        try
        {
            var device = await _devices.GetByIdAsync(deviceId);
            if (device == null || !device.IsActive || device.ClinicStatus != AccountStatus.Active)
            {
                if (device != null)
                    await _devices.RecordSyncAsync(deviceId, _session.UserId, "Pull", started, DateTime.Now, 0, 0, 0, "Failed",
                        "Sync refused: this computer or its clinic was turned off.", device.PendingChanges, DeviceIdentity.AppVersion);
                Refused?.Invoke(this, device == null || !device.IsActive
                    ? "This computer was turned off by your administrator. You've been signed out."
                    : "Your clinic was deactivated. You've been signed out.");
                return;
            }

            if (DeviceIdentity.MissedSyncAt is DateTime missed)
            {
                await _devices.RecordSyncAsync(deviceId, _session.UserId, "Push", missed, null, 0, 0, 0, "Failed",
                    "Couldn't reach the server.", device.PendingChanges, DeviceIdentity.AppVersion);
                DeviceIdentity.MissedSyncAt = null;
            }

            int pending = (await _dashboard.GetSyncStateAsync(clinicId)).Pending;
            await SettingsCache.LoadAsync(clinicId);
            await _devices.RecordSyncAsync(deviceId, _session.UserId, "Pull", started, DateTime.Now, 0, 0, 0, "Success", null, pending, DeviceIdentity.AppVersion);
        }
        catch (SqlException)
        {
            DeviceIdentity.MissedSyncAt ??= started;
        }
        finally
        {
            _running = false;
        }
    }
}
