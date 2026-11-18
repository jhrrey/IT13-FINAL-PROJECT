namespace VetCare.Helpers;

/// <summary>The four vaccine statuses (calculated, never saved).</summary>
public enum VaccineStatus
{
    /// <summary>Next due date more than 30 days away.</summary>
    UpToDate,

    /// <summary>Next due date within the next 30 days.</summary>
    DueSoon,

    /// <summary>Next due date already passed.</summary>
    Overdue,

    /// <summary>Final dose of a series: no next due date.</summary>
    Completed
}

/// <summary>
/// Vaccination rule 9: default intervals between doses (suggested; the vet can change the date),
/// and the status of a vaccine from its next due date.
/// </summary>
public static class VaccineScheduleHelper
{
    /// <summary>System settings → Clinic.VaccineDueSoonDays (30 by default).</summary>
    public static int DueSoonDays => SettingsCache.GetInt(Models.Entities.SettingKeys.VaccineDueSoonDays);

    public static VaccineStatus StatusOf(DateTime? nextDue, DateTime today)
    {
        if (nextDue == null) return VaccineStatus.Completed;
        if (nextDue.Value.Date < today.Date) return VaccineStatus.Overdue;
        if (nextDue.Value.Date <= today.Date.AddDays(DueSoonDays)) return VaccineStatus.DueSoon;
        return VaccineStatus.UpToDate;
    }

    /// <summary>Pill text, colours (background key, text key) and icon for a status.</summary>
    public static (string Text, string Background, string TextColor, string Icon) Style(VaccineStatus status) => status switch
    {
        VaccineStatus.DueSoon => ("Due soon", "WarningBox", "ConsultText", "clock"),
        VaccineStatus.Overdue => ("Overdue", "OrangeSoft", "Orange", "circle-alert"),
        VaccineStatus.Completed => ("Completed", "PaleBlue", "Navy", "badge-check"),
        _ => ("Up to date", "Mint", "CompletedText", "check")
    };

    /// <summary>
    /// The suggested next due date for <paramref name="doseNo"/> of a vaccine given on <paramref name="dateGiven"/>:
    /// puppy / kitten series (5-in-1, DHPP, 4-in-1, FVRCP) every 3 weeks for doses 1–2, then yearly;
    /// anti-rabies, kennel cough and anything else yearly.
    /// </summary>
    public static (DateTime Date, string Hint) SuggestNextDue(string vaccineName, int doseNo, DateTime dateGiven)
    {
        if (IsSeries(vaccineName) && doseNo < 3)
            return (dateGiven.Date.AddDays(21), "Suggested: +3 weeks");
        return (dateGiven.Date.AddYears(1), "Suggested: +1 year");
    }

    private static bool IsSeries(string name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        return n.Contains("5-in-1") || n.Contains("dhpp") || n.Contains("4-in-1") || n.Contains("fvrcp")
            || n.Contains("6-in-1") || n.Contains("8-in-1") || n.Contains("puppy") || n.Contains("kitten");
    }

    /// <summary>"in 11 days" / "today" / "18 days overdue" for the due list.</summary>
    public static string DaysText(DateTime nextDue, DateTime today)
    {
        int days = (nextDue.Date - today.Date).Days;
        if (days == 0) return "due today";
        if (days == 1) return "tomorrow";
        if (days > 0) return "in " + days + " days";
        return -days == 1 ? "1 day overdue" : -days + " days overdue";
    }
}
