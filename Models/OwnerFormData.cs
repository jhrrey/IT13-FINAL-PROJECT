namespace VetCare.Models;

/// <summary>What the Add / Edit owner form sends to the controller.</summary>
public sealed class OwnerFormData
{
    public const string FirstNameField = "firstName";
    public const string LastNameField = "lastName";
    public const string ContactNoField = "contactNo";
    public const string EmailField = "email";
    public const string AddressField = "address";

    /// <summary>0 when adding a new owner.</summary>
    public int OwnerId { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string ContactNo { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }

    /// <summary>True after the user saw the duplicate-contact warning and chose "Save anyway".</summary>
    public bool DuplicateConfirmed { get; set; }

    public bool IsNew => OwnerId == 0;
}
