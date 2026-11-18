using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Admin details panel (section 3.D, 440px): avatar, name, status and role pills, the account rows, the privacy
/// note, and Reset password / Edit / Deactivate (or Activate).
/// </summary>
public sealed class AdminDetailsView : ContentView
{
    public const double PanelWidth = 440;

    public AdminDetailsView(User admin)
    {
        var head = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                WithStart(PageKit.Avatar(admin.Initials, 64, 14, 22)),
                new Label { Text = admin.FullName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy") },
                new HorizontalStackLayout { Spacing = 8, Children = { Badges.AdminStatus(admin.DisplayStatus), Badges.Pill("Administrator", "Navy", "PaleBlue") } }
            }
        };

        var clinicRow = new VerticalStackLayout { Spacing = 4, Children = { Value(admin.ClinicName ?? "—"), Badges.SmallClinicStatus(admin.ClinicStatus) } };
        var lastLogin = User.ToLocal(admin.LastLoginAt);
        var rows = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                Row("building-2", "Clinic", clinicRow),
                Row("at-sign", "Username", Value(admin.Username)),
                Row("mail", "Email", Value(admin.Email)),
                Row("phone", "Contact no.", Value(string.IsNullOrEmpty(admin.ContactNo) ? "—" : ClinicsPage.Phone(admin.ContactNo))),
                Row("calendar", "Created", Value(User.ToLocal(admin.CreatedAt)?.ToString("MMM d, yyyy"))),
                Row("calendar", "Last updated", Value(User.ToLocal(admin.UpdatedAt)?.ToString("MMM d, yyyy · h:mm tt"))),
                Row("log-in", "Last login", Value(lastLogin?.ToString("MMM d, yyyy · h:mm tt") ?? "Never")),
                Row("refresh-cw", "Sync", Badges.Sync(admin.SyncStatus ?? SyncStatuses.Synced))
            }
        };
        if (admin.MustChangePassword)
            rows.Children.Add(Row("key-round", "Password", Value("Temporary – they'll set their own at the next sign-in")));

        var note = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0) },
                new Label { Text = "Super Admins can manage this account but can't sign in as this user.", FontSize = 13, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap, MaximumWidthRequest = 340 }
            }
        };

        var body = new VerticalStackLayout { Padding = new Thickness(24, 20), Spacing = 22, Children = { head, rows, note } };

        var reset = new HoverButton { Text = "Reset password", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(14, 0) };
        reset.Clicked += (s, e) => ResetPasswordRequested?.Invoke(this, EventArgs.Empty);
        var edit = new HoverButton { Text = "Edit", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(18, 0) };
        edit.Clicked += (s, e) => EditRequested?.Invoke(this, EventArgs.Empty);
        var status = new TextLink
        {
            Text = admin.IsActive ? "Deactivate" : "Activate",
            FontFamily = "DMSansBold",
            FontSize = 15,
            TextColor = ThemeResources.Color(admin.IsActive ? "Danger" : "Success"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        status.Clicked += (s, e) => StatusChangeRequested?.Invoke(this, EventArgs.Empty);
        var footer = new Grid { Padding = new Thickness(24, 16), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        footer.Add(reset, 0, 0);
        footer.Add(edit, 1, 0);
        footer.Add(status, 2, 0);

        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = "Admin details", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)), 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        Content = layout;
    }

    public event EventHandler CloseRequested;
    public event EventHandler EditRequested;
    public event EventHandler ResetPasswordRequested;
    public event EventHandler StatusChangeRequested;

    private static View Row(string icon, string label, View value)
    {
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(96)), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("BodyText"), WidthRequest = 17, HeightRequest = 17, VerticalOptions = LayoutOptions.Center }, 0, 0);
        grid.Add(new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        grid.Add(value, 2, 0);
        return grid;
    }

    private static Label Value(string text) => new()
    {
        Text = string.IsNullOrEmpty(text) ? "—" : text,
        FontSize = 14.5,
        FontFamily = "DMSansMedium",
        TextColor = ThemeResources.Color("Navy"),
        LineBreakMode = LineBreakMode.WordWrap,
        VerticalOptions = LayoutOptions.Center
    };

    private static View WithStart(View view)
    {
        view.HorizontalOptions = LayoutOptions.Start;
        return view;
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };
}
