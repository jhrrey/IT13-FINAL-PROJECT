using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>The user details panel: profile, role, clinic, status, dates and sync status.</summary>
public partial class UserDetailsView : ContentView
{
    public UserDetailsView(User user, bool showClinic, bool canManage, bool canChangeStatus, string readOnlyReason)
    {
        InitializeComponent();

        AvatarLabel.Text = user.Initials;
        NameLabel.Text = user.FullName;
        EmailLabel.Text = user.Email;

        AddRow("Username", Text(user.Username));
        AddRow("Role", Badges.Role(user.RoleId, user.RoleName));
        if (showClinic) AddRow("Clinic", Text(user.ClinicName ?? "—"));
        AddRow("Contact no.", Text(string.IsNullOrWhiteSpace(user.ContactNo) ? "—" : user.ContactNo));
        AddRow("Status", Badges.Status(user.Status));
        AddRow("Sync status", Badges.Sync(user.SyncStatus));
        AddRow("Created", Text(LocalTime(user.CreatedAt)));
        AddRow("Last updated", Text(LocalTime(user.UpdatedAt)));

        ActionsGrid.IsVisible = canManage;
        StatusButton.IsVisible = canManage && canChangeStatus;
        StatusButton.Text = user.IsActive ? "Deactivate" : "Activate";
        if (!user.IsActive)
        {
            StatusButton.Style = ThemeResources.Get<Style>("WorkspaceButton");
        }

        ReadOnlyNote.Text = readOnlyReason;
        ReadOnlyNote.IsVisible = !string.IsNullOrEmpty(readOnlyReason);

        EditButton.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
        ResetButton.Clicked += (s, e) => ResetPasswordRequested?.Invoke(this, EventArgs.Empty);
        StatusButton.Clicked += (s, e) => StatusChangeRequested?.Invoke(this, EventArgs.Empty);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);
        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);
    }

    public event EventHandler EditRequested;
    public event EventHandler ResetPasswordRequested;
    public event EventHandler StatusChangeRequested;
    public event EventHandler CloseRequested;

    /// <summary>Dates are stored in UTC and shown in local time.</summary>
    public static string LocalTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("MMM d, yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    private void AddRow(string label, View value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(120)), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        grid.Add(new Label
        {
            Text = label,
            FontSize = 14,
            TextColor = ThemeResources.Color("Muted"),
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);
        grid.Add(value, 1, 0);
        Rows.Children.Add(grid);
    }

    private static Label Text(string text) => new()
    {
        Text = text,
        FontFamily = "DMSansMedium",
        FontSize = 15,
        TextColor = ThemeResources.Color("Navy"),
        VerticalOptions = LayoutOptions.Center
    };
}
