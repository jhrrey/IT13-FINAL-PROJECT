namespace VetCare.Views.Shared;

/// <summary>
/// A dashboard page: rebuilt with fresh numbers on every refresh (every 60 seconds, rule 4) while keeping the
/// scroll position, so a refresh doesn't jump the page back to the top.
/// </summary>
public abstract class DashboardPageBase : ContentView
{
    private ScrollView _scroll;

    protected DashboardPageBase()
    {
        Content = DashboardKit.Page(PageKit.StateMessage("Loading your dashboard…"));
    }

    public DashboardActions Actions { get; set; } = new();

    public void ShowError(string message) =>
        Render(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    protected void Render(params View[] sections)
    {
        double y = _scroll?.ScrollY ?? 0;
        _scroll = DashboardKit.Page(sections);
        Content = _scroll;
        // Keep the position (0 on the first render): the rebuilt page can otherwise be scrolled by focus or re-layout.
        var scroll = _scroll;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), async () => { if (scroll == _scroll) await scroll.ScrollToAsync(0, y, false); });
    }
}
