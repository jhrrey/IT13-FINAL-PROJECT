namespace VetCare.Helpers;

public enum ReportPeriod { Today, ThisWeek, ThisMonth, LastMonth, Custom }

/// <summary>Report date ranges: the quick periods, the label, and the rule-4 checks.</summary>
public static class DateRangeHelper
{
    public static readonly (ReportPeriod Period, string Text)[] Periods =
    {
        (ReportPeriod.Today, "Today"), (ReportPeriod.ThisWeek, "This week"), (ReportPeriod.ThisMonth, "This month"),
        (ReportPeriod.LastMonth, "Last month"), (ReportPeriod.Custom, "Custom")
    };

    /// <summary>The range of a quick period (weeks start on Monday). Custom returns this month.</summary>
    public static (DateTime From, DateTime To) RangeOf(ReportPeriod period, DateTime today)
    {
        today = today.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return period switch
        {
            ReportPeriod.Today => (today, today),
            ReportPeriod.ThisWeek => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
            ReportPeriod.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            _ => (monthStart, today)
        };
    }

    /// <summary>"Nov 1 – Nov 17, 2026" (or "Nov 18, 2026" for one day; years shown when they differ).</summary>
    public static string Label(DateTime from, DateTime to)
    {
        if (from.Date == to.Date) return from.ToString("MMM d, yyyy");
        return from.Year == to.Year
            ? from.ToString("MMM d") + " – " + to.ToString("MMM d, yyyy")
            : from.ToString("MMM d, yyyy") + " – " + to.ToString("MMM d, yyyy");
    }

    /// <summary>Rule 4: both dates required, the end not before the start, at most 1 year. Null when valid.</summary>
    public static string Problem(DateTime? from, DateTime? to)
    {
        if (from == null || to == null) return "Pick a start and an end date";
        if (to.Value.Date < from.Value.Date) return "End date can't be before the start date";
        if (to.Value.Date > from.Value.Date.AddYears(1)) return "Pick a range of 1 year or less";
        return null;
    }

    /// <summary>The same-length period right before (for "vs last month" comparisons).</summary>
    public static (DateTime From, DateTime To) Previous(DateTime from, DateTime to)
    {
        if (from.Day == 1 && from.Month == to.Month && from.Year == to.Year)
        {
            var start = from.AddMonths(-1);
            return (start, start.AddDays(Math.Min(to.Day, DateTime.DaysInMonth(start.Year, start.Month)) - 1));
        }
        int days = (to.Date - from.Date).Days + 1;
        return (from.Date.AddDays(-days), from.Date.AddDays(-1));
    }
}
