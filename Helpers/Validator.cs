using System.Text.RegularExpressions;

namespace VetCare.Helpers;

/// <summary>Input checks shared by every form (sign-in, registration, user management).</summary>
public static class Validator
{
    /// <summary>System settings → Security.PasswordMinLength (8 by default).</summary>
    public static int MinPasswordLength => Math.Max(8, SettingsCache.GetInt(Models.Entities.SettingKeys.PasswordMinLength));

    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    private static readonly Regex UsernamePattern = new(@"^[A-Za-z0-9._-]+$");

    public static bool IsEmail(string value) => !string.IsNullOrWhiteSpace(value) && EmailPattern.IsMatch(value.Trim());

    /// <summary>Letters, digits, dot, underscore and dash only (no spaces).</summary>
    public static bool IsUsername(string value) => !string.IsNullOrWhiteSpace(value) && UsernamePattern.IsMatch(value.Trim());

    /// <summary>
    /// Why <paramref name="password"/> is not allowed, or null if it is.
    /// Rule (System settings → Security): at least MinPasswordLength characters, with a letter and a number when required.
    /// </summary>
    /// <summary>"At least 8 characters, letters and numbers" (follows the password settings).</summary>
    public static string PasswordHint =>
        "At least " + MinPasswordLength + " characters" + (SettingsCache.GetBool(Models.Entities.SettingKeys.PasswordRequireMix) ? ", letters and numbers" : string.Empty);

    public static string PasswordProblem(string password)
    {
        password ??= string.Empty;
        if (password.Length < MinPasswordLength) return "Use at least " + MinPasswordLength + " characters.";
        if (SettingsCache.GetBool(Models.Entities.SettingKeys.PasswordRequireMix) && (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)))
            return "Include at least one letter and one number.";
        return null;
    }

    /// <summary>Only the digits of a phone number ("0912 345 6789" → "09123456789").</summary>
    public static string Digits(string value) => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// A Philippine mobile number: 11 digits starting with 09 (e.g. 0917 123 4567).
    /// Spaces and dashes are allowed while typing.
    /// </summary>
    public static bool IsPhMobile(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(c => !(char.IsDigit(c) || c is ' ' or '-'))) return false;
        string digits = Digits(value);
        return digits.Length == 11 && digits.StartsWith("09", StringComparison.Ordinal);
    }

    /// <summary>"09171234567" → "0917 123 4567" (other values are returned unchanged).</summary>
    public static string FormatPhMobile(string value)
    {
        string digits = Digits(value);
        return digits.Length == 11 ? $"{digits[..4]} {digits[4..7]} {digits[7..]}" : value;
    }

    /// <summary>
    /// A Philippine contact no.: a mobile (0917 123 4567 or +63 917 123 4567), a landline with area code
    /// ((082) 221 1234, 0822 123 4567) or a local 7–8 digit landline.
    /// </summary>
    public static bool IsPhContact(string value)
    {
        if (!IsPhone(value)) return false;
        string digits = Digits(value);
        if (digits.StartsWith("63", StringComparison.Ordinal)) digits = "0" + digits[2..];
        if (digits.StartsWith("09", StringComparison.Ordinal)) return digits.Length == 11;
        if (digits.StartsWith("0", StringComparison.Ordinal)) return digits.Length is >= 9 and <= 11;
        return digits.Length is 7 or 8;
    }

    /// <summary>A plausible phone number: 7 to 20 digits, optionally with +, spaces, dashes or brackets.</summary>
    public static bool IsPhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))) return false;
        int digits = Digits(value).Length;
        return digits >= 7 && digits <= 20;
    }
}
