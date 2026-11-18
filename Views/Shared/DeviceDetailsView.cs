using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// Device details panel (Devices & Sync, section 4.B, 480px): the device, its health and Active switch, info rows,
/// the last 10 syncs, and Deactivate / Reactivate. Administrators can rename their clinic's computers.
/// </summary>
public sealed class DeviceDetailsView : ContentView
{
    public const double PanelWidth = 480;

    private readonly ClinicDevice _device;
    private readonly Label _nameLabel, _renameError;
    private readonly View _nameRow;
    private readonly View _renameRow;
    private readonly Entry _renameEntry;

    public DeviceDetailsView(ClinicDevice device, (int Success, int Total) rate, IReadOnlyList<SyncLogEntry> history, bool canRename)
    {
        _device = device;
        var now = DateTime.Now;

        // Header ---------------------------------------------------------------------------------------------------
        var tile = new Border
        {
            WidthRequest = 56,
            HeightRequest = 56,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = ThemeResources.Color("Cyan"),
            Content = new LucideIcon { Icon = "monitor", StrokeColor = ThemeResources.Color("Navy"), WidthRequest = 26, HeightRequest = 26, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
        _nameLabel = new Label { Text = device.DeviceName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap, VerticalOptions = LayoutOptions.Center };
        var nameStack = new HorizontalStackLayout { Spacing = 6, Children = { _nameLabel } };
        if (canRename)
            nameStack.Children.Add(PageKit.IconButton("pencil", "Rename this computer", () => ShowRename(true)));
        _nameRow = nameStack;

        (Border renameBox, _renameEntry) = PharmacyKit.TextInput("Device name", maxLength: 100);
        _renameEntry.Text = device.DeviceName;
        _renameError = PharmacyKit.ErrorLabel();
        var save = PharmacyKit.SmallButton("Save", "check", true, () => RenameRequested?.Invoke(this, _renameEntry.Text?.Trim()), "Save the new name");
        var cancel = PharmacyKit.SmallButton("Cancel", null, false, () => ShowRename(false), "Cancel renaming");
        var renameGrid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        renameGrid.Add(renameBox, 0, 0);
        renameGrid.Add(save, 1, 0);
        renameGrid.Add(cancel, 2, 0);
        _renameRow = new VerticalStackLayout { IsVisible = false, Children = { renameGrid, _renameError } };

        var active = new ToggleSwitch("Active", device.IsActive) { AskFirst = true };
        active.TapRequested += (s, e) => (device.IsActive ? DeactivateRequested : ReactivateRequested)?.Invoke(this, EventArgs.Empty);
        var head = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        head.Add(tile, 0, 0);
        head.Add(new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { _nameRow, _renameRow, new HorizontalStackLayout { Spacing = 14, Children = { Badges.DeviceHealth(device.Health), active } } }
        }, 1, 0);

        // Info rows ----------------------------------------------------------------------------------------------------
        string reason = device.IsActive ? null : device.DeactivatedReason == null ? "Turned off" : "Turned off · " + device.DeactivatedReason;
        var rows = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Row("building-2", "Clinic", device.ClinicName),
                Row("hash", "Machine code", device.MachineCode, mono: true),
                Row("calendar", "Registered", device.RegisteredAt.ToString("MMM d, yyyy")),
                Row("cloud", "Last sync", device.LastSyncAt == null ? "Never" : device.LastSyncAt.Value.ToString("MMM d, h:mm tt") + " · " + GreetingHelper.Relative(device.LastSyncAt.Value, now)),
                Row("upload", "Pending changes", device.PendingChanges.ToString()),
                Row("activity", "Success rate (7 days)", rate.Total == 0 ? "No syncs yet" : Math.Round(100.0 * rate.Success / rate.Total) + "% · " + rate.Success + " of " + rate.Total)
            }
        };
        if (!string.IsNullOrEmpty(device.AppVersion)) rows.Children.Add(Row("info", "App version", device.AppVersion));
        if (reason != null) rows.Children.Add(Row("ban", "Status", reason));

        // Recent syncs -------------------------------------------------------------------------------------------------
        var recent = new VerticalStackLayout { Spacing = 0 };
        recent.Children.Add(new Label { Text = "Recent syncs", FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(0, 0, 0, 8) });
        if (history.Count == 0) recent.Children.Add(new Label { Text = "No syncs yet.", FontSize = 14, TextColor = ThemeResources.Color("Muted") });
        for (int i = 0; i < history.Count; i++)
        {
            var h = history[i];
            if (i > 0) recent.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            var line = new Grid { Padding = new Thickness(0, 8), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            var left = new VerticalStackLayout { Spacing = 4 };
            left.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label { Text = h.StartedAt.ToString("MMM d, h:mm tt"), FontSize = 13.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center },
                    Badges.SyncDirection(h.Direction),
                    new Label { Text = "↑ " + h.RecordsSent + " / ↓ " + h.RecordsReceived + (h.ConflictsFound > 0 ? " · " + h.ConflictsFound + " conflicts" : string.Empty), FontSize = 13, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center }
                }
            });
            if (h.Status == "Failed" && !string.IsNullOrEmpty(h.ErrorMessage))
                left.Children.Add(new Label { Text = h.ErrorMessage, FontSize = 12.5, TextColor = ThemeResources.Color("DangerHover"), LineBreakMode = LineBreakMode.WordWrap });
            line.Add(left, 0, 0);
            line.Add(Badges.SyncResult(h.Status), 1, 0);
            recent.Children.Add(line);
        }

        var note = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new LucideIcon { Icon = "lock", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center },
                new Label { Text = "Only sync details are shown. Clinic records stay private.", FontSize = 13, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }
            }
        };

        var body = new VerticalStackLayout { Padding = new Thickness(24, 20), Spacing = 22, Children = { head, rows, recent, note } };

        var toggle = new HoverButton
        {
            Text = device.IsActive ? "Deactivate device" : "Reactivate device",
            Style = ThemeResources.Get<Style>(device.IsActive ? "DangerOutlineButton" : "OutlineButton")
        };
        if (!device.IsActive) { toggle.TextColor = ThemeResources.Color("Success"); toggle.BorderColor = ThemeResources.Color("Success"); }
        toggle.Clicked += (s, e) => (device.IsActive ? DeactivateRequested : ReactivateRequested)?.Invoke(this, EventArgs.Empty);
        var footer = new Grid { Padding = new Thickness(24, 16), Children = { toggle } };

        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = "Device details", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)), 1, 0);

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        layout.Add(new ScrollView { Content = body }, 0, 1);
        layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        Content = layout;
    }

    public event EventHandler CloseRequested;
    public event EventHandler DeactivateRequested;
    public event EventHandler ReactivateRequested;
    public event EventHandler<string> RenameRequested;

    public ClinicDevice Device => _device;

    public void ShowRenameError(string message) => PharmacyKit.SetError(_renameError, message, null);

    private void ShowRename(bool on)
    {
        _nameRow.IsVisible = !on;
        _renameRow.IsVisible = on;
        if (!on) PharmacyKit.SetError(_renameError, null, null);
    }

    private static View Row(string icon, string label, string value, bool mono = false)
    {
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(150)), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("BodyText"), WidthRequest = 17, HeightRequest = 17, VerticalOptions = LayoutOptions.Center }, 0, 0);
        grid.Add(new Label { Text = label, FontSize = 14, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, 1, 0);
        grid.Add(new Label
        {
            Text = string.IsNullOrEmpty(value) ? "—" : value,
            FontSize = mono ? 13.5 : 14.5,
            FontFamily = mono ? "Consolas" : "DMSansMedium",
            TextColor = ThemeResources.Color("Navy"),
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        }, 2, 0);
        return grid;
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };
}
