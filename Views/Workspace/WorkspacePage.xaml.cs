using VetCare.Views.Shared;

namespace VetCare.Views.Workspace;

public partial class WorkspacePage : ContentPage
{
    public WorkspacePage()
    {
        InitializeComponent();
    }

    public SidebarView Sidebar => SidebarPanel;

    /// <summary>"Hello, …", connection status and the profile chip.</summary>
    public TopBarView TopBar => TopBarPanel;

    /// <summary>The menu opened from the profile chip.</summary>
    public ActionsMenuLayer AccountMenu => AccountMenuLayer;

    /// <summary>Replaces the module page under the top bar with <paramref name="page"/>.</summary>
    public void ShowModule(View page) => ContentHost.Content = page;
}
