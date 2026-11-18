using VetCare.Controllers;
using VetCare.Models;
using VetCare.Models.Repositories;

namespace VetCare;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light; // the design is light-only
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = new Window
        {
            Title = "VetCare Veterinary Clinic",
            Width = 1320,
            Height = 900,
            MinimumWidth = 420,
            MinimumHeight = 560
        };

        ShowLanding(window);

        return window;
    }

    /// <summary>The landing / sign-in page. A successful sign-in opens the workspace.</summary>
    private static void ShowLanding(Window window)
    {
        var landing = new LandingController(LandingContent.CreateDefault(), new AuthModel());
        landing.SignedIn += (s, session) => ShowWorkspace(window, session);
        window.Page = landing.CreateView();
    }

    /// <summary>The workspace (sidebar + modules). Signing out returns to a fresh landing page.</summary>
    private static void ShowWorkspace(Window window, UserSession session)
    {
        var workspace = new WorkspaceController(session, new ClinicRepository());
        workspace.SignOutRequested += (s, e) => ShowLanding(window);
        window.Page = workspace.CreateView();
    }
}
