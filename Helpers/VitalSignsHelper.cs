using System.Globalization;

namespace VetCare.Helpers;

/// <summary>Normal body temperature by species (rule 6 warning), and the allowed input ranges.</summary>
public static class VitalSignsHelper
{
    public const decimal MinWeight = 0.01m;
    public const decimal MaxWeight = 200m;
    public const decimal MinTemperature = 30.0m;
    public const decimal MaxTemperature = 45.0m;

    /// <summary>Normal rectal temperature in °C, or null when there is no reference for the species.</summary>
    public static (decimal Low, decimal High)? NormalTemperature(string species) => species switch
    {
        "Dog" => (38.3m, 39.2m),
        "Cat" => (38.1m, 39.2m),
        "Rabbit" => (38.5m, 40.0m),
        "Bird" => (40.0m, 42.0m),
        "Hamster" => (36.0m, 38.0m),
        _ => null
    };

    /// <summary>"Normal for dogs: 38.3–39.2 °C", or null.</summary>
    public static string NormalTemperatureText(string species)
    {
        if (NormalTemperature(species) is not { } range) return null;
        var (low, high) = range;
        string plural = species switch { "Dog" => "dogs", "Cat" => "cats", "Rabbit" => "rabbits", "Bird" => "birds", "Hamster" => "hamsters", _ => species };
        return "Normal for " + plural + ": " + low.ToString("0.0", CultureInfo.InvariantCulture) + "–" + high.ToString("0.0", CultureInfo.InvariantCulture) + " °C";
    }

    /// <summary>True when a temperature is outside the species' normal range.</summary>
    public static bool IsOutsideNormal(string species, decimal? temperature) =>
        temperature != null && NormalTemperature(species) is { } range && (temperature < range.Low || temperature > range.High);
}
