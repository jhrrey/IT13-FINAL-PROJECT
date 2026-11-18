namespace VetCare.Models
{
    public enum AuthMode
    {
        SignIn,
        Register,

        /// <summary>One-time creation of the first Super Admin account.</summary>
        SetupSuperAdmin,

        /// <summary>After signing in with a temporary password: set your own before continuing.</summary>
        ChangePassword
    }

    public enum AuthFieldKind
    {
        Text,
        Email,
        Phone,
        Password,

        /// <summary>Multi-line text (3 rows), e.g. an address.</summary>
        TextArea
    }

    /// <summary>One input on the sign-in / registration form.</summary>
    public sealed class AuthField
    {
        public const string ClinicName = "clinicName";
        public const string ClinicAddress = "clinicAddress";
        public const string ClinicContact = "clinicContact";
        public const string AdminName = "adminName";
        public const string FullName = "fullName";
        public const string Email = "email";
        public const string Password = "password";
        public const string ConfirmPassword = "confirmPassword";

        public AuthField(string name, string label, string placeholder, AuthFieldKind kind, int column = 0, int maxLength = 0)
        {
            Name = name;
            Label = label;
            Placeholder = placeholder;
            Kind = kind;
            Column = column;
            MaxLength = maxLength;
        }

        /// <summary>Longest value the database column accepts (digits only for phone numbers); 0 = no limit.</summary>
        public int MaxLength { get; }

        public string Name { get; }
        public string Label { get; }
        public string Placeholder { get; }
        public AuthFieldKind Kind { get; }

        /// <summary>Which column the field sits in on a two-column form (0 = left, 1 = right).</summary>
        public int Column { get; }
    }
}
