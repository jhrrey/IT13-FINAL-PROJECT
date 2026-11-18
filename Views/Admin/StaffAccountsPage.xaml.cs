using VetCare.Views.Shared;

namespace VetCare.Views.Admin;

public partial class StaffAccountsPage : ContentView
{
    public StaffAccountsPage()
    {
        InitializeComponent();
    }

    public UsersManagementView Management => ManagementView;
}
