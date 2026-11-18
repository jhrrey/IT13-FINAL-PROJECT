using System.Security.Cryptography;

namespace VetCare.Helpers;

/// <summary>
/// Readable temporary passwords such as "Vc7-paws-4821": a capital, a lowercase letter and a digit, a short word and
/// four digits. Always 12+ characters with letters and numbers, so it passes <see cref="Validator.PasswordProblem"/>.
/// </summary>
public static class PasswordGenerator
{
    private static readonly string[] Words =
    {
        "paws", "tail", "fur", "bark", "purr", "mane", "fins", "wing", "hoof", "nose",
        "pup", "kit", "calm", "kind", "care", "vet", "leaf", "sun", "moon", "star"
    };

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O (look like 1 and 0)
    private const string Lower = "abcdefghjkmnpqrstuvwxyz";    // no i, l or o

    public static string Generate()
    {
        char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
        string prefix = Pick(Upper).ToString() + Pick(Lower) + RandomNumberGenerator.GetInt32(2, 10);
        string word = Words[RandomNumberGenerator.GetInt32(Words.Length)];
        string digits = RandomNumberGenerator.GetInt32(1000, 10000).ToString();
        string password = prefix + "-" + word + "-" + digits;
        // Longer when System settings asks for more (Security.PasswordMinLength).
        while (password.Length < Validator.MinPasswordLength) password += RandomNumberGenerator.GetInt32(0, 10).ToString();
        return password;
    }
}
