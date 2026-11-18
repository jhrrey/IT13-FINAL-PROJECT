using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.SuperAdmin;

/// <summary>
/// System settings (System Settings module, section 4): a category menu, setting rows with inputs, "Reset to default",
/// "Clinic can override" tags, the overrides link, a sticky save bar and the Change history tab.
/// The Administrator version (<see cref="ClinicMode"/>) shows only the settings a clinic can override, saved as the
/// clinic's own values.
/// </summary>
public sealed class SystemSettingsPage : ContentView
{
    public const string HistoryKey = "History";
    private const double MenuWidth = 240;

    private readonly bool _clinicMode;
    private readonly Grid _layout;
    private readonly VerticalStackLayout _menu = new() { Spacing = 2 };
    private readonly ScrollView _menuStrip;
    private readonly HorizontalStackLayout _menuStripRow = new() { Spacing = 6 };
    private readonly ContentView _content = new();
    private readonly Border _menuCard;
    private readonly Grid _saveBar;
    private readonly Label _unsavedLabel, _offlineLabel;
    private readonly HoverButton _save;
    private readonly Dictionary<string, Row> _rows = new();
    private readonly List<(string Key, Border Item, Label Text, LucideIcon Icon, Ellipse Dot)> _menuItems = new();
    private Dictionary<string, SystemSetting> _global = new();
    private Dictionary<string, SystemSetting> _clinic = new();
    private Dictionary<string, int> _overrideCounts = new();
    private string _category;
    private bool _offline;

    private sealed class Row
    {
        public SettingDefinition Def;
        public string Original;
        public string Current;
        public Border Frame;
        public Label Error;
        public TextLink Reset;
        public Action<string> SetInput;
    }

    public SystemSettingsPage(bool clinicMode)
    {
        _clinicMode = clinicMode;

        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new PageHeader
        {
            Icon = "settings",
            Title = clinicMode ? "Clinic settings" : "System settings",
            Subtitle = clinicMode ? "Your clinic's own values. Anything you don't change uses the VetCare default." : "Set the rules VetCare follows for every clinic."
        }, 0, 0);
        header.Add(new Label { Text = "Changes reach clinic computers on their next sync.", FontSize = 13.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 4) }, 1, 0);

        _menuCard = new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(8), WidthRequest = MenuWidth, VerticalOptions = LayoutOptions.Start, Content = _menu };
        _menuStrip = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _menuStripRow, IsVisible = false };

        var categories = SettingCatalog.Categories.Where(c => !clinicMode || SettingCatalog.All.Any(d => d.Category == c.Key && d.ClinicOverride)).ToList();
        foreach (var (key, label, icon, _) in categories) AddMenuItem(key, label, icon);
        if (!clinicMode) AddMenuItem(HistoryKey, "Change history", "history");

        _layout = new Grid { ColumnSpacing = 20, RowSpacing = 16 };
        _layout.SizeChanged += (s, e) => Arrange(_layout.Width);
        Arrange(0);

        var page = DashboardKit.Page(header, _menuStrip, _layout);

        // Sticky save bar -----------------------------------------------------------------------------------------------
        _unsavedLabel = new Label { FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
        _offlineLabel = new Label { Text = "Connect to the internet to save settings.", FontSize = 13.5, TextColor = ThemeResources.Color("Danger"), VerticalOptions = LayoutOptions.Center, IsVisible = false };
        var discard = new HoverButton { Text = "Discard", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(20, 0) };
        discard.Clicked += (s, e) => Discard();
        _save = new HoverButton { Text = "Save changes", Style = ThemeResources.Get<Style>("WorkspaceButton"), Padding = new Thickness(22, 0) };
        _save.Clicked += (s, e) => SaveRequested?.Invoke(this, Changes());
        _saveBar = new Grid
        {
            IsVisible = false,
            Padding = new Thickness(40, 12),
            ColumnSpacing = 12,
            BackgroundColor = ThemeResources.Color("Surface"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.08f, Radius = 12, Offset = new Point(0, -2) }
        };
        _saveBar.Add(new Ellipse { WidthRequest = 9, HeightRequest = 9, Fill = ThemeResources.Color("Warning"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        _saveBar.Add(new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center, Children = { _unsavedLabel, _offlineLabel } }, 1, 0);
        _saveBar.Add(discard, 2, 0);
        _saveBar.Add(_save, 3, 0);
        var barLine = new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.Start };

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(page, 0, 0);
        root.Add(new Grid { Children = { _saveBar, barLine } }, 0, 1);
        barLine.SetBinding(IsVisibleProperty, new Binding(nameof(IsVisible), source: _saveBar));
        Content = root;
        _content.Content = ClinicsPage.Card(PageKit.StateMessage("Loading settings…"), new Thickness(0));
    }

    public event EventHandler<string> CategoryChanged;
    public event EventHandler<IReadOnlyDictionary<string, string>> SaveRequested;
    public event EventHandler<string> OverridesRequested;
    public event EventHandler<string> UseDefaultRequested;
    public event EventHandler HistoryFiltersChanged;

    public string Category => _category;
    public bool HasChanges => _rows.Values.Any(r => r.Current != r.Original);

    public bool ClinicMode => _clinicMode;

    // Data ---------------------------------------------------------------------------------------------------------------

    /// <summary>Loads values. <paramref name="clinic"/> holds this clinic's overrides (clinic mode).</summary>
    public void SetData(IReadOnlyList<SystemSetting> global, IReadOnlyList<SystemSetting> clinic, IReadOnlyDictionary<string, int> overrideCounts)
    {
        _global = global.ToDictionary(g => g.SettingKey);
        _clinic = (clinic ?? Array.Empty<SystemSetting>()).ToDictionary(c => c.SettingKey);
        _overrideCounts = overrideCounts?.ToDictionary(k => k.Key, k => k.Value) ?? new();
        _rows.Clear();
        UpdateSaveBar();
        ShowCategory(_category ?? _menuItems.First().Key);
    }

    public void ShowError(string message) => _content.Content = ClinicsPage.Card(PageKit.StateMessage(message, "circle-alert", "DangerSoft"), new Thickness(0));

    /// <summary>Field errors after Save (key → message); the first category with an error opens.</summary>
    public void ShowErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, row) in _rows) SetRowError(row, errors.TryGetValue(key, out var m) ? m : null);
        var first = errors.Keys.Select(SettingCatalog.Get).FirstOrDefault(d => d != null);
        if (first != null && first.Category != _category)
        {
            ShowCategory(first.Category);
            foreach (var (key, row) in _rows) SetRowError(row, errors.TryGetValue(key, out var m) ? m : null);
        }
    }

    public void SetOffline(bool offline)
    {
        _offline = offline;
        _offlineLabel.IsVisible = offline;
        _save.IsEnabled = !offline;
    }

    public void SetBusy(bool busy)
    {
        _save.IsEnabled = !busy && !_offline;
        _save.Text = busy ? "Saving…" : "Save changes";
    }

    /// <summary>Values the user changed (key → stored value).</summary>
    public IReadOnlyDictionary<string, string> Changes() =>
        _rows.Where(r => r.Value.Current != r.Value.Original).ToDictionary(r => r.Key, r => r.Value.Current);

    /// <summary>The value each key would have after saving (for cross-field checks such as open / close).</summary>
    public string ValueAfterSave(string key) => _rows.TryGetValue(key, out var r) ? r.Current : EffectiveValue(key);

    // Menu --------------------------------------------------------------------------------------------------------------

    private void AddMenuItem(string key, string label, string icon)
    {
        var text = new Label { Text = label, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var ic = new LucideIcon { Icon = icon, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center };
        var dot = new Ellipse { WidthRequest = 8, HeightRequest = 8, Fill = ThemeResources.Color("Warning"), VerticalOptions = LayoutOptions.Center, IsVisible = false };
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(ic, 0, 0);
        grid.Add(text, 1, 0);
        grid.Add(dot, 2, 0);
        var item = new Border { HeightRequest = 48, Padding = new Thickness(14, 0), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectCategory(key);
        item.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => { if (_category != key) item.BackgroundColor = ThemeResources.Color("MenuHover"); };
        hover.PointerExited += (s, e) => { if (_category != key) item.BackgroundColor = Colors.Transparent; };
        item.GestureRecognizers.Add(hover);
        HandCursor.Apply(item);
        SemanticProperties.SetDescription(item, label);
        _menuItems.Add((key, item, text, ic, dot));
        _menu.Children.Add(item);

        var chip = PharmacyKit.SmallButton(label, icon, false, () => SelectCategory(key), label);
        chip.StyleId = key;
        _menuStripRow.Children.Add(chip);
    }

    /// <summary>Opens a category (or the Change history) as if it were clicked.</summary>
    public void OpenCategory(string key) => SelectCategory(key);

    private void SelectCategory(string key)
    {
        if (key == _category) return;
        ShowCategory(key);
        CategoryChanged?.Invoke(this, key);
    }

    private void ShowCategory(string key)
    {
        _category = key;
        foreach (var (k, item, text, icon, _) in _menuItems)
        {
            bool on = k == key;
            item.BackgroundColor = on ? ThemeResources.Color("Cyan") : Colors.Transparent;
            text.FontFamily = on ? "DMSansBold" : "DMSansMedium";
            text.TextColor = ThemeResources.Color(on ? "Navy" : "MenuText");
            icon.StrokeColor = ThemeResources.Color(on ? "Navy" : "MenuText");
        }
        foreach (var chip in _menuStripRow.Children.OfType<Border>())
            chip.BackgroundColor = ThemeResources.Color(chip.StyleId == key ? "Cyan" : "Surface");
        if (key == HistoryKey) return;   // the controller fills the history
        _content.Content = CategoryView(key);
    }

    private void Arrange(double width)
    {
        bool narrow = width > 0 && width < 820;
        _layout.Children.Clear();
        _layout.ColumnDefinitions.Clear();
        _menuStrip.IsVisible = narrow;
        if (narrow)
        {
            _layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _layout.Add(_content, 0, 0);
        }
        else
        {
            _layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(MenuWidth)));
            _layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _layout.Add(_menuCard, 0, 0);
            _layout.Add(_content, 1, 0);
        }
    }

    // Category content ------------------------------------------------------------------------------------------------------

    private View CategoryView(string category)
    {
        var info = SettingCatalog.Categories.First(c => c.Key == category);
        var defs = SettingCatalog.All.Where(d => d.Category == category && (!_clinicMode || d.ClinicOverride)).ToList();
        var card = new VerticalStackLayout { Spacing = 0 };
        for (int i = 0; i < defs.Count; i++)
        {
            if (i > 0) card.Children.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor") });
            card.Children.Add(RowView(defs[i]));
        }
        return new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = info.Label, FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy") },
                        new Label { Text = info.Description, FontSize = 15, TextColor = ThemeResources.Color("BodyText") }
                    }
                },
                new Border { Style = ThemeResources.Get<Style>("WorkspaceCard"), Padding = new Thickness(0), Content = card }
            }
        };
    }

    /// <summary>The value this page edits: the clinic's own value (clinic mode) or the global value.</summary>
    private string EffectiveValue(string key) =>
        _clinicMode && _clinic.TryGetValue(key, out var c) ? c.SettingValue
        : _global.TryGetValue(key, out var g) ? g.SettingValue
        : SettingCatalog.Get(key)?.Default;

    /// <summary>"Reset to" target: the seed value (Super Admin) or the VetCare default, i.e. the global value (clinic mode).</summary>
    private string DefaultFor(string key) =>
        _clinicMode ? (_global.TryGetValue(key, out var g) ? g.SettingValue : SettingCatalog.Get(key)?.Default)
        : (_global.TryGetValue(key, out var s) ? s.DefaultValue : SettingCatalog.Get(key)?.Default);

    private View RowView(SettingDefinition def)
    {
        if (!_rows.TryGetValue(def.Key, out var row))
        {
            string value = EffectiveValue(def.Key);
            row = new Row { Def = def, Original = value, Current = value };
            _rows[def.Key] = row;
        }

        // Left: label, tags, description, meta.
        // The tags wrap under the label when there isn't room beside it.
        var titleRow = new WrapLayout { Children = { new Label { Text = def.Label, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 8, 4) } } };
        void Tag(View pill) { pill.Margin = new Thickness(0, 1, 8, 4); titleRow.Children.Add(pill); }
        if (def.ClinicOverride && !_clinicMode) Tag(Badges.Pill("Clinic can override", "Navy", "PaleBlue"));
        if (def.NotEnforcedYet) Tag(Badges.Pill("Not enforced yet", "BodyText", "NeutralSoft"));
        var left = new VerticalStackLayout { Spacing = 3, Children = { titleRow, new Label { Text = def.Description, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.WordWrap } } };
        var source = _clinicMode && _clinic.TryGetValue(def.Key, out var own) ? own : _global.TryGetValue(def.Key, out var g) ? g : null;
        if (_clinicMode)
            left.Children.Add(Meta(_clinic.ContainsKey(def.Key) ? "Your clinic's value" + Changed(source) : "Using the VetCare default (" + SettingCatalog.Display(def.Key, DefaultFor(def.Key)) + ")"));
        else if (source?.UpdatedAt != null)
            left.Children.Add(Meta("Last changed by " + (source.UpdatedByName ?? "Super Admin") + " · " + source.UpdatedAt.Value.ToString("MMM d, yyyy")));
        if (!_clinicMode && def.ClinicOverride && _overrideCounts.TryGetValue(def.Key, out int count) && count > 0)
        {
            var link = new TextLink { Text = count == 1 ? "1 clinic uses a different value" : count + " clinics use a different value", FontFamily = "DMSansBold", FontSize = 13.5, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Start };
            link.Clicked += (s, e) => OverridesRequested?.Invoke(this, def.Key);
            left.Children.Add(link);
        }
        if (_clinicMode && _clinic.ContainsKey(def.Key))
        {
            var useDefault = new TextLink { Text = "Use the VetCare default", FontFamily = "DMSansBold", FontSize = 13.5, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Start };
            useDefault.Clicked += (s, e) => UseDefaultRequested?.Invoke(this, def.Key);
            left.Children.Add(useDefault);
        }

        // Right: input, range, reset link, error.
        row.Error = PharmacyKit.ErrorLabel();
        row.Reset = new TextLink { FontSize = 13, TextColor = ThemeResources.Color("Orange"), HorizontalOptions = LayoutOptions.End };
        row.Reset.Clicked += (s, e) =>
        {
            row.SetInput?.Invoke(DefaultFor(def.Key));
            ValueChanged(row, DefaultFor(def.Key));
        };
        var right = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End, WidthRequest = def.Kind == SettingKind.Options ? 340 : 280, Children = { Input(row) } };
        if (def.RangeText is string range) right.Children.Add(new Label { Text = range, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.End });
        right.Children.Add(row.Reset);
        right.Children.Add(row.Error);

        var grid = new Grid { Padding = new Thickness(20), ColumnSpacing = 20, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(left, 0, 0);
        grid.Add(right, 1, 0);
        bool? narrowNow = null;
        grid.SizeChanged += (s, e) =>
        {
            bool narrow = grid.Width > 0 && grid.Width < 640;
            if (narrow == narrowNow) return;
            narrowNow = narrow;
            Dispatcher.Dispatch(() =>
            {
                if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
                Grid.SetRow(right, narrow ? 1 : 0);
                Grid.SetColumn(right, narrow ? 0 : 1);
                right.HorizontalOptions = narrow ? LayoutOptions.Start : LayoutOptions.End;
            });
        };

        // Unsaved rows get a 3px amber left border.
        row.Frame = new Border { StrokeThickness = 0, Padding = new Thickness(3, 0, 0, 0), Content = new Border { StrokeThickness = 0, BackgroundColor = ThemeResources.Color("Surface"), Content = grid } };
        UpdateRow(row);
        return row.Frame;
    }

    private static string Changed(SystemSetting s) =>
        s?.UpdatedAt == null ? string.Empty : " · changed by " + (s.UpdatedByName ?? "an administrator") + " on " + s.UpdatedAt.Value.ToString("MMM d, yyyy");

    private static Label Meta(string text) => new() { Text = text, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(0, 2, 0, 0) };

    // Inputs ---------------------------------------------------------------------------------------------------------------

    private View Input(Row row)
    {
        var def = row.Def;
        switch (def.Kind)
        {
            case SettingKind.Toggle:
            {
                var toggle = new ToggleSwitch(row.Current == "true" ? "On" : "Off", row.Current == "true", "SuccessDot") { HorizontalOptions = LayoutOptions.End };
                toggle.Toggled += (s, on) => { toggle.Text = on ? "On" : "Off"; ValueChanged(row, on ? "true" : "false"); };
                row.SetInput = v => { toggle.IsOn = v == "true"; toggle.Text = toggle.IsOn ? "On" : "Off"; };
                return toggle;
            }
            case SettingKind.Fixed:
                row.SetInput = _ => { };
                return new Label { Text = SettingCatalog.Display(def.Key, row.Current), FontFamily = "DMSansMedium", FontSize = 15, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.End };
            case SettingKind.Time:
            {
                // Every 15 minutes, shown as "8:00 AM" (WinUI's TimePicker drops AM/PM, so 5:00 could be either).
                var times = Enumerable.Range(0, 24 * 4).Select(i => TimeSpan.FromMinutes(i * 15)).ToList();
                var picker = new Picker { FontFamily = "DMSans", FontSize = 15, TextColor = ThemeResources.Color("Navy"), BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Center };
                foreach (var time in times) picker.Items.Add(DateTime.Today.Add(time).ToString("h:mm tt", CultureInfo.InvariantCulture));
                void Select(string v)
                {
                    if (!TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out var x)) return;
                    int i = times.FindIndex(t => t == x);
                    if (i < 0) { times.Add(x); picker.Items.Add(DateTime.Today.Add(x).ToString("h:mm tt", CultureInfo.InvariantCulture)); i = times.Count - 1; }
                    picker.SelectedIndex = i;
                }
                Select(row.Current);
                picker.SelectedIndexChanged += (s, e) =>
                {
                    if (picker.SelectedIndex >= 0) ValueChanged(row, times[picker.SelectedIndex].ToString(@"hh\:mm", CultureInfo.InvariantCulture));
                };
                row.SetInput = Select;
                return new Border { Style = ThemeResources.Get<Style>("InputBox"), Padding = new Thickness(8, 0), WidthRequest = 180, HorizontalOptions = LayoutOptions.End, Content = picker };
            }
            case SettingKind.Options:
            {
                var segments = new HorizontalStackLayout { Spacing = 0 };
                var buttons = new List<(string Value, Border Button, Label Text)>();
                void Paint(string v)
                {
                    foreach (var (value, b, l) in buttons)
                    {
                        bool on = value == v;
                        b.BackgroundColor = ThemeResources.Color(on ? "Cyan" : "Surface");
                        l.FontFamily = on ? "DMSansBold" : "DMSans";
                    }
                }
                foreach (int option in def.Options)
                {
                    string value = option.ToString(CultureInfo.InvariantCulture);
                    var text = new Label { Text = option + " " + def.Unit, FontSize = 13.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center };
                    var button = new Border { Padding = new Thickness(8, 8), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, Content = text };
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (s, e) => { Paint(value); ValueChanged(row, value); };
                    button.GestureRecognizers.Add(tap);
                    HandCursor.Apply(button);
                    buttons.Add((value, button, text));
                    segments.Children.Add(button);
                }
                Paint(row.Current);
                row.SetInput = Paint;
                return new Border { Padding = new Thickness(4), Stroke = ThemeResources.Color("BorderColor"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 }, HorizontalOptions = LayoutOptions.End, Content = segments };
            }
            default:
            {
                bool money = def.Kind == SettingKind.Money;
                bool number = def.Kind == SettingKind.Number;
                var (box, entry) = PharmacyKit.TextInput(null, number || money ? Keyboard.Numeric : Keyboard.Text, def.MaxLength > 0 ? def.MaxLength : 20, money ? "₱" : null);
                entry.Text = money && decimal.TryParse(row.Current, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? m.ToString("0.00", CultureInfo.InvariantCulture) : row.Current;
                box.WidthRequest = number ? 130 : money ? 180 : 280;
                box.HorizontalOptions = LayoutOptions.End;
                var counter = new Label { FontSize = 12, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.End, IsVisible = def.Key == SettingKeys.FooterMessage };
                var preview = def.Key == SettingKeys.BillNoPrefix ? Badges.Pill(string.Empty, "Navy", "CyanSoft") : null;
                void Extras(string v)
                {
                    counter.Text = (v?.Length ?? 0) + "/" + def.MaxLength;
                    if (preview is Border p && p.Content is Label l) l.Text = (string.IsNullOrWhiteSpace(v) ? "VC" : v.Trim().ToUpperInvariant()) + "-" + DateTime.Today.Year + "-0001";
                }
                entry.TextChanged += (s, e) =>
                {
                    string v = entry.Text ?? string.Empty;
                    Extras(v);
                    ValueChanged(row, money && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x.ToString("0.00", CultureInfo.InvariantCulture)
                        : def.Key == SettingKeys.BillNoPrefix ? v.Trim().ToUpperInvariant() : v.Trim());
                };
                row.SetInput = v => entry.Text = money && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x.ToString("0.00", CultureInfo.InvariantCulture) : v;
                Extras(entry.Text);
                var stack = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End };
                if (number && def.Unit != null)
                {
                    var line = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { box, new Label { Text = def.Unit, FontSize = 14, TextColor = ThemeResources.Color("BodyText"), VerticalOptions = LayoutOptions.Center } } };
                    stack.Children.Add(line);
                }
                else stack.Children.Add(box);
                stack.Children.Add(counter);
                if (preview != null)
                {
                    preview.HorizontalOptions = LayoutOptions.End;
                    stack.Children.Add(new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End, Children = { new Label { Text = "Preview", FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center }, preview } });
                }
                return stack;
            }
        }
    }

    private void ValueChanged(Row row, string value)
    {
        row.Current = value;
        SetRowError(row, null);
        UpdateRow(row);
        UpdateSaveBar();
    }

    private void UpdateRow(Row row)
    {
        if (row.Frame != null) row.Frame.BackgroundColor = row.Current != row.Original ? ThemeResources.Color("Warning") : Colors.Transparent;
        string def = DefaultFor(row.Def.Key);
        bool showReset = row.Def.Kind != SettingKind.Fixed && row.Current != def && !(_clinicMode && !_clinic.ContainsKey(row.Def.Key) && row.Current == row.Original);
        row.Reset.IsVisible = showReset;
        row.Reset.Text = (_clinicMode ? "Back to the default (" : "Reset to default (") + SettingCatalog.Display(row.Def.Key, def) + ")";
    }

    private static void SetRowError(Row row, string message) => PharmacyKit.SetError(row.Error, message, null);

    private void UpdateSaveBar()
    {
        int count = _rows.Values.Count(r => r.Current != r.Original);
        _saveBar.IsVisible = count > 0;
        _unsavedLabel.Text = count == 1 ? "1 unsaved change" : count + " unsaved changes";
        var dirtyCategories = _rows.Values.Where(r => r.Current != r.Original).Select(r => r.Def.Category).ToHashSet();
        foreach (var (key, _, _, _, dot) in _menuItems) dot.IsVisible = dirtyCategories.Contains(key);
    }

    private void Discard()
    {
        foreach (var row in _rows.Values)
        {
            if (row.Current == row.Original) continue;
            row.SetInput?.Invoke(row.Original);
            row.Current = row.Original;
            SetRowError(row, null);
            UpdateRow(row);
        }
        UpdateSaveBar();
    }

    // Change history -----------------------------------------------------------------------------------------------------

    private DateBox _histFrom, _histTo;
    private PickerField _histSetting, _histScope;

    public (DateTime? From, DateTime? To, string Key, string Scope) HistoryFilter =>
        (_histFrom?.Date, _histTo?.Date, _histSetting?.SelectedValue as string, _histScope?.SelectedValue as string);

    public void ShowHistory(IReadOnlyList<SettingChange> changes, IReadOnlyList<Clinic> clinics)
    {
        if (_histFrom == null)
        {
            _histFrom = new DateBox { Placeholder = "From", WidthRequest = 160, MaximumDate = DateTime.Today };
            _histTo = new DateBox { Placeholder = "To", WidthRequest = 160, MaximumDate = DateTime.Today };
            _histSetting = new PickerField("setting", "Setting") { WidthRequest = 230, Margin = new Thickness(0, 0, 12, 8) };
            _histSetting.SetItems(new (string, object)[] { ("All settings", null) }.Concat(SettingCatalog.All.Select(d => (d.Label, (object)d.Key))), null);
            _histSetting.Select(null);
            _histScope = new PickerField("scope", "Scope") { WidthRequest = 220, Margin = new Thickness(0, 0, 12, 8) };
            _histScope.SetItems(new (string, object)[] { ("Global and clinics", null), ("Global only", "global") }
                .Concat(clinics.Select(c => (c.ClinicName, (object)c.ClinicId.ToString(CultureInfo.InvariantCulture)))), null);
            _histScope.Select(null);
            _histFrom.DateChanged += (s, e) => HistoryFiltersChanged?.Invoke(this, EventArgs.Empty);
            _histTo.DateChanged += (s, e) => HistoryFiltersChanged?.Invoke(this, EventArgs.Empty);
            _histSetting.SelectionChanged += (s, e) => HistoryFiltersChanged?.Invoke(this, EventArgs.Empty);
            _histScope.SelectionChanged += (s, e) => HistoryFiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        var filters = new WrapLayout();
        foreach (var (label, view) in new (string, View)[] { ("From", _histFrom), ("To", _histTo) })
        {
            if (view.Parent is Layout old) old.Children.Remove(view);
            filters.Children.Add(new VerticalStackLayout { Margin = new Thickness(0, 0, 12, 8), Children = { PharmacyKit.FieldLabel(label), view } });
        }
        foreach (var picker in new View[] { _histSetting, _histScope })
        {
            if (picker.Parent is Layout old) old.Children.Remove(picker);
            filters.Children.Add(picker);
        }

        var table = new FlexTable(new("Date & time", 120), new("Setting", 170, true), new("Old → new", 170, true), new("Changed by", 110, true), new("Scope", 130));
        if (changes.Count == 0) table.AddMessage(PageKit.EmptyState("history", "No changes yet", "Every saved setting change appears here."));
        foreach (var c in changes)
        {
            var def = SettingCatalog.Get(c.SettingKey);
            var row = table.NewRow(62);
            row.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = c.ChangedAt.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                    new Label { Text = c.ChangedAt.ToString("h:mm tt", CultureInfo.InvariantCulture), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
                }
            }, 0, 0);
            row.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Children =
                {
                    new Label { Text = def?.Label ?? c.SettingKey, FontSize = 14, FontFamily = "DMSansMedium", TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 },
                    new Label { Text = c.SettingKey, FontSize = 12, FontFamily = "Consolas", TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 }
                }
            }, 1, 0);
            var change = new FormattedString();
            change.Spans.Add(new Span { Text = SettingCatalog.Display(c.SettingKey, c.OldValue), TextColor = ThemeResources.Color("Muted"), TextDecorations = TextDecorations.Strikethrough, FontSize = 14 });
            change.Spans.Add(new Span { Text = "  →  ", TextColor = ThemeResources.Color("Muted"), FontSize = 14 });
            change.Spans.Add(new Span { Text = SettingCatalog.Display(c.SettingKey, c.NewValue), TextColor = ThemeResources.Color("Navy"), FontFamily = "DMSansBold", FontSize = 14 });
            row.Add(new Label { FormattedText = change, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(0, 0, 12, 0) }, 2, 0);
            row.Add(PageKit.Cell(c.ChangedByName ?? "—"), 3, 0);
            row.Add(c.ClinicId == null ? Badges.Pill("Global", "Navy", "PaleBlue") : Badges.Pill(c.ClinicName ?? "Clinic", "BodyText", "NeutralSoft"), 4, 0);
            table.AddRow(row);
        }

        _content.Content = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = "Change history", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy") },
                        new Label { Text = "Every saved change: old and new values, who changed it, and where it applies.", FontSize = 15, TextColor = ThemeResources.Color("BodyText") }
                    }
                },
                ClinicsPage.Card(filters, new Thickness(20, 16, 8, 8)),
                ClinicsPage.Card(table, new Thickness(0))
            }
        };
    }
}
