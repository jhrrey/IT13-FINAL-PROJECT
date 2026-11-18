namespace VetCare.Helpers;

/// <summary>Dashboard wording: the greeting by time of day and short "how long" texts.</summary>
public static class GreetingHelper
{
    /// <summary>"Good morning" (before 12), "Good afternoon" (before 6 PM), "Good evening".</summary>
    public static string Greeting(DateTime now) =>
        now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";

    /// <summary>"12 min", "1 hr 5 min", "just now".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "just now";
        int hours = (int)span.TotalHours, minutes = span.Minutes;
        if (hours == 0) return minutes + " min";
        return hours + " hr" + (minutes > 0 ? " " + minutes + " min" : string.Empty);
    }

    /// <summary>"2 min ago", "3 hr ago", "Nov 14".</summary>
    public static string Ago(DateTime when, DateTime now)
    {
        var span = now - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
        if (span.TotalDays < 1 && when.Date == now.Date) return (int)span.TotalHours + " hr ago";
        return when.Date == now.Date.AddDays(-1) ? "yesterday" : when.ToString("MMM d");
    }

    /// <summary>"just now", "2 min ago", "3 hours ago", "1 day ago", "12 days ago" (sync times, sign-up age).</summary>
    public static string Relative(DateTime when, DateTime now)
    {
        var span = now - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
        if (span.TotalDays < 1) return (int)span.TotalHours == 1 ? "1 hour ago" : (int)span.TotalHours + " hours ago";
        int days = (int)span.TotalDays;
        return days == 1 ? "1 day ago" : days + " days ago";
    }

    /// <summary>"+12% vs yesterday" / "−8% vs yesterday" / null when there's nothing to compare.</summary>
    public static (string Text, bool Up)? Change(decimal today, decimal yesterday)
    {
        if (yesterday <= 0) return null;
        decimal percent = Math.Round((today - yesterday) / yesterday * 100);
        return ((percent >= 0 ? "+" : "−") + Math.Abs(percent) + "% vs yesterday", percent >= 0);
    }
}
