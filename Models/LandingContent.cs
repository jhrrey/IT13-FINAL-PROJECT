using System.Collections.Generic;

namespace VetCare.Models
{
    /// <summary>All of the copy shown on the landing page. Edit the text here.</summary>
    public sealed class LandingContent
    {
        public BrandContent Brand { get; set; }
        public NavbarContent Navbar { get; set; }
        public HeroContent Hero { get; set; }
        public QuoteContent Quote { get; set; }
        public ServicesContent Services { get; set; }
        public AboutContent About { get; set; }
        public FooterContent Footer { get; set; }

        public static LandingContent CreateDefault()
        {
            return new LandingContent
            {
                Brand = new BrandContent
                {
                    Name = "VetCare",
                    Tagline = "Veterinary Clinic"
                },

                Navbar = new NavbarContent
                {
                    Links = new[]
                    {
                        new NavLink("Our care", NavigationTarget.OurCare),
                        new NavLink("About VetCare", NavigationTarget.About)
                    }
                },

                Hero = new HeroContent
                {
                    Eyebrow = "GOOD CARE. HAPPY PAWS.",
                    TitleLine1 = "For the love",
                    TitleLine2Prefix = "of ",
                    TitleLine2Accent = "every paw.",
                    Lead = "They make your days brighter. We’re here to help make their whole life healthier—with thoughtful care at every stage.",
                    PrimaryCta = new NavLink("Find their care", NavigationTarget.OurCare),
                    SecondaryCta = new NavLink("Get to know us", NavigationTarget.About),
                    Note = "Here for the big moments, and the little ones too.",
                    CardEyebrow = "YOUR PET’S CARE, TOGETHER"
                },

                Quote = new QuoteContent
                {
                    Text = "“They’re family. Their care should feel that way, too.”",
                    Subtext = "A little more understanding. A little less worry. A whole lot of love."
                },

                Services = new ServicesContent
                {
                    Eyebrow = "HERE FOR THEIR EVERY CHAPTER",
                    TitleLine1 = "A little care goes a",
                    TitleAccent = "long way.",
                    Intro = "From everyday checkups to life’s little surprises, explore care for the moments that matter.",
                    LinkLabel = "Learn more",
                    Items = new[]
                    {
                        new ServiceItem("stethoscope", AccentTone.Navy, BadgeTone.Cyan,
                            "Wellness & checkups",
                            "A thoughtful look at the whole picture, and a little reassurance along the way."),
                        new ServiceItem("shield-check", AccentTone.Orange, BadgeTone.PaleBlue,
                            "Preventive care",
                            "Everyday protection to help your best friend feel their best, season after season."),
                        new ServiceItem("microscope", AccentTone.Navy, BadgeTone.Mint,
                            "Diagnostics",
                            "Clear answers and a closer look, so you can make informed care decisions."),
                        new ServiceItem("sparkles", AccentTone.Orange, BadgeTone.PaleBlue,
                            "Dental health",
                            "Gentle attention to the little things that help keep those tails wagging."),
                        new ServiceItem("bone", AccentTone.Navy, BadgeTone.Mint,
                            "Everyday advice",
                            "Friendly guidance for nutrition, new routines, and all your “is this normal?” moments.")
                    }
                },

                About = new AboutContent
                {
                    Eyebrow = "A CLINIC WITH A SOFT SPOT",
                    TitleLine1 = "Good medicine.",
                    TitleLine2 = "Even better humans.",
                    Text = "The best care starts with listening. VetCare is built around the little details that make a visit feel kinder: a patient hello, thoughtful answers, and a team that sees the whole pet—not just the chart.",
                    Cta = new NavLink("Meet the care we offer", NavigationTarget.OurCare)
                },

                Footer = new FooterContent
                {
                    Motto = "Care for them. Comfort for you.",
                    BackToTop = "Back to top"
                }
            };
        }
    }

    public sealed class NavLink
    {
        public NavLink(string label, NavigationTarget target)
        {
            Label = label;
            Target = target;
        }

        public string Label { get; }
        public NavigationTarget Target { get; }
    }

    public sealed class BrandContent
    {
        public string Name { get; set; }
        public string Tagline { get; set; }
    }

    public sealed class NavbarContent
    {
        public IReadOnlyList<NavLink> Links { get; set; }
    }

    public sealed class HeroContent
    {
        public string Eyebrow { get; set; }
        public string TitleLine1 { get; set; }
        public string TitleLine2Prefix { get; set; }
        public string TitleLine2Accent { get; set; }
        public string Lead { get; set; }
        public NavLink PrimaryCta { get; set; }
        public NavLink SecondaryCta { get; set; }
        public string Note { get; set; }
        public string CardEyebrow { get; set; }
    }

    public sealed class QuoteContent
    {
        public string Text { get; set; }
        public string Subtext { get; set; }
    }

    public sealed class ServicesContent
    {
        public string Eyebrow { get; set; }
        public string TitleLine1 { get; set; }
        public string TitleAccent { get; set; }
        public string Intro { get; set; }
        public string LinkLabel { get; set; }
        public IReadOnlyList<ServiceItem> Items { get; set; }
    }

    public sealed class AboutContent
    {
        public string Eyebrow { get; set; }
        public string TitleLine1 { get; set; }
        public string TitleLine2 { get; set; }
        public string Text { get; set; }
        public NavLink Cta { get; set; }
    }

    public sealed class FooterContent
    {
        public string Motto { get; set; }
        public string BackToTop { get; set; }
    }
}
