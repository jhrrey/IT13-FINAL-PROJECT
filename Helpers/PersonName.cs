namespace VetCare.Helpers;

/// <summary>Splits a single "full name" box into first and last name.</summary>
public static class PersonName
{
    /// <summary>
    /// The last word is the last name; everything before it is the first name
    /// ("Jhon Rey Depacaquibo" → "Jhon Rey" / "Depacaquibo"). One word → first name only.
    /// This matches how Database/001_user_management.sql split existing names.
    /// </summary>
    public static (string First, string Last) Split(string fullName)
    {
        var name = string.Join(" ", (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        int lastSpace = name.LastIndexOf(' ');
        return lastSpace < 0 ? (name, string.Empty) : (name[..lastSpace], name[(lastSpace + 1)..]);
    }

    public static string Join(string first, string last) =>
        string.Join(" ", new[] { first, last }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

    /// <summary>Up to two initials for an avatar ("Maria Santos" → "MS").</summary>
    public static string Initials(string first, string last)
    {
        string a = string.IsNullOrWhiteSpace(first) ? string.Empty : first.Trim()[..1];
        string b = string.IsNullOrWhiteSpace(last) ? string.Empty : last.Trim()[..1];
        string initials = (a + b).ToUpperInvariant();
        return initials.Length == 0 ? "?" : initials;
    }
}
