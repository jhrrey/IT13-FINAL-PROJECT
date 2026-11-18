using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Workspace;

/// <summary>
/// The workspace top bar: "Hello, Maria Santos" with a role pill on the left; on the right the
/// connection status ("All synced" / "Offline") and the signed-in user's profile chip.
/// </summary>
public sealed class TopBarView : ContentView
{
    private readonly Label _greeting;
    // DM Sans leaves extra room under the letters, so the 15px greeting sits lower in its box than
    // the 12px pill text does. Moving the pill down 2px lines the two up visually.
    private readonly ContentView _roleHost = new() { VerticalOptions = LayoutOptions.Center, TranslationY = 2 };
    private readonly Border _syncPill;
    private readonly LucideIcon _syncIcon;
    private readonly Label _syncLabel;
    private readonly Border _profileChip;
    private readonly Label _profileInitials;
    private readonly Label _profileName;

    public TopBarView()
    {
        BackgroundColor = ThemeResources.Color("SidebarBackground");

        // Left: greeting + role
        _greeting = new Label { FontSize = 15, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var left = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center, Children = { _greeting, _roleHost } };

        // Right: sync status pill
        _syncIcon = new LucideIcon { WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
        _syncLabel = new Label { FontFamily = "DMSansBold", FontSize = 13, VerticalOptions = LayoutOptions.Center };
        _syncPill = new Border
        {
            Padding = new Thickness(12, 6),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 6, Children = { _syncIcon, _syncLabel } }
        };

        // Right: profile chip (avatar + first name + chevron), opens the account menu
        _profileInitials = new Label
        {
            FontFamily = "DMSansBold",
            FontSize = 13,
            TextColor = ThemeResources.Color("Navy"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _profileName = new Label
        {
            FontFamily = "DMSansMedium",
            FontSize = 14,
            TextColor = ThemeResources.Color("Navy"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaximumWidthRequest = 160
        };
        _profileChip = new Border
        {
            Padding = new Thickness(6, 5, 12, 5),
            BackgroundColor = ThemeResources.Color("Surface"),
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = ThemeResources.Color("Navy"), Offset = new Point(0, 2), Radius = 8, Opacity = 0.05f },
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 32,
                        HeightRequest = 32,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 16 },
                        BackgroundColor = ThemeResources.Color("Cyan"),
                        Content = _profileInitials
                    },
                    _profileName,
                    new LucideIcon { Icon = "chevron-down", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        SemanticProperties.SetDescription(_profileChip, "Account menu");

        var chipTap = new TapGestureRecognizer();
        chipTap.Tapped += (s, e) => ProfileMenuRequested?.Invoke(this, _profileChip);
        _profileChip.GestureRecognizers.Add(chipTap);
        var chipHover = new PointerGestureRecognizer();
        chipHover.PointerEntered += (s, e) => _profileChip.BackgroundColor = ThemeResources.Color("RowHover");
        chipHover.PointerExited += (s, e) => _profileChip.BackgroundColor = ThemeResources.Color("Surface");
        _profileChip.GestureRecognizers.Add(chipHover);
        HandCursor.Apply(_profileChip);

        var right = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = { _syncPill, _profileChip } };

        var row = new Grid
        {
            HeightRequest = 72,
            Padding = new Thickness(40, 0),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 16
        };
        row.Add(left, 0, 0);
        row.Add(right, 1, 0);

        Content = new Grid
        {
            Children =
            {
                row,
                new BoxView { HeightRequest = 1, Color = ThemeResources.Color("SidebarBorder"), VerticalOptions = LayoutOptions.End }
            }
        };

        ShowSync(true);
    }

    /// <summary>The profile chip was clicked (argument: the chip, to anchor the menu).</summary>
    public event EventHandler<View> ProfileMenuRequested;

    public void ShowUser(UserSession session)
    {
        string name = session.IsGuest ? "Guest" : session.FullName;
        _greeting.FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = "Hello, ", TextColor = ThemeResources.Color("BodyText") },
                new Span { Text = name, FontFamily = "DMSansBold", TextColor = ThemeResources.Color("Navy") }
            }
        };

        _roleHost.Content = session.IsGuest ? null : Badges.Role(session.RoleId, ShortRoleName(session));
        _roleHost.IsVisible = !session.IsGuest;

        var (first, last) = PersonName.Split(session.FullName ?? "Guest");
        _profileInitials.Text = PersonName.Initials(first, last);
        _profileName.Text = string.IsNullOrEmpty(first) ? name : first;
        _profileChip.IsVisible = !session.IsGuest;
    }

    /// <summary>"All synced" (mint) when the central database answers, otherwise "Offline" (amber).</summary>
    public void ShowSync(bool online)
    {
        _syncPill.BackgroundColor = ThemeResources.Color(online ? "SuccessSoft" : "WarningSoft");
        _syncIcon.Icon = online ? "cloud-check" : "cloud-off";
        _syncIcon.StrokeColor = ThemeResources.Color(online ? "Success" : "Warning");
        _syncLabel.Text = online ? "All synced" : "Offline";
        _syncLabel.TextColor = ThemeResources.Color(online ? "Success" : "Warning");
        SemanticProperties.SetDescription(_syncPill, online ? "Connected to the VetCare server, all changes synced" : "Can't reach the VetCare server");
    }

    /// <summary>Short labels for the pill: "Staff" for Cashier/Staff, otherwise the role name.</summary>
    private static string ShortRoleName(UserSession session) =>
        session.RoleId == RoleIds.CashierStaff ? "Staff" : session.RoleName;
}
