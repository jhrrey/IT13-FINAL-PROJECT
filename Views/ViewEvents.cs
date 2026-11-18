using VetCare.Models;

namespace VetCare.Views;

public sealed class PageNavigateEventArgs : EventArgs
{
    public PageNavigateEventArgs(NavigationTarget target) => Target = target;

    public NavigationTarget Target { get; }
}

public sealed class AuthModeEventArgs : EventArgs
{
    public AuthModeEventArgs(AuthMode mode, VisualElement origin = null)
    {
        Mode = mode;
        Origin = origin;
    }

    public AuthMode Mode { get; }

    /// <summary>The view that was clicked (used to animate the modal out of it). May be null.</summary>
    public VisualElement Origin { get; }
}

public sealed class AuthSubmitEventArgs : EventArgs
{
    public AuthSubmitEventArgs(IDictionary<string, string> values) => Values = values;

    /// <summary>Field name to the value the user typed.</summary>
    public IDictionary<string, string> Values { get; }
}
