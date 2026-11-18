namespace VetCare.Models
{
    /// <summary>Colour of a service icon's stroke.</summary>
    public enum AccentTone
    {
        Navy,
        Orange
    }

    /// <summary>Colour of the circle behind a service icon.</summary>
    public enum BadgeTone
    {
        Cyan,
        PaleBlue,
        Mint
    }

    /// <summary>One card in the "Our care" grid.</summary>
    public sealed class ServiceItem
    {
        public ServiceItem(string icon, AccentTone iconTone, BadgeTone badge, string title, string description)
        {
            Icon = icon;
            IconTone = iconTone;
            Badge = badge;
            Title = title;
            Description = description;
        }

        /// <summary>Lucide icon name, e.g. "stethoscope".</summary>
        public string Icon { get; }
        public AccentTone IconTone { get; }
        public BadgeTone Badge { get; }
        public string Title { get; }
        public string Description { get; }
    }
}
