using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// Reset password panel (section 3.C, 420px): the admin, "Generate temporary password", then the password shown
/// once in large monospace with Copy, and a reminder to give it privately.
/// </summary>
public sealed class ResetPasswordView : ContentView
{
    public const double PanelWidth = 420;

    private readonly HoverButton _generate;
    private readonly VerticalStackLayout _result;
    private readonly Label _password, _message, _copied;

    public ResetPasswordView(User admin)
    {
        var card = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        card.Add(PageKit.Avatar(admin.Initials, 48, 10, 16), 0, 0);
        card.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = admin.FullName, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = "@" + admin.Username, FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") },
                new Label { Text = admin.ClinicName ?? "—", FontSize = 13.5, TextColor = ThemeResources.Color("Muted") }
            }
        }, 1, 0);
        var cardBox = new Border { Padding = new Thickness(16, 14), Stroke = ThemeResources.Color("BorderColor"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Content = card };

        _generate = new HoverButton { Text = "Generate temporary password", Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _generate.Clicked += (s, e) => GenerateRequested?.Invoke(this, EventArgs.Empty);

        _password = new Label { FontFamily = "Consolas", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
        var copy = PharmacyKit.SmallButton("Copy", "copy", false, async () => await CopyAsync(), "Copy the password");
        var passwordRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        passwordRow.Add(_password, 0, 0);
        passwordRow.Add(copy, 1, 0);
        _copied = new Label { Text = "Copied.", FontSize = 13, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color("Success"), IsVisible = false };
        _result = new VerticalStackLayout
        {
            Spacing = 10,
            IsVisible = false,
            Children =
            {
                new Border { Padding = new Thickness(18, 16), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, BackgroundColor = ThemeResources.Color("CyanSoft"), Content = passwordRow },
                _copied,
                new Label { Text = "This password is shown only once. Give it to the admin privately. They'll set their own password at the next sign-in.", FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap }
            }
        };
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        var body = new VerticalStackLayout { Padding = new Thickness(24, 20), Spacing = 18, Children = { cardBox, _generate, _result, _message } };
        var done = new HoverButton { Text = "Done", Style = ThemeResources.Get<Style>("OutlineButton") };
        done.Clicked += (s, e) => DoneRequested?.Invoke(this, EventArgs.Empty);
        var footer = new Grid { Padding = new Thickness(24, 16), Children = { done } };

        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = "Reset password", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", () => DoneRequested?.Invoke(this, EventArgs.Empty)), 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        Content = layout;
    }

    public event EventHandler GenerateRequested;
    public event EventHandler DoneRequested;

    public void ShowPassword(string password)
    {
        _password.Text = password;
        _result.IsVisible = true;
        _generate.IsVisible = false;
        ShowMessage(null);
    }

    public void ShowMessage(string message)
    {
        _message.Text = message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void SetBusy(bool busy)
    {
        _generate.IsEnabled = !busy;
        _generate.Text = busy ? "Generating…" : "Generate temporary password";
    }

    private async Task CopyAsync()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(_password.Text);
            _copied.IsVisible = true;
        }
        catch (Exception)
        {
            _copied.Text = "Couldn't copy. Select and type it instead.";
            _copied.TextColor = ThemeResources.Color("Danger");
            _copied.IsVisible = true;
        }
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };
}
