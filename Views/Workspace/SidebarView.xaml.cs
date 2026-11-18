using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Models.Navigation;
using VetCare.Views.Controls;

namespace VetCare.Views.Workspace;

/// <summary>
/// The workspace sidebar. Shows what the controller gives it (menu, clinic, user) and reports
/// clicks via events. Knows nothing about roles or the database.
/// </summary>
public partial class SidebarView : ContentView
{
    private readonly List<SidebarMenuItem> _items = new();
    private bool _canSwitchClinic;

    public SidebarView()
    {
        InitializeComponent();

        var cardTap = new TapGestureRecognizer();
        cardTap.Tapped += (s, e) =>
        {
            if (!_canSwitchClinic) return;
            if (DropdownLayer.IsVisible) HideClinicList();
            else ClinicSwitcherRequested?.Invoke(this, EventArgs.Empty);
        };
        ClinicCard.GestureRecognizers.Add(cardTap);

        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += (s, e) => HideClinicList();
        DropdownDismiss.GestureRecognizers.Add(dismissTap);

        var signOutTap = new TapGestureRecognizer();
        signOutTap.Tapped += (s, e) => SignOutRequested?.Invoke(this, EventArgs.Empty);
        SignOutButton.GestureRecognizers.Add(signOutTap);

        var signOutHover = new PointerGestureRecognizer();
        signOutHover.PointerEntered += (s, e) => SignOutButton.BackgroundColor = ThemeResources.Color("MenuHover");
        signOutHover.PointerExited += (s, e) => SignOutButton.BackgroundColor = Colors.Transparent;
        SignOutButton.GestureRecognizers.Add(signOutHover);
        HandCursor.Apply(SignOutButton);
    }

    /// <summary>The sign-out button in the user footer was clicked.</summary>
    public event EventHandler SignOutRequested;

    /// <summary>A menu item was clicked (argument: the item's key).</summary>
    public event EventHandler<string> ItemSelected;

    /// <summary>The clinic card was clicked and switching clinics is allowed.</summary>
    public event EventHandler ClinicSwitcherRequested;

    /// <summary>A clinic was picked from the dropdown.</summary>
    public event EventHandler<Clinic> ClinicSelected;

    /// <summary>Builds the menu. Sections are already filtered for the user's role.</summary>
    public void ShowMenu(IReadOnlyList<VisibleSection> sections)
    {
        MenuStack.Children.Clear();
        _items.Clear();

        foreach (var section in sections)
        {
            MenuStack.Children.Add(new Label
            {
                Text = section.Title.ToUpperInvariant(),
                FontFamily = "DMSansBold",
                FontSize = 12,
                CharacterSpacing = 0.67, // 0.5px at 12px (see the letter-spacing note in app.css)
                TextColor = ThemeResources.Color("SectionLabel"),
                Margin = new Thickness(8, 24, 0, 8)
            });

            foreach (var (item, _) in section.Items)
            {
                var view = new SidebarMenuItem(item);
                view.Clicked += (s, e) => ItemSelected?.Invoke(this, item.Key);
                _items.Add(view);
                MenuStack.Children.Add(view);
            }
        }
    }

    public void SetActive(string itemKey)
    {
        foreach (var item in _items) item.IsActive = item.Item.Key == itemKey;
    }

    /// <summary>Shows the clinic name. The chevron and dropdown only appear when switching is allowed.</summary>
    public void ShowClinic(string clinicName, bool canSwitch)
    {
        ClinicNameLabel.Text = clinicName;
        _canSwitchClinic = canSwitch;
        Chevron.IsVisible = canSwitch;
        HandCursor.Apply(ClinicCard);
        if (!canSwitch) HideClinicList();
    }

    public void ShowUser(string fullName, string subtitle, bool canSignOut)
    {
        SignOutButton.IsVisible = canSignOut;
        UserNameLabel.Text = fullName;
        UserRoleLabel.Text = subtitle;
        AvatarLabel.Text = string.IsNullOrWhiteSpace(fullName)
            ? "?"
            : fullName.Trim()[0].ToString().ToUpperInvariant();
    }

    /// <summary>Opens the dropdown under the clinic card. <paramref name="message"/> replaces the list (e.g. an error).</summary>
    public void ShowClinicList(IReadOnlyList<Clinic> clinics, int? selectedClinicId, string message = null)
    {
        ClinicList.Children.Clear();

        if (message != null || clinics.Count == 0)
        {
            ClinicList.Children.Add(new Label
            {
                Text = message ?? "No clinics yet.",
                FontSize = 14,
                TextColor = ThemeResources.Color("Muted"),
                Margin = new Thickness(10, 10)
            });
        }
        else
        {
            foreach (var clinic in clinics)
                ClinicList.Children.Add(CreateClinicRow(clinic, clinic.ClinicId == selectedClinicId));
        }

        Dropdown.Margin = new Thickness(16, ClinicCard.Y + ClinicCard.Height + 6, 16, 0);
        DropdownLayer.IsVisible = true;
        Chevron.Rotation = 180;
    }

    public void HideClinicList()
    {
        DropdownLayer.IsVisible = false;
        Chevron.Rotation = 0;
    }

    private View CreateClinicRow(Clinic clinic, bool selected)
    {
        var name = new Label
        {
            Text = clinic.ClinicName,
            FontFamily = selected ? "DMSansBold" : "DMSans",
            FontSize = 15,
            TextColor = ThemeResources.Color("Navy"),
            LineBreakMode = LineBreakMode.TailTruncation
        };
        var status = new Label
        {
            Text = clinic.Status ?? string.Empty,
            FontSize = 12,
            TextColor = ThemeResources.Color("Muted")
        };
        var check = new LucideIcon
        {
            Icon = "check",
            StrokeColor = ThemeResources.Color("Navy"),
            WidthRequest = 18,
            HeightRequest = 18,
            IsVisible = selected,
            VerticalOptions = LayoutOptions.Center
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        grid.Add(new VerticalStackLayout { Spacing = 1, Children = { name, status } }, 0, 0);
        grid.Add(check, 1, 0);

        var row = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = selected ? ThemeResources.Color("Cyan") : Colors.Transparent,
            Content = grid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => ClinicSelected?.Invoke(this, clinic);
        row.GestureRecognizers.Add(tap);

        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => { if (!selected) row.BackgroundColor = ThemeResources.Color("MenuHover"); };
        hover.PointerExited += (s, e) => { if (!selected) row.BackgroundColor = Colors.Transparent; };
        row.GestureRecognizers.Add(hover);

        HandCursor.Apply(row);
        return row;
    }
}
