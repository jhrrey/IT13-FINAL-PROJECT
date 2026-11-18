using System;
using System.Collections.Generic;
using System.Linq;
using VetCare.Helpers;

namespace VetCare.Models
{
    /// <summary>
    /// The account forms (sign in, clinic registration, Super Admin setup): what each contains
    /// and how its values are validated. Has no knowledge of the UI.
    /// </summary>
    public sealed class AuthModel
    {
        private readonly List<AuthModeDefinition> _definitions;

        public AuthModel()
        {
            _definitions = new List<AuthModeDefinition> { CreateSignIn(), CreateRegister(), CreateSetupSuperAdmin(), CreateChangePassword() };
        }

        public IReadOnlyList<AuthModeDefinition> Definitions => _definitions;

        /// <summary>The forms shown as tabs on the sign-in card.</summary>
        public IReadOnlyList<AuthModeDefinition> CardTabs => new[] { Get(AuthMode.SignIn), Get(AuthMode.Register) };

        public AuthModeDefinition Get(AuthMode mode) => _definitions.Find(d => d.Mode == mode);

        /// <summary>Validates the values entered on one form.</summary>
        /// <returns>Field name to error message. Empty when everything is valid.</returns>
        public IDictionary<string, string> Validate(AuthMode mode, IDictionary<string, string> values)
        {
            var errors = new Dictionary<string, string>();
            bool newPassword = mode != AuthMode.SignIn; // new passwords must follow the password rule

            foreach (var field in Get(mode).Fields)
            {
                string value;
                values.TryGetValue(field.Name, out value);
                value = value ?? string.Empty;
                if (field.Kind != AuthFieldKind.Password) value = value.Trim();

                if (value.Length == 0)
                {
                    errors[field.Name] = field.Name == AuthField.ConfirmPassword
                        ? "Please confirm your password."
                        : "Please enter your " + field.Label.ToLowerInvariant() + ".";
                    continue;
                }

                if (field.MaxLength > 0 && Normalize(field, value).Length > field.MaxLength)
                {
                    errors[field.Name] = field.Kind == AuthFieldKind.Phone
                        ? "Use at most " + field.MaxLength + " digits."
                        : "Use " + field.MaxLength + " characters or fewer.";
                }
                else if (field.Kind == AuthFieldKind.Email && !Validator.IsEmail(value))
                {
                    errors[field.Name] = "Please enter a valid email address.";
                }
                else if (field.Kind == AuthFieldKind.Phone && !Validator.IsPhone(value))
                {
                    errors[field.Name] = "Please enter a valid contact number.";
                }
                else if (newPassword && field.Name == AuthField.Password && Validator.PasswordProblem(value) is string problem)
                {
                    errors[field.Name] = problem;
                }
                else if (field.Name == AuthField.ConfirmPassword)
                {
                    string password;
                    values.TryGetValue(AuthField.Password, out password);
                    if (value != password) errors[field.Name] = "Passwords don’t match.";
                }
            }

            return errors;
        }

        /// <summary>
        /// The value as it is stored: trimmed, emails lower-cased, contact numbers reduced
        /// to digits (e.g. "0912 345 6789" → "09123456789"). Passwords are returned unchanged.
        /// </summary>
        public static string Normalize(AuthField field, string value)
        {
            value = value ?? string.Empty;
            switch (field.Kind)
            {
                case AuthFieldKind.Password:
                    return value;
                case AuthFieldKind.Email:
                    return value.Trim().ToLowerInvariant();
                case AuthFieldKind.Phone:
                    return new string(value.Where(char.IsDigit).ToArray());
                default:
                    return value.Trim();
            }
        }

        /// <summary>The normalized value of field <paramref name="name"/> on form <paramref name="mode"/>.</summary>
        public string Value(AuthMode mode, IDictionary<string, string> values, string name)
        {
            var field = Get(mode).Fields.First(f => f.Name == name);
            values.TryGetValue(name, out var value);
            return Normalize(field, value);
        }

        private static AuthModeDefinition CreateSignIn()
        {
            return new AuthModeDefinition(
                AuthMode.SignIn,
                tabLabel: "Sign in",
                heading: "Welcome back.",
                subtext: "Sign in to your VetCare account.",
                submitLabel: "Sign in",
                footnote: "By continuing, you agree to use VetCare’s account services.",
                successMessage: "Signed in.",
                fields: new[]
                {
                    new AuthField(AuthField.Email, "Email address", "you@example.com", AuthFieldKind.Email, maxLength: 100),
                    new AuthField(AuthField.Password, "Password", "Enter your password", AuthFieldKind.Password)
                });
        }

        private static AuthModeDefinition CreateRegister()
        {
            return new AuthModeDefinition(
                AuthMode.Register,
                tabLabel: "Create account",
                heading: "Register your clinic.",
                subtext: "Set up VetCare for your veterinary clinic.",
                submitLabel: "Create account",
                footnote: "Your clinic will be reviewed by the VetCare team before activation.",
                successMessage: "Your clinic is registered. The VetCare team will review it; you can sign in once it’s approved.",
                // Max lengths match the db_vetcare columns (Clinic.ClinicName 100, Clinic.Address 255,
                // Clinic.ContactNo 20 (mobile numbers: 11), User.FirstName/LastName 50, User.Email 100).
                fields: new[]
                {
                    new AuthField(AuthField.ClinicName, "Clinic name", "e.g. Happy Paws Veterinary Clinic", AuthFieldKind.Text, maxLength: 100),
                    new AuthField(AuthField.ClinicAddress, "Clinic address", "e.g. Matina, Davao City", AuthFieldKind.Text, maxLength: 255),
                    new AuthField(AuthField.ClinicContact, "Clinic contact number", "e.g. 0912 345 6789", AuthFieldKind.Phone, maxLength: 11),
                    new AuthField(AuthField.AdminName, "Administrator full name", "e.g. Maria Santos", AuthFieldKind.Text, maxLength: 50),
                    new AuthField(AuthField.Email, "Email address", "you@example.com", AuthFieldKind.Email, column: 1, maxLength: 100),
                    new AuthField(AuthField.Password, "Password", "Create a password", AuthFieldKind.Password, column: 1),
                    new AuthField(AuthField.ConfirmPassword, "Confirm password", "Re-enter your password", AuthFieldKind.Password, column: 1)
                });
        }

        private static AuthModeDefinition CreateChangePassword()
        {
            return new AuthModeDefinition(
                AuthMode.ChangePassword,
                tabLabel: "Set your password",
                heading: "Set your password.",
                subtext: "You signed in with a temporary password. Choose your own to continue.",
                submitLabel: "Save and continue",
                footnote: "Choose a password only you know, and keep it to yourself.",
                successMessage: "Password saved.",
                fields: new[]
                {
                    new AuthField(AuthField.Password, "New password", "Create a password", AuthFieldKind.Password),
                    new AuthField(AuthField.ConfirmPassword, "Confirm password", "Re-enter your password", AuthFieldKind.Password)
                });
        }

        private static AuthModeDefinition CreateSetupSuperAdmin()
        {
            return new AuthModeDefinition(
                AuthMode.SetupSuperAdmin,
                tabLabel: "Super Admin setup",
                heading: "Set up the Super Admin.",
                subtext: "Create the account that approves and manages every clinic. This can only be done once.",
                submitLabel: "Create Super Admin",
                footnote: "The Super Admin is not tied to any clinic and can switch between all of them.",
                successMessage: "Super Admin created. Sign in with your new account.",
                // Max lengths match the db_vetcare User columns (FirstName/LastName 50, Email 100).
                fields: new[]
                {
                    new AuthField(AuthField.FullName, "Full name", "e.g. Juan Dela Cruz", AuthFieldKind.Text, maxLength: 50),
                    new AuthField(AuthField.Email, "Email address", "you@example.com", AuthFieldKind.Email, maxLength: 100),
                    new AuthField(AuthField.Password, "Password", "Create a password", AuthFieldKind.Password, column: 1),
                    new AuthField(AuthField.ConfirmPassword, "Confirm password", "Re-enter your password", AuthFieldKind.Password, column: 1)
                });
        }
    }
}
