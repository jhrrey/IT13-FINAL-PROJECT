using System.Globalization;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;

namespace VetCare.Helpers;

/// <summary>
/// The settings this computer follows (System Settings module, sections 2 and 6): the global values plus the signed-in
/// clinic's overrides. Loaded when the landing page opens, at sign-in and on every sync check; until then (or offline)
/// the last loaded values, or the catalog defaults, apply. Typed getters never throw.
/// </summary>
public static class SettingsCache
{
    private static Dictionary<string, string> _global = new();
    private static Dictionary<string, string> _clinic = new();

    /// <summary>The clinic whose overrides are loaded (null = global values only).</summary>
    public static int? ClinicId { get; private set; }

    public static event EventHandler Changed;

    /// <summary>Global values only (landing page, Super Admin).</summary>
    public static async Task LoadGlobalAsync()
    {
        try
        {
            var rows = await new SettingRepository().GetGlobalAsync();
            _global = rows.ToDictionary(r => r.SettingKey, r => r.SettingValue);
            _clinic = new();
            ClinicId = null;
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception) { /* keep the last values */ }
    }

    /// <summary>Global values plus one clinic's overrides.</summary>
    public static async Task LoadAsync(int? clinicId)
    {
        try
        {
            var repo = new SettingRepository();
            var rows = await repo.GetGlobalAsync();
            var overrides = clinicId == null ? Array.Empty<SystemSetting>() : await repo.GetOverridesForClinicAsync(clinicId.Value);
            _global = rows.ToDictionary(r => r.SettingKey, r => r.SettingValue);
            _clinic = overrides.ToDictionary(r => r.SettingKey, r => r.SettingValue);
            ClinicId = clinicId;
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception) { /* keep the last values */ }
    }

    /// <summary>The effective value: the clinic's override if any, else the global value, else the catalog default.</summary>
    public static string Get(string key) =>
        _clinic.TryGetValue(key, out var c) ? c
        : _global.TryGetValue(key, out var g) ? g
        : SettingCatalog.Get(key)?.Default;

    public static int GetInt(string key) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v
        : int.Parse(SettingCatalog.Get(key)?.Default ?? "0", CultureInfo.InvariantCulture);

    public static bool GetBool(string key) => (Get(key) ?? SettingCatalog.Get(key)?.Default) == "true";

    public static decimal GetMoney(string key) =>
        decimal.TryParse(Get(key), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : 0m;

    public static TimeSpan GetTime(string key) =>
        TimeSpan.TryParse(Get(key), CultureInfo.InvariantCulture, out var t) ? t
        : TimeSpan.Parse(SettingCatalog.Get(key)?.Default ?? "00:00", CultureInfo.InvariantCulture);

    public static string GetText(string key) => Get(key) ?? string.Empty;
}
