using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>
/// A row of the <c>Pet</c> table. A pet belongs to one owner and to a clinic only through
/// that owner (the Pet table has no ClinicID of its own).
/// </summary>
public sealed class Pet
{
    public int PetId { get; set; }
    public int OwnerId { get; set; }
    public string PetName { get; set; }

    /// <summary>One of <see cref="PetSpecies.All"/>.</summary>
    public string Species { get; set; }

    public string Breed { get; set; }

    /// <summary>"Male" or "Female".</summary>
    public string Sex { get; set; }

    public DateTime? BirthDate { get; set; }
    public string Color { get; set; }

    /// <summary>Latest weight in kg.</summary>
    public decimal? Weight { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>One of <see cref="SyncStatuses"/>.</summary>
    public string SyncStatus { get; set; }

    // From the joined Owner row (not Pet columns) --------------------------------------

    public string OwnerFirstName { get; set; }
    public string OwnerLastName { get; set; }
    public string OwnerContactNo { get; set; }

    // Convenience ---------------------------------------------------------------------

    public string OwnerName => PersonName.Join(OwnerFirstName, OwnerLastName);

    /// <summary>"0917 123 4567".</summary>
    public string OwnerContactDisplay => Validator.FormatPhMobile(OwnerContactNo);

    /// <summary>"2 yrs 3 mos", or null without a birth date.</summary>
    public string AgeText => BirthDate == null ? null : AgeCalculator.Describe(BirthDate.Value, DateTime.Today);

    /// <summary>"12.5 kg", or null.</summary>
    public string WeightText => Weight == null ? null : Weight.Value.ToString("0.##") + " kg";

    /// <summary>"Dog · Aspin" (just the species without a breed).</summary>
    public string SpeciesAndBreed => string.IsNullOrWhiteSpace(Breed) ? Species : Species + " · " + Breed;
}

/// <summary>The species and sexes the form offers.</summary>
public static class PetSpecies
{
    public const string Dog = "Dog";
    public const string Cat = "Cat";
    public const string Bird = "Bird";
    public const string Rabbit = "Rabbit";
    public const string Hamster = "Hamster";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[] { Dog, Cat, Bird, Rabbit, Hamster, Other };

    public const string Male = "Male";
    public const string Female = "Female";

    public static readonly IReadOnlyList<string> Sexes = new[] { Male, Female };

    /// <summary>The Lucide icon for a species (paw print for the rest).</summary>
    public static string IconFor(string species) => species switch
    {
        Dog => "dog",
        Cat => "cat",
        Bird => "bird",
        Rabbit => "rabbit",
        _ => "paw-print"
    };

    /// <summary>Avatar circle colour key: dog = mint, cat = pale blue, bird = light orange, others = cyan.</summary>
    public static string CircleFor(string species) => species switch
    {
        Dog => "Mint",
        Cat => "PaleBlue",
        Bird => "OrangeSoft",
        _ => "Cyan"
    };
}
