using System.Globalization;
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
/// System Settings (Tasks/VetCare_System_Settings_Module.md, section 5):
///  1. only the Super Admin changes global settings and clinic defaults;
///  2. Administrators change only "Clinic can override" settings, for their own clinic;
///  3. values must be in range (closing after opening, fee not negative …);  4. settings are never deleted;
///  5. reset puts back the seed value; removing an override makes the clinic use the global value;
///  6. every change saves who and when, and appears in the history;  7. risky changes need confirmation;
///  8. changing settings needs a connection;  9. server rules apply at once, computer rules after the next sync;
/// 10. changes don't rewrite old records (e.g. existing bills keep their fee).
/// </summary>
public sealed class SettingController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly SettingRepository _settings;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private Toast _toast;
    private SystemSettingsPage _page;
    private IReadOnlyList<SystemSetting> _global = Array.Empty<SystemSetting>();
    private Func<Task> _pendingConfirm;

    public SettingController(UserSession session, SettingRepository settings, ClinicRepository clinics)
    {
        _session = session;
        _settings = settings;
        _clinics = clinics;
    }

    /// <summary>"history" opens the Change history (dashboard link).</summary>
    public string StartIntent { get; init; }

    private bool ClinicMode => !_session.IsSuperAdmin;
    private bool Allowed => _session.IsSuperAdmin || (_session.IsAdministrator && _session.ClinicId != null);   // rules 1, 2

    // Data (section 7) -----------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SystemSetting>> GetSettingsAsync(string category) =>
        (await _settings.GetGlobalAsync()).Where(s => category == null || s.Category == category).ToList();

    /// <summary>The clinic's override if any, else the global value.</summary>
    public async Task<string> GetEffectiveValueAsync(string key, int? clinicId)
    {
        if (clinicId != null && (await _settings.GetOverridesForClinicAsync(clinicId.Value)).FirstOrDefault(o => o.SettingKey == key) is SystemSetting own)
            return own.SettingValue;
        return (await _settings.GetGlobalAsync()).FirstOrDefault(g => g.SettingKey == key)?.SettingValue ?? SettingCatalog.Get(key)?.Default;
    }

    public Task<IReadOnlyList<SystemSetting>> GetClinicOverridesAsync(string key) => _settings.GetOverridesAsync(key);

    public Task<IReadOnlyList<SettingChange>> GetChangeHistoryAsync(DateTime? from, DateTime? to, string key, string scope) =>
        _settings.GetHistoryAsync(from, to, key, scope);

    /// <summary>Validates every change (rule 3), then saves them together (global, or the Administrator's clinic).</summary>
    public async Task<OperationResult> UpdateSettingsAsync(IReadOnlyDictionary<string, string> changes, Func<string, string> valueAfterSave)
    {
        if (!Allowed) return OperationResult.Fail("You can’t change settings.");
        var errors = new Dictionary<string, string>();
        foreach (var (key, value) in changes)
        {
            var def = SettingCatalog.Get(key);
            if (def == null || def.Kind == SettingKind.Fixed) { errors[key] = "This setting can't be changed."; continue; }
            if (ClinicMode && !def.ClinicOverride) { errors[key] = "Only the Super Admin can change this."; continue; }   // rule 2
            if (Validate(def, value) is string problem) errors[key] = problem;
        }
        // Cross-field: closing after opening.
        if ((changes.ContainsKey(SettingKeys.OpenTime) || changes.ContainsKey(SettingKeys.CloseTime))
            && TimeSpan.TryParse(valueAfterSave(SettingKeys.OpenTime), CultureInfo.InvariantCulture, out var open)
            && TimeSpan.TryParse(valueAfterSave(SettingKeys.CloseTime), CultureInfo.InvariantCulture, out var close) && close <= open)
            errors[changes.ContainsKey(SettingKeys.CloseTime) ? SettingKeys.CloseTime : SettingKeys.OpenTime] = "Closing time must be after opening time";
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        if (!await ClinicController.IsOnlineAsync()) return OperationResult.Fail("Connect to the internet to save settings.");   // rule 8

        await _settings.SaveAsync(changes, _session.UserId, ClinicMode ? _session.ClinicId : null);                              // rule 6
        await SettingsCache.LoadAsync(_session.ClinicId);
        return OperationResult.Success("Settings saved. Clinic computers will update on their next sync.");
    }

    public Task<OperationResult> ResetToDefaultAsync(string key)
    {
        var def = SettingCatalog.Get(key);
        string seed = _global.FirstOrDefault(g => g.SettingKey == key)?.DefaultValue ?? def?.Default;
        return UpdateSettingsAsync(new Dictionary<string, string> { [key] = seed }, k => k == key ? seed : null);
    }

    /// <summary>Administrator: their clinic's own value (rule 2).</summary>
    public Task<OperationResult> SetClinicOverrideAsync(string key, string value) =>
        UpdateSettingsAsync(new Dictionary<string, string> { [key] = value }, k => k == key ? value : null);

    /// <summary>Removes a clinic's own value: it uses the global value again (rule 5).</summary>
    public async Task<OperationResult> RemoveClinicOverrideAsync(string key, int clinicId)
    {
        if (!Allowed || (ClinicMode && clinicId != _session.ClinicId)) return OperationResult.Fail("You can’t change this clinic's settings.");
        if (!await ClinicController.IsOnlineAsync()) return OperationResult.Fail("Connect to the internet to save settings.");
        await _settings.RemoveOverrideAsync(key, clinicId, _session.UserId);
        await SettingsCache.LoadAsync(_session.ClinicId);
        return OperationResult.Success(ClinicMode ? "Now using the VetCare default." : "Override removed. That clinic uses the default value again.");
    }

    /// <summary>Rule 3: "Enter a number from 8 to 32", "Enter a valid email", …</summary>
    public static string Validate(SettingDefinition def, string value)
    {
        value ??= string.Empty;
        switch (def.Kind)
        {
            case SettingKind.Number:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < def.Min || n > def.Max)
                    return $"Enter a number from {def.Min:0} to {def.Max:0}";
                return null;
            case SettingKind.Money:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m) || m < (decimal)def.Min || m > (decimal)def.Max)
                    return $"Enter an amount from ₱{def.Min:N0} to ₱{def.Max:N0}";
                return null;
            case SettingKind.Time:
                return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _) ? null : "Choose a time";
            case SettingKind.Toggle:
                return value is "true" or "false" ? null : "Choose on or off";
            case SettingKind.Options:
                return int.TryParse(value, out int o) && def.Options.Contains(o) ? null : "Choose one of the options";
            case SettingKind.Text:
                if (string.IsNullOrWhiteSpace(value)) return "Required";
                if (def.MaxLength > 0 && value.Length > def.MaxLength) return "Use " + def.MaxLength + " characters or fewer";
                if (def.Min > 0 && value.Trim().Length < def.Min) return "Use at least " + def.Min + " characters";
                if (def.Key == SettingKeys.SupportEmail && !Validator.IsEmail(value)) return "Enter a valid email address";
                if (def.Key == SettingKeys.SupportContactNo && !Validator.IsPhContact(value)) return "Enter a valid contact number";
                if (def.Key == SettingKeys.BillNoPrefix && !value.All(char.IsLetter)) return "Use 2–5 letters only";
                return null;
            default:
                return null;
        }
    }

    /// <summary>Rule 7: the confirmation for a risky change, or null.</summary>
    private string RiskyMessage(IReadOnlyDictionary<string, string> changes)
    {
        var lines = new List<string>();
        string Old(string key) => _global.FirstOrDefault(g => g.SettingKey == key)?.SettingValue;
        if (changes.TryGetValue(SettingKeys.RequireApproval, out var a) && a == "false")
            lines.Add("New clinics will become active right after signing up, without your review.");
        if (changes.TryGetValue(SettingKeys.PasswordRequireMix, out var mix) && mix == "false")
            lines.Add("Passwords won't need a letter and a number anymore.");
        if (changes.TryGetValue(SettingKeys.OfflineLoginMaxDays, out var off) && int.TryParse(off, out int days) && days > 14)
            lines.Add("A deactivated account could keep working offline for up to " + days + " days.");
        foreach (var key in new[] { SettingKeys.GuestInfoDays, SettingKeys.RejectedSignupDays, SettingKeys.SyncLogDays })
        {
            if (changes.TryGetValue(key, out var v) && int.TryParse(v, out int now) && int.TryParse(Old(key), out int before) && now < before)
                lines.Add(SettingCatalog.Get(key).Label + ": " + now + " days instead of " + before + ". Older data will be removed at the next cleanup.");
        }
        return lines.Count == 0 ? null : string.Join("\n\n", lines);
    }

    // View --------------------------------------------------------------------------------------------------------------

    public View CreateView()
    {
        if (!Allowed)
            return new Label { Text = "Settings are for the Super Admin and clinic Administrators.", FontSize = 16, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(40) };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) =>
        {
            var action = _pendingConfirm;
            await CloseDialogAsync();
            if (action != null) await action();
        };

        _page = new SystemSettingsPage(ClinicMode);
        _page.SaveRequested += async (s, changes) => await SaveAsync(changes);
        _page.CategoryChanged += async (s, category) => { if (category == SystemSettingsPage.HistoryKey) await LoadHistoryAsync(); };
        _page.HistoryFiltersChanged += async (s, e) => await LoadHistoryAsync();
        _page.OverridesRequested += async (s, key) => await OpenOverridesAsync(key);
        _page.UseDefaultRequested += async (s, key) =>
        {
            try
            {
                var result = await RemoveClinicOverrideAsync(key, _session.ClinicId ?? 0);
                if (!result.Ok) { await ShowInfoAsync("Can’t change this setting", result.Message); return; }
                _toast.Show(result.Message);
                await LoadAsync();
            }
            catch (SqlException) { await ShowInfoAsync("Can’t change this setting", DatabaseError); }
        };
        _host.ShowPage(_page);

        _toast = new Toast();
        _ = LoadAsync();
        return new Grid { Children = { _host, _toast } };
    }

    private async Task LoadAsync()
    {
        try
        {
            _global = await _settings.GetGlobalAsync();
            var own = ClinicMode ? await _settings.GetOverridesForClinicAsync(_session.ClinicId.Value) : Array.Empty<SystemSetting>();
            var counts = ClinicMode ? new Dictionary<string, int>() : await _settings.GetOverrideCountsAsync();
            _page.SetData(_global, own, counts);
            _page.SetOffline(!await ClinicController.IsOnlineAsync());
            if (StartIntent == "history" && !ClinicMode && _page.Category != SystemSettingsPage.HistoryKey) _page.OpenCategory(SystemSettingsPage.HistoryKey);
        }
        catch (SqlException)
        {
            _page.ShowError(DatabaseError);
        }
    }

    private async Task SaveAsync(IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Count == 0) return;
        if (!ClinicMode && RiskyMessage(changes) is string risky)                                                  // rule 7
        {
            _pendingConfirm = () => SaveConfirmedAsync(changes);
            await _host.Dialog.ShowConfirmAsync("Save these changes?", risky, "Yes, save", danger: false, icon: "triangle-alert");
            return;
        }
        await SaveConfirmedAsync(changes);
    }

    private async Task SaveConfirmedAsync(IReadOnlyDictionary<string, string> changes)
    {
        _page.SetBusy(true);
        try
        {
            var result = await UpdateSettingsAsync(changes, _page.ValueAfterSave);
            if (!result.Ok)
            {
                if (result.FieldErrors.Count > 0) _page.ShowErrors(result.FieldErrors.ToDictionary(k => k.Key, k => k.Value));
                else await ShowInfoAsync("Settings weren't saved", result.Message);
                return;
            }
            _toast.Show(result.Message);
            await LoadAsync();
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Settings weren't saved", DatabaseError);
        }
        finally
        {
            _page.SetBusy(false);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var (from, to, key, scope) = _page.HistoryFilter;
            var clinics = (await _clinics.GetAllAsync()).ToList();
            _page.ShowHistory(await GetChangeHistoryAsync(from, to, key, scope), clinics);
        }
        catch (SqlException)
        {
            _page.ShowError(DatabaseError);
        }
    }

    private async Task OpenOverridesAsync(string key)
    {
        try
        {
            var def = SettingCatalog.Get(key);
            var overrides = await GetClinicOverridesAsync(key);
            string global = _global.FirstOrDefault(g => g.SettingKey == key)?.SettingValue ?? def.Default;
            var view = new SettingOverridesView(def, overrides, global);
            view.CloseRequested += async (s, e) => await _host.Panel.HideAsync();
            view.RemoveRequested += (s, clinicId) =>
            {
                string clinic = overrides.FirstOrDefault(o => o.ClinicId == clinicId)?.ClinicName ?? "this clinic";
                _pendingConfirm = async () =>
                {
                    try
                    {
                        var result = await RemoveClinicOverrideAsync(key, clinicId);
                        if (!result.Ok) { await ShowInfoAsync("Override not removed", result.Message); return; }
                        _toast.Show(result.Message);
                        await _host.Panel.HideAsync();
                        await LoadAsync();
                    }
                    catch (SqlException) { await ShowInfoAsync("Override not removed", DatabaseError); }
                };
                _ = _host.Dialog.ShowConfirmAsync("Remove " + clinic + "'s value?", clinic + " will use the default value (" + SettingCatalog.Display(key, global) + ") after its next sync.",
                    "Remove override", danger: true, icon: "settings");
            };
            await _host.Panel.ShowAsync(view, SettingOverridesView.PanelWidth);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t load the clinics", DatabaseError);
        }
    }

    private Task ShowInfoAsync(string title, string message)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: "circle-alert", showCancel: false);
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
