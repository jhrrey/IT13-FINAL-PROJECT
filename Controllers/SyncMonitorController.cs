using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.SuperAdmin;

namespace VetCare.Controllers;

/// <summary>
/// Devices & Sync Monitoring (Tasks/VetCare_Devices_Sync_Monitoring_Module.md, section 5):
///  1. the Super Admin sees every clinic; an Administrator only their own (Session.ClinicId);
///  2. read-only for clinic data: it only turns devices on or off (and renames, for Administrators);
///  3. a turned-off device can't sign in and its next sync is refused (AuthController, SyncAgent);
///  4. "Lost or stolen" needs confirmation and its unsynced changes aren't accepted;
///  5. a device can only be reactivated while its clinic is Active;  6. an inactive clinic's devices show as Inactive;
///  7. health uses System settings (stale hours, failing count);  8. conflicts are counts only;
///  9. errors are technical only;  10. old logs are rolled into daily totals after Retention.SyncLogDays;
/// 11. online only.
/// </summary>
public sealed class SyncMonitorController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";
    private const string CleanupDayKey = "vetcare.syncCleanupDay";

    private readonly UserSession _session;
    private readonly DeviceRepository _devices;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private ContentView _dialogLayer;
    private Toast _toast;
    private DevicesSyncPage _page;
    private Func<IDictionary<string, string>, Task> _pendingConfirm;
    private int _logPage = 1;

    public SyncMonitorController(UserSession session, DeviceRepository devices, ClinicRepository clinics)
    {
        _session = session;
        _devices = devices;
        _clinics = clinics;
    }

    /// <summary>"device:{id}", "health:{Health}", "clinic:{id}", "tab:sync:failed:today".</summary>
    public string StartIntent { get; init; }

    private bool AllClinics => _session.IsSuperAdmin;
    private int? Scope => AllClinics ? null : _session.ClinicId;
    private bool Allowed => _session.IsSuperAdmin || (_session.IsAdministrator && _session.ClinicId != null);
    private bool InScope(ClinicDevice d) => d != null && (AllClinics || d.ClinicId == _session.ClinicId);

    // Data (section 7) -----------------------------------------------------------------------------------------------

    public async Task<SyncSummary> GetSyncSummaryAsync()
    {
        var devices = await _devices.GetDevicesAsync(Scope, null);
        var (failed, conflicts) = await _devices.GetTodayAsync(Scope, DateTime.Today);
        return new SyncSummary(devices.Count,
            devices.Count(d => d.Health == SyncHealthHelper.Healthy),
            devices.Count(d => d.Health == SyncHealthHelper.Stale),
            devices.Count(d => d.Health == SyncHealthHelper.Failing),
            devices.Count(d => d.Health == SyncHealthHelper.NeverSynced),
            failed, conflicts);
    }

    public Task<IReadOnlyList<(DateTime Day, int Success, int Failed)>> GetSyncChartAsync(int days) => _devices.GetChartAsync(Scope, DateTime.Today, days);

    /// <summary>Devices matching the search, clinic and health, problems first (Failing, Stale, Never synced, Healthy, Inactive).</summary>
    public async Task<IReadOnlyList<ClinicDevice>> GetDevicesAsync(string search, int? clinicId, string health)
    {
        var list = await _devices.GetDevicesAsync(AllClinics ? clinicId : Scope, search);
        return list.Where(d => health == null || d.Health == health)
            .OrderBy(d => SyncHealthHelper.HealthRank(d.Health)).ThenBy(d => d.ClinicName).ThenBy(d => d.DeviceName).ToList();
    }

    public async Task<ClinicDevice> GetDeviceByIdAsync(int deviceId)
    {
        var d = await _devices.GetByIdAsync(deviceId);
        return InScope(d) ? d : null;
    }

    public Task<IReadOnlyList<SyncLogEntry>> GetDeviceSyncHistoryAsync(int deviceId, int count) => _devices.GetHistoryAsync(deviceId, count);

    public Task<PagedResult<SyncLogEntry>> GetSyncLogsAsync(SyncLogQuery q) =>
        _devices.GetLogsPageAsync(AllClinics ? q : q with { ClinicId = _session.ClinicId });

    public async Task<IReadOnlyList<ClinicSyncRow>> GetClinicSyncHealthAsync()
    {
        if (!AllClinics) return Array.Empty<ClinicSyncRow>();
        var devices = await _devices.GetDevicesAsync(null, null);
        var week = await _devices.GetWeekByClinicAsync(DateTime.Today);
        return devices.GroupBy(d => (d.ClinicId, d.ClinicName, d.ClinicStatus)).Select(g =>
        {
            int stale = g.Count(d => d.Health == SyncHealthHelper.Stale || d.Health == SyncHealthHelper.NeverSynced);
            int failing = g.Count(d => d.Health == SyncHealthHelper.Failing);
            var (f, c) = week.TryGetValue(g.Key.ClinicId, out var w) ? w : (0, 0);
            return new ClinicSyncRow
            {
                ClinicId = g.Key.ClinicId,
                ClinicName = g.Key.ClinicName,
                ClinicStatus = g.Key.ClinicStatus,
                Devices = g.Count(),
                Healthy = g.Count(d => d.Health == SyncHealthHelper.Healthy),
                Stale = stale,
                Failing = failing,
                FailedWeek = f,
                ConflictsWeek = c,
                LastSync = g.Max(d => d.LastSyncAt),
                Overall = SyncHealthHelper.Overall(g.Key.ClinicStatus, failing, stale)
            };
        }).OrderBy(r => SyncHealthHelper.Rank(r.Overall)).ThenBy(r => r.ClinicName).ToList();
    }

    public async Task<OperationResult> DeactivateDeviceAsync(int deviceId, string reason)
    {
        if (!Allowed) return OperationResult.Fail("You can’t change devices.");
        if (!await ClinicController.IsOnlineAsync()) return OperationResult.Fail("Connect to the internet to change devices.");   // rule 11
        var d = await GetDeviceByIdAsync(deviceId);
        if (d == null) return OperationResult.Fail("This device no longer exists.");
        await _devices.DeactivateAsync(deviceId, reason);                                                                          // rules 3, 4
        return OperationResult.Success(d.DeviceName + (reason == DeactivateDeviceDialog.LostOrStolen
            ? " was blocked. Its unsynced changes will be refused."
            : " was turned off. It can no longer sign in or sync."));
    }

    public async Task<OperationResult> ReactivateDeviceAsync(int deviceId)
    {
        if (!Allowed) return OperationResult.Fail("You can’t change devices.");
        if (!await ClinicController.IsOnlineAsync()) return OperationResult.Fail("Connect to the internet to change devices.");
        var d = await GetDeviceByIdAsync(deviceId);
        if (d == null) return OperationResult.Fail("This device no longer exists.");
        if (d.ClinicStatus != AccountStatus.Active) return OperationResult.Fail("Activate " + d.ClinicName + " first.");           // rule 5
        await _devices.ReactivateAsync(deviceId);
        return OperationResult.Success(d.DeviceName + " can sign in and sync again.");
    }

    /// <summary>Administrators rename their clinic's computers (rule 2).</summary>
    public async Task<OperationResult> RenameDeviceAsync(int deviceId, string newName)
    {
        if (!_session.IsAdministrator) return OperationResult.Fail("Only the clinic's Administrator can rename its computers.");
        if (string.IsNullOrWhiteSpace(newName)) return OperationResult.Fail("Enter a name.");
        if (newName.Length > 100) return OperationResult.Fail("Use 100 characters or fewer.");
        var d = await GetDeviceByIdAsync(deviceId);
        if (d == null) return OperationResult.Fail("This device no longer exists.");
        await _devices.RenameAsync(deviceId, newName.Trim());
        return OperationResult.Success("Renamed to " + newName.Trim() + ".");
    }

    /// <summary>Rule 10: once a day (when the module opens), roll old logs into daily totals and remove them.</summary>
    public async Task CleanupOldLogsAsync()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (Preferences.Default.Get(CleanupDayKey, string.Empty) == today) return;
        try
        {
            await _devices.CleanupAsync(SettingsCache.GetInt(SettingKeys.SyncLogDays));
            Preferences.Default.Set(CleanupDayKey, today);
        }
        catch (SqlException) { /* tried again next time */ }
    }

    // View --------------------------------------------------------------------------------------------------------------

    public View CreateView()
    {
        if (!Allowed)
            return new Label { Text = "Devices & sync is for the Super Admin and clinic Administrators.", FontSize = 16, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(40) };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) =>
        {
            var action = _pendingConfirm;
            if (action == null) { await CloseDialogAsync(); return; }
            await action(e.Values ?? new Dictionary<string, string>());
        };

        _page = new DevicesSyncPage(AllClinics);
        _page.RefreshRequested += async (s, e) => await RefreshAsync();
        _page.FiltersChanged += async (s, e) => { _logPage = 1; await LoadTabAsync(); };
        _page.TabChanged += async (s, e) => { _logPage = 1; await LoadTabAsync(); };
        _page.PageRequested += async (s, p) => { _logPage = p; await LoadTabAsync(); };
        _page.DeviceSelected += async (s, d) => await OpenDetailsAsync(d.DeviceId);
        _page.DeviceToggleRequested += async (s, d) => await ToggleAsync(d);
        _page.DeviceMenuRequested += (s, e) => _host.Menu.Show(e.Anchor, new List<MenuEntry>
        {
            new("View details", "eye", () => _ = OpenDetailsAsync(e.Device.DeviceId)),
            e.Device.IsActive
                ? new MenuEntry("Deactivate", "ban", () => _ = ToggleAsync(e.Device), Danger: true)
                : new MenuEntry("Reactivate", "power", () => _ = ToggleAsync(e.Device))
        });
        _page.ClinicSelected += async (s, id) =>
        {
            _page.Preset(clinicId: id);
            _page.OpenTab(DevicesSyncPage.DevicesTab);
            await Task.CompletedTask;
        };
        _host.ShowPage(_page);

        _dialogLayer = new ContentView { IsVisible = false };
        _toast = new Toast();
        _ = StartAsync();
        return new Grid { Children = { _host, _dialogLayer, _toast } };
    }

    private async Task StartAsync()
    {
        await CleanupOldLogsAsync();
        // Dashboard shortcuts.
        string intent = StartIntent ?? string.Empty;
        var parts = intent.Split(':');
        if (parts[0] == "health" && parts.Length > 1) _page.Preset(health: parts[1]);
        if (parts[0] == "clinic" && parts.Length > 1 && int.TryParse(parts[1], out int clinicId)) _page.Preset(clinicId: clinicId);
        if (intent == "tab:sync:failed:today")
        {
            _page.Preset(logDay: DateTime.Today, logStatus: "Failed");
            _page.OpenTab(DevicesSyncPage.LogTab);   // loads the tab
            await RefreshSummaryAsync();
            return;
        }
        await RefreshAsync();
        if (parts[0] == "device" && parts.Length > 1 && int.TryParse(parts[1], out int deviceId)) await OpenDetailsAsync(deviceId);
    }

    private async Task RefreshAsync()
    {
        await RefreshSummaryAsync();
        await LoadTabAsync();
    }

    private async Task RefreshSummaryAsync()
    {
        try
        {
            _page.ShowSummary(await GetSyncSummaryAsync());
            _page.ShowChart(await GetSyncChartAsync(7));
            var devices = await _devices.GetDevicesAsync(Scope, null);
            var clinics = AllClinics ? (await _clinics.GetAllAsync()).ToList() : new List<Clinic>();
            _page.SetPickers(clinics, devices);
            _page.ShowUpdated(DateTime.Now);
        }
        catch (SqlException)
        {
            _page.ShowError(DatabaseError);
        }
    }

    private async Task LoadTabAsync()
    {
        _page.ShowLoading();
        try
        {
            switch (_page.CurrentTab)
            {
                case DevicesSyncPage.LogTab:
                    _page.ShowLogs(await GetSyncLogsAsync(_page.ReadLogQuery(_logPage)));
                    break;
                case DevicesSyncPage.HealthTab:
                    _page.ShowClinicHealth(await GetClinicSyncHealthAsync());
                    break;
                default:
                    _page.ShowDevices(await GetDevicesAsync(_page.SearchText, _page.ClinicFilter, _page.HealthFilter));
                    break;
            }
        }
        catch (SqlException)
        {
            _page.ShowError(DatabaseError);
        }
    }

    private async Task OpenDetailsAsync(int deviceId)
    {
        ClinicDevice device;
        (int, int) rate;
        IReadOnlyList<SyncLogEntry> history;
        try
        {
            device = await GetDeviceByIdAsync(deviceId);
            if (device == null) return;
            rate = await _devices.GetSuccessRateAsync(deviceId, DateTime.Today.AddDays(-6));
            history = await GetDeviceSyncHistoryAsync(deviceId, 10);
        }
        catch (SqlException) { _page.ShowError(DatabaseError); return; }

        var view = new DeviceDetailsView(device, rate, history, canRename: _session.IsAdministrator);
        view.CloseRequested += async (s, e) => await _host.Panel.HideAsync();
        view.DeactivateRequested += (s, e) => ShowDeactivate(device);
        view.ReactivateRequested += async (s, e) => await ConfirmReactivateAsync(device);
        view.RenameRequested += async (s, name) =>
        {
            try
            {
                var result = await RenameDeviceAsync(device.DeviceId, name);
                if (!result.Ok) { view.ShowRenameError(result.Message); return; }
                _toast.Show(result.Message);
                await RefreshAsync();
                await OpenDetailsAsync(device.DeviceId);
            }
            catch (SqlException) { view.ShowRenameError(DatabaseError); }
        };
        await _host.Panel.ShowAsync(view, DeviceDetailsView.PanelWidth);
    }

    private async Task ToggleAsync(ClinicDevice device)
    {
        if (device.IsActive) ShowDeactivate(device);
        else await ConfirmReactivateAsync(device);
    }

    private void ShowDeactivate(ClinicDevice device)
    {
        var dialog = new DeactivateDeviceDialog(device);
        dialog.CancelRequested += (s, e) => CloseDeactivate();
        dialog.ConfirmRequested += async (s, reason) =>
        {
            dialog.SetBusy(true);
            try
            {
                var result = await DeactivateDeviceAsync(device.DeviceId, reason);
                if (!result.Ok) { dialog.ShowMessage(result.Message); return; }
                CloseDeactivate();
                await _host.Panel.HideAsync();
                _toast.Show(result.Message);
                await RefreshAsync();
            }
            catch (SqlException) { dialog.ShowMessage(DatabaseError); }
            finally { dialog.SetBusy(false); }
        };
        _dialogLayer.Content = dialog;
        _dialogLayer.IsVisible = true;
    }

    private void CloseDeactivate()
    {
        _dialogLayer.IsVisible = false;
        _dialogLayer.Content = null;
    }

    private async Task ConfirmReactivateAsync(ClinicDevice device)
    {
        if (device.ClinicStatus != AccountStatus.Active)
        {
            _pendingConfirm = null;
            await _host.Dialog.ShowConfirmAsync("Can’t reactivate " + device.DeviceName, "Activate " + device.ClinicName + " first.", "OK",
                danger: false, icon: "building-2", showCancel: false);
            return;
        }
        _pendingConfirm = async _ =>
        {
            _host.Dialog.SetBusy(true);
            try
            {
                var result = await ReactivateDeviceAsync(device.DeviceId);
                if (!result.Ok) { _host.Dialog.SetBusy(false); _host.Dialog.ShowResult(result); return; }
                await CloseDialogAsync();
                await _host.Panel.HideAsync();
                _toast.Show(result.Message);
                await RefreshAsync();
            }
            catch (SqlException)
            {
                _host.Dialog.SetBusy(false);
                _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError));
            }
        };
        await _host.Dialog.ShowConfirmAsync("Reactivate " + device.DeviceName + "?", "It can sign in and sync again.", "Reactivate",
            danger: false, icon: "monitor");
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
