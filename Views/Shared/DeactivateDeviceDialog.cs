using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Deactivate device dialog (Devices & Sync, section 4.C; centered, 480px): the reason as two cards — "Replaced or no
/// longer used" or "Lost or stolen" — and a warning about the computer's unsynced changes.
/// </summary>
public sealed class DeactivateDeviceDialog : ContentView
{
    public const string Replaced = "Replaced";
    public const string LostOrStolen = "Lost or stolen";

    private readonly ClinicDevice _device;
    private readonly List<(string Reason, Border Card, Ellipse Dot)> _cards = new();
    private readonly Border _infoBox;
    private readonly Label _info, _message;
    private readonly HoverButton _deactivate;
    private string _reason = Replaced;

    public DeactivateDeviceDialog(ClinicDevice device)
    {
        _device = device;
        var dim = new BoxView { Color = Color.FromRgba(10, 46, 79, 0.45) };
        var dimTap = new TapGestureRecognizer();
        dimTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        dim.GestureRecognizers.Add(dimTap);

        (_infoBox, _info) = PharmacyKit.InfoBox("WarningSoft", "Warning");
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };

        var cancel = new HoverButton { Text = "Cancel", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
        cancel.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _deactivate = new HoverButton { Text = "Deactivate", Style = ThemeResources.Get<Style>("DangerButton") };
        _deactivate.Clicked += (s, e) => ConfirmRequested?.Invoke(this, _reason);
        var buttons = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        buttons.Add(cancel, 0, 0);
        buttons.Add(_deactivate, 1, 0);

        var stack = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new IconBadge { Icon = "monitor", Size = 48, IconSize = 22, CircleColor = ThemeResources.Color("DangerSoft"), IconColor = ThemeResources.Color("Danger"), HorizontalOptions = LayoutOptions.Start },
                new Label { Text = "Deactivate " + device.DeviceName + "?", FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = device.ClinicName + (device.PendingChanges > 0 ? " · " + device.PendingChanges + " unsynced " + (device.PendingChanges == 1 ? "change" : "changes") : string.Empty), FontSize = 14, TextColor = ThemeResources.Color("BodyText") },
                ReasonCard(Replaced, "Replaced or no longer used", "It will stop syncing after this."),
                ReasonCard(LostOrStolen, "Lost or stolen", "Block it now. Unsynced changes will be refused."),
                _infoBox,
                _message,
                buttons
            }
        };
        var card = new Border
        {
            WidthRequest = 480,
            MaximumWidthRequest = 480,
            Padding = new Thickness(28, 24),
            Margin = new Thickness(16),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            BackgroundColor = ThemeResources.Color("Surface"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.18f, Radius = 24, Offset = new Point(0, 8) },
            Content = stack
        };
        Content = new Grid { Children = { dim, card } };
        Select(Replaced);
    }

    public event EventHandler<string> ConfirmRequested;
    public event EventHandler CancelRequested;

    public void ShowMessage(string message)
    {
        _message.Text = message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(message);
    }

    public void SetBusy(bool busy)
    {
        _deactivate.IsEnabled = !busy;
        _deactivate.Text = busy ? "Please wait…" : "Deactivate";
    }

    private View ReasonCard(string reason, string title, string subtitle)
    {
        var dot = new Ellipse { WidthRequest = 10, HeightRequest = 10, Fill = Colors.Transparent, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var ring = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            StrokeThickness = 2,
            Stroke = ThemeResources.Color("Navy"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 2, 0, 0),
            Content = dot
        };
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(ring, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = title, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = subtitle, FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.WordWrap }
            }
        }, 1, 0);
        var card = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = grid
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Select(reason);
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        SemanticProperties.SetDescription(card, title);
        _cards.Add((reason, card, dot));
        return card;
    }

    private void Select(string reason)
    {
        _reason = reason;
        foreach (var (r, card, dot) in _cards)
        {
            bool on = r == reason;
            card.Stroke = ThemeResources.Color(on ? "Navy" : "BorderColor");
            card.BackgroundColor = ThemeResources.Color(on ? "CyanSoft" : "Surface");
            dot.Fill = on ? ThemeResources.Color("Navy") : Colors.Transparent;
        }

        int pending = _device.PendingChanges;
        string changes = pending == 1 ? "1 change" : pending + " changes";
        if (reason == LostOrStolen)
        {
            _infoBox.IsVisible = true;
            _infoBox.BackgroundColor = ThemeResources.Color("DangerSoft");
            _info.TextColor = ThemeResources.Color("DangerHover");
            _info.Text = pending > 0 ? "Any of its " + changes + " that haven't synced will be lost." : "It's blocked right away and can't sign in or sync again.";
        }
        else
        {
            _infoBox.IsVisible = pending > 0;
            _infoBox.BackgroundColor = ThemeResources.Color("WarningSoft");
            _info.TextColor = ThemeResources.Color("Warning");
            _info.Text = "This computer still has " + changes + " that haven't synced. Sync it first so no data is lost.";
        }
    }
}
