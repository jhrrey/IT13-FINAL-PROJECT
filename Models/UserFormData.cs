namespace VetCare.Models;

/// <summary>What the Add / Edit user form sends to the controller.</summary>
public sealed class UserFormData
{
    // Field names (used for inline error messages).
    public const string FirstNameField = "firstName";
    public const string LastNameField = "lastName";
    public const string UsernameField = "username";
    public const string EmailField = "email";
    public const string ContactNoField = "contactNo";
    public const string RoleField = "role";
    public const string ClinicField = "clinic";
    public const string PasswordField = "password";
    public const string ConfirmPasswordField = "confirmPassword";

    /// <summary>0 when adding a new user.</summary>
    public int UserId { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string ContactNo { get; set; }
    public int? RoleId { get; set; }
    public int? ClinicId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Temporary password (Add only).</summary>
    public string Password { get; set; }

    public string ConfirmPassword { get; set; }

    public bool IsNew => UserId == 0;
}

/// <summary>The outcome of a controller action: success, a general message, and/or per-field errors.</summary>
public sealed class OperationResult
{
    private OperationResult(bool ok, string message, IDictionary<string, string> fieldErrors)
    {
        Ok = ok;
        Message = message;
        FieldErrors = fieldErrors ?? new Dictionary<string, string>();
    }

    public bool Ok { get; }
    public string Message { get; }
    public IDictionary<string, string> FieldErrors { get; }

    public static OperationResult Success(string message = null) => new(true, message, null);
    public static OperationResult Fail(string message) => new(false, message, null);
    public static OperationResult Invalid(IDictionary<string, string> errors) => new(false, null, errors);
}
