using System.Collections.Generic;

namespace VetCare.Models
{
    /// <summary>Everything the account card shows for one tab ("Sign in" or "Create account").</summary>
    public sealed class AuthModeDefinition
    {
        public AuthModeDefinition(
            AuthMode mode,
            string tabLabel,
            string heading,
            string subtext,
            string submitLabel,
            string footnote,
            string successMessage,
            IReadOnlyList<AuthField> fields)
        {
            Mode = mode;
            TabLabel = tabLabel;
            Heading = heading;
            Subtext = subtext;
            SubmitLabel = submitLabel;
            Footnote = footnote;
            SuccessMessage = successMessage;
            Fields = fields;
        }

        public AuthMode Mode { get; }
        public string TabLabel { get; }
        public string Heading { get; }
        public string Subtext { get; }
        public string SubmitLabel { get; }
        public string Footnote { get; }
        public string SuccessMessage { get; }
        public IReadOnlyList<AuthField> Fields { get; }
    }
}
