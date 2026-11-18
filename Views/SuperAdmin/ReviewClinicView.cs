using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Review sign-up panel (section 4.B, 480px): clinic and administrator details, the duplicate check,
/// and Reject / Approve clinic.
/// </summary>
public sealed class ReviewClinicView : ContentView
{
    public const double PanelWidth = 480;

    private readonly HoverButton _approve, _reject;
    private readonly Label _message;

    public ReviewClinicView(Clinic clinic, IReadOnlyList<ClinicDuplicate> duplicates)
    {
        var title = new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "Review sign-up", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                Badges.ClinicStatus(clinic.Status)
            }
        };
        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(title, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", () => CancelRequested?.Invoke(this, EventArgs.Empty)), 1, 0);

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(24, 20),
            Spacing = 22,
            Children =
            {
                Section("building-2", "Clinic", new (string, string)[]
                {
                    ("Name", clinic.ClinicName), ("Address", clinic.Address), ("Contact no.", ClinicsPage.Phone(clinic.ContactNo)),
                    ("Email", clinic.Email), ("Signed up", clinic.DateCreated?.ToString("MMM d, yyyy · h:mm tt"))
                }),
                Section("user-cog", "Administrator", new (string, string)[]
                {
                    ("Full name", clinic.AdminName), ("Username", clinic.AdminUsername), ("Email", clinic.AdminEmail)
                }),
                DuplicateBox(duplicates)
            }
        };
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };
        body.Children.Add(_message);

        _reject = new HoverButton { Text = "Reject", Style = ThemeResources.Get<Style>("DangerOutlineButton"), Padding = new Thickness(26, 0) };
        _reject.Clicked += (s, e) => RejectRequested?.Invoke(this, EventArgs.Empty);
        _approve = new HoverButton { Text = "Approve clinic", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _approve.Clicked += (s, e) => ApproveRequested?.Invoke(this, EventArgs.Empty);
        var footer = new Grid { Padding = new Thickness(24, 16), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        footer.Add(_reject, 0, 0);
        footer.Add(_approve, 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        Content = layout;
    }

    public event EventHandler ApproveRequested;
    public event EventHandler RejectRequested;
    public event EventHandler CancelRequested;
    public event EventHandler<int> ViewClinicRequested;

    public void ShowMessage(string message)
    {
        _message.Text = message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(message);
    }

    /// <summary>Rule 11: approve / reject need a connection.</summary>
    public void SetOffline(bool offline)
    {
        _approve.IsEnabled = _reject.IsEnabled = !offline;
        ShowMessage(offline ? "You're offline. Approving or rejecting needs an internet connection." : null);
    }

    private View DuplicateBox(IReadOnlyList<ClinicDuplicate> duplicates)
    {
        if (duplicates.Count == 0)
            return Box("Mint", "Success", "circle-check", "No matching clinics found.", null);

        var d = duplicates[0];
        string text = "A clinic with the same " + d.MatchedOn + " already exists: " + d.ClinicName + " (" + d.Status + ")." +
                      (duplicates.Count > 1 ? " " + (duplicates.Count - 1) + " more match." : string.Empty);
        var link = new TextLink { Text = "View clinic", FontSize = 14 };
        link.Clicked += (s, e) => ViewClinicRequested?.Invoke(this, d.ClinicId);
        return Box("WarningSoft", "Warning", "triangle-alert", text, link);
    }

    private static View Box(string background, string color, string icon, string text, View extra)
    {
        var lines = new VerticalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = text, FontFamily = "DMSansMedium", FontSize = 14.5, TextColor = ThemeResources.Color(color), LineBreakMode = LineBreakMode.WordWrap } } };
        if (extra != null) lines.Children.Add(extra);
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(color), WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 1, 0, 0) }, 0, 0);
        grid.Add(lines, 1, 0);
        return new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color(background),
            Content = grid
        };
    }

    /// <summary>A section heading with an icon and label / value rows.</summary>
    public static View Section(string icon, string title, IEnumerable<(string Label, string Value)> rows)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 2),
            Children =
            {
                new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                new Label { Text = title, FontFamily = "NunitoExtraBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
            }
        });
        foreach (var (label, value) in rows)
        {
            var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(new GridLength(110)), new ColumnDefinition(GridLength.Star) } };
            row.Add(new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("Muted") }, 0, 0);
            row.Add(new Label { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, FontSize = 14.5, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap }, 1, 0);
            stack.Children.Add(row);
        }
        return stack;
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };
}
