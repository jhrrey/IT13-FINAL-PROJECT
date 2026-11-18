using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.SuperAdmin;

namespace VetCare.Controllers;

/// <summary>
/// Super Admin Dashboard (Tasks/VetCare_Super_Admin_Dashboard.md, section 3):
///  1. Super Admin only;  2. counts and statuses only, never clinic records or money;
///  3. users of Rejected clinics and archived records aren't counted;
///  4. stale / failing use System Settings (spec defaults until that module exists);
///  5. calculated when the page opens and every 60 seconds;  6. every card and row opens the related module;
///  7. read-only;  8. online only: when the server can't be reached, the last numbers stay with a warning.
/// </summary>
public sealed partial class DashboardController
{
    private SuperAdminDashboardPage _superPage;
    private SuperAdminDashboardData _lastSuperData;
    private int _superRefreshNumber;

    public bool CanViewSuperAdmin => _session.IsSuperAdmin;

    // Data (section 4) -----------------------------------------------------------------------------------------------

    public async Task<SuperAdminDashboardData> GetSuperAdminDashboardAsync()
    {
        var data = await _dashboard.GetSuperAdminAsync(DateTime.Today, SyncHealthHelper.StaleHours, SyncHealthHelper.FailingCount);
        data.SettingChanges = await new Models.Repositories.SettingRepository().GetRecentChangesAsync(5);
        return data;
    }

    public async Task<SuperAdminDashboardData> GetPlatformStatsAsync() => await GetSuperAdminDashboardAsync();
    public async Task<IReadOnlyList<AttentionItem>> GetAttentionItemsAsync() => (await GetSuperAdminDashboardAsync()).Attention;
    public async Task<IReadOnlyList<(string Month, int New, int Active)>> GetClinicGrowthAsync() => (await GetSuperAdminDashboardAsync()).Growth;
    public async Task<IReadOnlyList<(string Day, int Success, int Failed)>> GetSyncActivityAsync() => (await GetSuperAdminDashboardAsync()).SyncDays;
    public async Task<(int Admins, int Vets, int Staff)> GetUsersByRoleAsync() { var d = await GetSuperAdminDashboardAsync(); return (d.Admins, d.Vets, d.Staff); }
    public async Task<IReadOnlyList<Clinic>> GetRecentSignupsAsync() => (await GetSuperAdminDashboardAsync()).RecentSignups;
    public async Task<IReadOnlyList<ClinicSyncHealth>> GetClinicSyncHealthAsync() => (await GetSuperAdminDashboardAsync()).SyncHealth;
    public async Task<PlatformUsage> GetPlatformUsageAsync() => (await GetSuperAdminDashboardAsync()).Usage;

    /// <summary>System Settings isn't built yet, so there's no change history to show.</summary>
    public Task<IReadOnlyList<string>> GetRecentSettingChangesAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    // View -------------------------------------------------------------------------------------------------------------

    public View CreateSuperAdminView()
    {
        if (!CanViewSuperAdmin)
            return new Label { Text = "This dashboard is for the Super Admin.", FontSize = 16, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(40) };

        _superPage = new SuperAdminDashboardPage { Open = (key, id, intent) => OpenModule(key, id, intent) };
        _superPage.RefreshRequested += async (s, e) => await RefreshSuperAdminAsync();

        // Rule 5: every 60 seconds while the page is shown.
        _timer = _superPage.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(60);
        _timer.Tick += async (s, e) => await RefreshSuperAdminAsync();
        _superPage.Loaded += (s, e) => _timer.Start();
        _superPage.Unloaded += (s, e) => _timer.Stop();

        _ = RefreshSuperAdminAsync();
        return _superPage;
    }

    private async Task RefreshSuperAdminAsync()
    {
        int mine = ++_superRefreshNumber;
        try
        {
            var data = await GetSuperAdminDashboardAsync();
            if (mine != _superRefreshNumber) return;
            _lastSuperData = data;
            _superPage.Show(data, online: true);
        }
        catch (SqlException)
        {
            if (mine != _superRefreshNumber) return;
            // Rule 8: keep the last loaded numbers with a warning, or explain when nothing has loaded yet.
            if (_lastSuperData != null) _superPage.Show(_lastSuperData, online: false);
            else _superPage.ShowError("Can't reach the VetCare server. Make sure SQL Server (SQLEXPRESS) is running, then press Refresh.");
        }
        catch (Exception ex)
        {
            if (mine == _superRefreshNumber && _lastSuperData == null) _superPage.ShowError("The dashboard couldn't be loaded: " + ex.Message);
        }
    }
}
