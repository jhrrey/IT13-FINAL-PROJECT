using VetCare.Models;
using VetCare.Models.Repositories;
using VetCare.Views;

namespace VetCare.Controllers;

/// <summary>Creates the landing page, starts the feature controllers, and handles navigation.</summary>
public sealed class LandingController
{
    private readonly LandingContent _content;
    private readonly AuthModel _authModel;
    private LandingPage _view;
    private AuthController _auth;

    public LandingController(LandingContent content, AuthModel authModel)
    {
        _content = content;
        _authModel = authModel;
    }

    /// <summary>Raised after a successful sign-in; the app then opens the workspace.</summary>
    public event EventHandler<UserSession> SignedIn;

    public Page CreateView()
    {
        _view = new LandingPage(_content);
        _view.NavigateRequested += OnNavigateRequested;

        _auth = new AuthController(_authModel, _view.AuthCard, _view.AccountModal,
            new UserRepository(), new ClinicRepository());
        _auth.SignedIn += (s, session) => SignedIn?.Invoke(this, session);
        _auth.Start();

        return _view;
    }

    private async void OnNavigateRequested(object sender, PageNavigateEventArgs e)
    {
        if (e.Target == NavigationTarget.SignIn)
        {
            // Close the registration modal if it is open, scroll to the card, then focus the email field.
            await _auth.ShowSignInAsync();
            await _view.ScrollToAsync(e.Target);
            _auth.FocusSignIn();
            return;
        }

        await _view.ScrollToAsync(e.Target);
    }
}
