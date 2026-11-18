namespace VetCare.Helpers;

/// <summary>Pet ages: "2 yrs 3 mos" from a birth date, and an approximate birth date from an estimated age.</summary>
public static class AgeCalculator
{
    /// <summary>
    /// "2 yrs 3 mos", "1 yr", "8 mos", "3 wks" or "5 days". Birth dates in the future give "Not born yet".
    /// </summary>
    public static string Describe(DateTime birthDate, DateTime today)
    {
        birthDate = birthDate.Date;
        today = today.Date;
        if (birthDate > today) return "Not born yet";

        int months = (today.Year - birthDate.Year) * 12 + today.Month - birthDate.Month;
        if (today.Day < birthDate.Day) months--;

        int years = months / 12;
        int restMonths = months % 12;

        if (years > 0)
            return restMonths == 0 ? Plural(years, "yr") : Plural(years, "yr") + " " + Plural(restMonths, "mo");
        if (months > 0) return Plural(months, "mo");

        int days = (today - birthDate).Days;
        return days >= 7 ? Plural(days / 7, "wk") : days == 1 ? "1 day" : days + " days";
    }

    /// <summary>
    /// Rule 4: with only an estimated age, the birth date is today minus that many years and months.
    /// </summary>
    public static DateTime EstimateBirthDate(int years, int months, DateTime today) =>
        today.Date.AddYears(-years).AddMonths(-months);

    /// <summary>Whole years and months between the birth date and today (to fill the estimate inputs).</summary>
    public static (int Years, int Months) YearsAndMonths(DateTime birthDate, DateTime today)
    {
        int months = (today.Year - birthDate.Year) * 12 + today.Month - birthDate.Month;
        if (today.Day < birthDate.Day) months--;
        months = Math.Max(0, months);
        return (months / 12, months % 12);
    }

    private static string Plural(int value, string unit) => value + " " + unit + (value == 1 ? string.Empty : "s");
}
