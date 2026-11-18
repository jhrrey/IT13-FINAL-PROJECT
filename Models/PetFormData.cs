namespace VetCare.Models;

/// <summary>What the Add / Edit pet form sends to the controller (raw text; the controller validates it).</summary>
public sealed class PetFormData
{
    public const string OwnerField = "owner";
    public const string PetNameField = "petName";
    public const string SpeciesField = "species";
    public const string BreedField = "breed";
    public const string SexField = "sex";
    public const string BirthDateField = "birthDate";
    public const string AgeField = "age";
    public const string ColorField = "color";
    public const string WeightField = "weight";

    /// <summary>0 when adding a new pet.</summary>
    public int PetId { get; set; }

    /// <summary>0 when no owner is chosen yet.</summary>
    public int OwnerId { get; set; }

    /// <summary>The owner the pet had when the form opened (a different <see cref="OwnerId"/> = a transfer).</summary>
    public int OriginalOwnerId { get; set; }

    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public string Sex { get; set; }

    /// <summary>The picked birth date; null when no date was picked.</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>"I don't know the exact date" is ticked: use <see cref="AgeYears"/> / <see cref="AgeMonths"/>.</summary>
    public bool UseEstimatedAge { get; set; }

    public string AgeYears { get; set; }
    public string AgeMonths { get; set; }
    public string Color { get; set; }
    public string Weight { get; set; }

    public bool IsNew => PetId == 0;
}
