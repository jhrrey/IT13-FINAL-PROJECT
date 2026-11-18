namespace VetCare.Views.Shared;

/// <summary>
/// Container for a module with a list and a profile page (Owners, Pets): shows one page at a
/// time, with the actions menu, side panel and dialogs layered above it.
/// </summary>
public sealed class ModuleHostView : ContentView
{
    private readonly ContentView _pageHost = new();

    public ModuleHostView()
    {
        Menu = new ActionsMenuLayer();
        Panel = new SidePanelHost();
        Dialog = new ConfirmDialogView();

        // Drawing order: page, then menu, side panel, dialog on top.
        Content = new Grid { Children = { _pageHost, Menu, Panel, Dialog } };
    }

    public ActionsMenuLayer Menu { get; }
    public SidePanelHost Panel { get; }
    public ConfirmDialogView Dialog { get; }

    /// <summary>The list or profile currently shown.</summary>
    public View CurrentPage => _pageHost.Content;

    public void ShowPage(View page)
    {
        Menu.Hide();
        _pageHost.Content = page;
    }
}
