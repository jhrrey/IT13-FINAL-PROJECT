using System.Globalization;

namespace VetCare.Helpers;

/// <summary>
/// Rule 14: bookable time slots follow the clinic's hours and slot length (System settings → Clinic defaults:
/// Clinic.OpenTime, Clinic.CloseTime, Clinic.SlotMinutes; 8:00 AM–5:00 PM in 30-minute steps by default).
/// </summary>
public static class TimeSlotHelper
{
    public static TimeSpan Opens => SettingsCache.GetTime(Models.Entities.SettingKeys.OpenTime);
    public static TimeSpan Closes => SettingsCache.GetTime(Models.Entities.SettingKeys.CloseTime);
    public static int SlotMinutes => Math.Max(5, SettingsCache.GetInt(Models.Entities.SettingKeys.SlotMinutes));

    public static TimeSpan SlotLength => TimeSpan.FromMinutes(SlotMinutes);

    /// <summary>8:00, 8:30 … 4:30 (the last slot ends at closing time).</summary>
    public static IReadOnlyList<TimeSpan> AllSlots()
    {
        var slots = new List<TimeSpan>();
        for (var t = Opens; t + SlotLength <= Closes; t += SlotLength) slots.Add(t);
        return slots;
    }

    /// <summary>The start of the slot that <paramref name="time"/> falls in (e.g. 10:42 → 10:30).</summary>
    public static TimeSpan SlotOf(TimeSpan time)
    {
        int minutes = (int)Math.Floor(time.TotalMinutes / SlotMinutes) * SlotMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// A slot is taken when any active appointment of the vet starts inside it
    /// (a walk-in at 10:42 takes the 10:30 slot).
    /// </summary>
    public static bool IsTaken(TimeSpan slot, IEnumerable<TimeSpan> activeTimes) =>
        activeTimes.Any(t => t >= slot && t < slot + SlotLength);

    /// <summary>Rule 5: the slot has already started.</summary>
    public static bool IsPast(DateTime date, TimeSpan slot, DateTime now) => date.Date + slot < now;

    /// <summary>"9:30 AM".</summary>
    public static string Format(TimeSpan time) =>
        DateTime.Today.Add(time).ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>The current time without seconds (used for walk-ins).</summary>
    public static TimeSpan NowToTheMinute(DateTime now) => new(now.Hour, now.Minute, 0);
}
