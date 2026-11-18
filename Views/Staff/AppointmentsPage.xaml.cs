using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Staff;

/// <summary>The three ways the Appointments page shows the day.</summary>
public enum AppointmentsView
{
    Queue,
    Calendar,
    List
}

/// <summary>
/// The Appointments page (Cashier/Staff and Administrator): Queue, Calendar (Day / Week) and List
/// views. Displays what the controller gives it and raises events; the rules live in
/// Controllers/AppointmentController.
/// </summary>
public partial class AppointmentsPage : ContentView
{
    private static readonly TableColumn[] Columns =
    {
        new("Date", 120), new("Time", 96), new("Pet", 190), new("Owner", 170), new("Vet", 180),
        new("Reason", 200), new("Status", 150), new("Sync", 120), new("", 60)
    };

    private const double TimeColumnWidth = 84;
    private const double SlotHeight = 56;
    private const double CalendarHeaderHeight = 52;
    private const double VetColumnMinWidth = 200;
    private const double DayColumnMinWidth = 120;
    private const double QueueColumnMinWidth = 218;

    private readonly List<(AppointmentsView View, Border Button, Label Text, LucideIcon Icon)> _viewButtons = new();
    private readonly List<(bool Week, Border Button, Label Text)> _modeButtons = new();
    private IReadOnlyList<User> _vets = Array.Empty<User>();
    private IDispatcherTimer _searchTimer;
    private PagedResult<Appointment> _page;
    private AppointmentsView _view = AppointmentsView.Queue;
    private bool _week;
    private DateTime _date = DateTime.Today;
    private bool _settingDate;
    private bool _canEdit;
    private double _width;

    public AppointmentsPage()
    {
        InitializeComponent();

        VetPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        StatusPicker.Items.Add("All status");
        foreach (string status in AppointmentStatuses.All) StatusPicker.Items.Add(status);
        StatusPicker.SelectedIndex = 0;
        StatusPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);

        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        BookButton.Clicked += (s, e) => BookRequested?.Invoke(this, EventArgs.Empty);
        WalkInButton.Clicked += (s, e) => WalkInRequested?.Invoke(this, EventArgs.Empty);
        TodayButton.Clicked += (s, e) => ChangeDate(DateTime.Today);
        AddTap(PrevDayButton, () => ChangeDate(_date.AddDays(-Step)));
        AddTap(NextDayButton, () => ChangeDate(_date.AddDays(Step)));
        DatePickerOverlay.DateSelected += (s, e) => { if (!_settingDate) ChangeDate(e.NewDate); };
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        AddViewButton(AppointmentsView.Queue, "columns-3", "Queue");
        AddViewButton(AppointmentsView.Calendar, "calendar", "Calendar");
        AddViewButton(AppointmentsView.List, "list", "List");
        AddModeButton(false, "Day");
        AddModeButton(true, "Week");
        UpdateSwitches();
        ShowDate(_date);

        SizeChanged += (s, e) => ApplyWidth(Width);

        ScrollFocus.DontScrollOnFocus(PageScroll, QueueScroll, CalendarScroll, TableScroll);
    }

    public event EventHandler BookRequested;
    public event EventHandler WalkInRequested;
    public event EventHandler<DateTime> DateChanged;
    public event EventHandler FiltersChanged;
    public event EventHandler ViewChanged;
    public event EventHandler<int> PageRequested;

    /// <summary>A card, block or row was clicked: open the details panel.</summary>
    public event EventHandler<Appointment> AppointmentSelected;

    /// <summary>A queue card's action button (appointment, action key).</summary>
    public event EventHandler<(Appointment Appointment, string Action)> ActionRequested;

    /// <summary>A list row's three-dot button.</summary>
    public event EventHandler<(View Anchor, Appointment Appointment)> MenuRequested;

    /// <summary>An empty calendar slot was clicked (vet 0 in Week view = no vet chosen).</summary>
    public event EventHandler<(int VetId, DateTime Date, TimeSpan Time)> SlotRequested;

    public AppointmentsView CurrentView => _view;
    public bool WeekMode => _week;
    public DateTime SelectedDate => _date;

    /// <summary>Monday of the selected date's week.</summary>
    public DateTime WeekStart => _date.AddDays(-(((int)_date.DayOfWeek + 6) % 7)).Date;

    public int? CurrentVetId => VetPicker.SelectedIndex > 0 && VetPicker.SelectedIndex - 1 < _vets.Count ? _vets[VetPicker.SelectedIndex - 1].UserId : null;
    public string CurrentStatus => StatusPicker.SelectedIndex > 0 ? AppointmentStatuses.All[StatusPicker.SelectedIndex - 1] : null;
    public string CurrentSearch => SearchEntry.Text?.Trim();
    public bool IsFiltering => CurrentVetId != null || CurrentStatus != null || !string.IsNullOrWhiteSpace(CurrentSearch);

    private int Step => _view == AppointmentsView.Calendar && _week ? 7 : 1;

    /// <summary>False for the Administrator (view only): no Walk-in / Book buttons.</summary>
    public void SetCanEdit(bool canEdit)
    {
        _canEdit = canEdit;
        HeaderButtons.IsVisible = canEdit;
    }

    public void SetVets(IReadOnlyList<User> vets)
    {
        int? keep = CurrentVetId;
        _vets = vets;
        VetPicker.Items.Clear();
        VetPicker.Items.Add("All vets");
        foreach (var vet in vets) VetPicker.Items.Add("Dr. " + vet.FullName);
        int index = keep == null ? 0 : vets.ToList().FindIndex(v => v.UserId == keep) + 1;
        VetPicker.SelectedIndex = Math.Max(0, index);
    }

    public void ShowDate(DateTime date)
    {
        _date = date.Date;
        DateLabel.Text = _view == AppointmentsView.Calendar && _week
            ? "Week of " + WeekStart.ToString("MMM d") + " – " + WeekStart.AddDays(6).ToString("MMM d, yyyy")
            : _date.ToString("dddd, MMM d, yyyy");
        _settingDate = true;
        DatePickerOverlay.Date = _date;
        _settingDate = false;
    }

    public void ShowSummary(AppointmentDaySummary summary, bool today)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("calendar-days", "Cyan", summary.Total.ToString(), today ? "Today's appointments" : "Appointments this day"),
            PageKit.SummaryCard("clock", "PaleBlue", summary.Waiting.ToString(), "Waiting"),
            PageKit.SummaryCard("stethoscope", "WarningBox", summary.InConsultation.ToString(), "In consultation", "ConsultText"),
            PageKit.SummaryCard("circle-check", "Mint", summary.Completed.ToString(), today ? "Completed today" : "Completed this day", "CompletedText")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading appointments…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    // Queue -------------------------------------------------------------------------------------

    /// <summary>Scheduled / Checked In / In Consultation / Completed columns with a card per visit.</summary>
    public void ShowQueue(IReadOnlyList<Appointment> appointments, Func<Appointment, AppointmentAction> actionFor)
    {
        ShowOnly(QueueScroll);
        QueueGrid.Children.Clear();
        QueueGrid.ColumnDefinitions.Clear();

        var statuses = AppointmentStatuses.Queue;
        for (int i = 0; i < statuses.Count; i++)
        {
            QueueGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var items = appointments.Where(a => a.Status == statuses[i]).ToList();
            QueueGrid.Add(QueueColumn(statuses[i], items, actionFor), i, 0);
        }
        SizeQueue();
    }

    private View QueueColumn(string status, IReadOnlyList<Appointment> items, Func<Appointment, AppointmentAction> actionFor)
    {
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 4, 0, 4) };
        header.Add(new Label { Text = status, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, 0, 0);
        header.Add(Badges.Pill(items.Count.ToString(), "Navy", AppointmentStatuses.ColorsFor(status).Background), 1, 0);

        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(10, 12, 10, 12), Children = { header } };
        if (items.Count == 0)
            stack.Children.Add(new Label { Text = "Nothing here yet", FontSize = 14, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 12) });
        foreach (var a in items)
        {
            var appointment = a;
            stack.Children.Add(AppointmentCards.Card(a, actionFor(a),
                () => AppointmentSelected?.Invoke(this, appointment),
                key => ActionRequested?.Invoke(this, (appointment, key))));
        }

        // 3px top border in the status colour.
        var column = new Grid { RowDefinitions = { new RowDefinition(new GridLength(3)), new RowDefinition(GridLength.Auto) } };
        column.Add(new BoxView { Color = ThemeResources.Color(AppointmentStatuses.AccentFor(status)) }, 0, 0);
        column.Add(stack, 0, 1);

        return new Border
        {
            BackgroundColor = ThemeResources.Color("SidebarBackground"),
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 0,
            VerticalOptions = LayoutOptions.Start,
            Content = column
        };
    }

    // Calendar ------------------------------------------------------------------------------------

    /// <summary>Day view: time rows on the left, one column per vet, blocks coloured by status.</summary>
    public void ShowCalendarDay(IReadOnlyList<User> vets, IReadOnlyList<Appointment> appointments, DateTime date, bool canBook)
    {
        ShowOnly(CalendarCard);
        CalendarTitle.Text = date.ToString("dddd, MMM d");
        if (vets.Count == 0)
        {
            CalendarHost.Content = PageKit.StateMessage("There are no active vets in this clinic.");
            return;
        }

        var slots = TimeSlotHelper.AllSlots();
        var grid = CalendarGrid(vets.Count, slots.Count, VetColumnMinWidth);

        for (int c = 0; c < vets.Count; c++)
        {
            var vet = vets[c];
            grid.Add(HeaderCell(new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    PageKit.Avatar(vet.Initials, 28, 14, 11),
                    new Label { Text = "Dr. " + vet.FullName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 160 }
                }
            }, highlight: false), c + 1, 0);

            for (int r = 0; r < slots.Count; r++)
            {
                var slot = slots[r];
                bool bookable = canBook && !TimeSlotHelper.IsPast(date, slot, DateTime.Now);
                int vetId = vet.UserId;
                grid.Add(SlotCell(bookable ? () => SlotRequested?.Invoke(this, (vetId, date, slot)) : null), c + 1, r + 1);
            }

            foreach (var group in appointments.Where(a => a.VetId == vet.UserId).GroupBy(a => RowOf(a.AppointmentTime, slots.Count)))
                grid.Add(BlocksCell(group.ToList(), compact: false), c + 1, group.Key + 1);
        }

        CalendarHost.Content = WithNowLine(grid, date, vets.Count);
    }

    /// <summary>Week view: seven day columns, blocks show time + pet name.</summary>
    public void ShowCalendarWeek(IReadOnlyList<Appointment> appointments, DateTime weekStart, bool canBook)
    {
        ShowOnly(CalendarCard);
        CalendarTitle.Text = weekStart.ToString("MMM d") + " – " + weekStart.AddDays(6).ToString("MMM d, yyyy");

        var slots = TimeSlotHelper.AllSlots();
        var grid = CalendarGrid(7, slots.Count, DayColumnMinWidth);

        for (int d = 0; d < 7; d++)
        {
            var day = weekStart.AddDays(d);
            bool today = day == DateTime.Today;
            grid.Add(HeaderCell(new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = day.ToString("ddd").ToUpperInvariant(), FontFamily = "DMSansBold", FontSize = 11, TextColor = ThemeResources.Color(today ? "Orange" : "Muted"), HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = day.Day.ToString(), FontFamily = "NunitoExtraBold", FontSize = 18, TextColor = ThemeResources.Color(today ? "Orange" : "Navy"), HorizontalOptions = LayoutOptions.Center }
                }
            }, today), d + 1, 0);

            for (int r = 0; r < slots.Count; r++)
            {
                var slot = slots[r];
                bool bookable = canBook && !TimeSlotHelper.IsPast(day, slot, DateTime.Now);
                grid.Add(SlotCell(bookable ? () => SlotRequested?.Invoke(this, (0, day, slot)) : null), d + 1, r + 1);
            }

            foreach (var group in appointments.Where(a => a.AppointmentDate.Date == day).GroupBy(a => RowOf(a.AppointmentTime, slots.Count)))
                grid.Add(BlocksCell(group.ToList(), compact: true), d + 1, group.Key + 1);
        }

        int todayColumn = (DateTime.Today - weekStart).Days;
        CalendarHost.Content = todayColumn is >= 0 and < 7 ? WithNowLine(grid, DateTime.Today, 7, todayColumn) : grid;
    }

    private Grid CalendarGrid(int columns, int rows, double minColumnWidth)
    {
        double available = Math.Max(0, (_width > 0 ? _width : 1000) - 80 - 2);
        var grid = new Grid
        {
            ColumnSpacing = 0,
            RowSpacing = 0,
            WidthRequest = Math.Max(available, TimeColumnWidth + columns * minColumnWidth)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(TimeColumnWidth)));
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(CalendarHeaderHeight)));
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition(new GridLength(SlotHeight)));

        grid.Add(HeaderCell(new ContentView(), highlight: false), 0, 0);
        var slots = TimeSlotHelper.AllSlots();
        for (int r = 0; r < slots.Count; r++)
            grid.Add(new Grid
            {
                Children =
                {
                    new Label { Text = TimeSlotHelper.Format(slots[r]), FontSize = 12, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4, 10, 0) },
                    new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End }
                }
            }, 0, r + 1);
        return grid;
    }

    private static View HeaderCell(View content, bool highlight) => new Grid
    {
        BackgroundColor = ThemeResources.Color(highlight ? "TabActiveFill" : "InputBackground"),
        Children =
        {
            content,
            new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End },
            new BoxView { WidthRequest = 1, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Start }
        }
    };

    /// <summary>An empty half-hour cell; bookable ones highlight on hover and open "Book appointment".</summary>
    private static View SlotCell(Action onBook)
    {
        var cell = new Grid
        {
            Children =
            {
                new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End },
                new BoxView { WidthRequest = 1, Color = ThemeResources.Color("BorderColor"), HorizontalOptions = LayoutOptions.Start }
            }
        };
        if (onBook == null)
        {
            cell.BackgroundColor = ThemeResources.Color("PageBackground");
            return cell;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => onBook();
        cell.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => cell.BackgroundColor = ThemeResources.Color("RowHover");
        hover.PointerExited += (s, e) => cell.BackgroundColor = Colors.Transparent;
        cell.GestureRecognizers.Add(hover);
        HandCursor.Apply(cell);
        ToolTipProperties.SetText(cell, "Book this time");
        return cell;
    }

    /// <summary>The appointments starting in one half-hour cell, stacked.</summary>
    private View BlocksCell(IReadOnlyList<Appointment> items, bool compact)
    {
        var stack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(4, 3, 4, 3) };
        double height = (SlotHeight - 6 - (items.Count - 1) * 2) / items.Count;
        foreach (var a in items) stack.Children.Add(Block(a, compact || items.Count > 1, height));
        return stack;
    }

    /// <summary>A rounded block in the status colour: time, pet name and (when there's room) reason.</summary>
    private View Block(Appointment a, bool compact, double height)
    {
        var (background, text) = AppointmentStatuses.ColorsFor(a.Status);
        var time = new Span
        {
            Text = a.TimeText + "  ",
            FontFamily = "DMSansBold",
            TextDecorations = a.Status == AppointmentStatuses.Cancelled ? TextDecorations.Strikethrough : TextDecorations.None
        };
        var content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    FontSize = 12,
                    TextColor = ThemeResources.Color(text),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    FormattedText = new FormattedString { Spans = { time, new Span { Text = a.PetName, FontFamily = "DMSansBold" } } }
                }
            }
        };
        if (!compact)
            content.Children.Add(new Label { Text = a.Reason, FontSize = 11.5, TextColor = ThemeResources.Color(text), LineBreakMode = LineBreakMode.TailTruncation });

        var block = new Border
        {
            HeightRequest = Math.Max(18, height),
            Padding = new Thickness(8, 0),
            BackgroundColor = ThemeResources.Color(background),
            Stroke = ThemeResources.Color(AppointmentStatuses.AccentFor(a.Status)),
            StrokeThickness = a.SyncStatus == SyncStatuses.Conflict ? 0 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = content
        };
        if (a.SyncStatus == SyncStatuses.Conflict)
        {
            block.Stroke = ThemeResources.Color("Danger");
            block.StrokeThickness = 1.5;
            ToolTipProperties.SetText(block, "Double booking — please reschedule");
        }
        else ToolTipProperties.SetText(block, a.TimeText + " · " + a.PetName + " · " + a.Reason + " · " + a.Status);

        AddTap(block, () => AppointmentSelected?.Invoke(this, a));
        return block;
    }

    /// <summary>A red line across the day at the current time (today only, within clinic hours).</summary>
    private static View WithNowLine(Grid grid, DateTime date, int columns, int column = -1)
    {
        var now = DateTime.Now;
        if (date.Date != now.Date || now.TimeOfDay < TimeSlotHelper.Opens || now.TimeOfDay >= TimeSlotHelper.Closes) return grid;

        double y = CalendarHeaderHeight + (now.TimeOfDay - TimeSlotHelper.Opens).TotalMinutes / TimeSlotHelper.SlotMinutes * SlotHeight;
        var line = new BoxView
        {
            HeightRequest = 2,
            Color = ThemeResources.Color("Danger"),
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Margin = new Thickness(0, y, 0, 0)
        };
        var dot = new Ellipse
        {
            WidthRequest = 8,
            HeightRequest = 8,
            Fill = ThemeResources.Color("Danger"),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Margin = new Thickness(-4, y - 3, 0, 0)
        };
        int first = column >= 0 ? column + 1 : 1;
        int span = column >= 0 ? 1 : columns;
        grid.Add(line, first, 0);
        Grid.SetRowSpan(line, grid.RowDefinitions.Count);
        Grid.SetColumnSpan(line, span);
        grid.Add(dot, first, 0);
        Grid.SetRowSpan(dot, grid.RowDefinitions.Count);
        return grid;
    }

    /// <summary>The half-hour row an appointment starts in (times outside clinic hours go to the first / last row).</summary>
    private static int RowOf(TimeSpan time, int rows)
    {
        int row = (int)Math.Floor((time - TimeSlotHelper.Opens).TotalMinutes / TimeSlotHelper.SlotMinutes);
        return Math.Clamp(row, 0, rows - 1);
    }

    // List -----------------------------------------------------------------------------------------

    public void ShowList(PagedResult<Appointment> page)
    {
        _page = page;
        ShowOnly(TableCard);
        TableStack.Children.Clear();

        if (page.TotalCount == 0)
        {
            TableScroll.IsVisible = false;
            PagerRow.IsVisible = false;
            TableStateHost.Content = IsFiltering
                ? PageKit.StateMessage("No appointments match your filters.")
                : PageKit.EmptyState("calendar-days", "No appointments yet", "Book the first visit to get started.",
                    _canEdit ? "Book appointment" : null, () => BookRequested?.Invoke(this, EventArgs.Empty));
            TableStateHost.IsVisible = true;
            return;
        }

        TableStateHost.IsVisible = false;
        TableScroll.IsVisible = true;
        PagerRow.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(Columns));
        foreach (var a in page.Items) TableStack.Children.Add(Row(a));

        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "appointment" : "appointments")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    private View Row(Appointment a)
    {
        var grid = PageKit.RowGrid(Columns, 60);
        grid.Add(PageKit.Cell(a.AppointmentDate.ToString("MMM d, yyyy")), 0, 0);
        grid.Add(AppointmentCards.TimeLabel(a, 14), 1, 0);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                AppointmentCards.PetAvatar(a, 32),
                AppointmentCards.WithGuestPill(a, new Label { Text = a.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = a.IsGuest ? 80 : 130 })
            }
        }, 2, 0);
        grid.Add(PageKit.Cell(a.OwnerName), 3, 0);
        grid.Add(PageKit.Cell(a.VetName), 4, 0);
        grid.Add(PageKit.Cell(a.Reason, muted: true), 5, 0);
        grid.Add(Badges.AppointmentStatus(a.Status), 6, 0);
        var sync = Badges.Sync(a.SyncStatus);
        if (a.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Double booking — please reschedule");
        grid.Add(sync, 7, 0);

        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + a.PetName + " at " + a.TimeText,
            () => MenuRequested?.Invoke(this, (menuButton, a)));
        grid.Add(menuButton, 8, 0);

        var open = new TapGestureRecognizer();
        open.Tapped += (s, e) =>
        {
            if (e.GetPosition(grid) is Point p && p.X > grid.Width - Columns[^1].Width - 20) return; // actions column
            AppointmentSelected?.Invoke(this, a);
        };
        grid.GestureRecognizers.Add(open);
        HandCursor.Apply(grid);
        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    // View switches --------------------------------------------------------------------------------

    private void AddViewButton(AppointmentsView view, string icon, string text)
    {
        var iconView = new LucideIcon { Icon = icon, WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
        var label = new Label { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        var button = new Border
        {
            Padding = new Thickness(12, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { iconView, label } }
        };
        AddTap(button, () =>
        {
            if (_view == view) return;
            _view = view;
            UpdateSwitches();
            ShowDate(_date);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });
        _viewButtons.Add((view, button, label, iconView));
        ViewSwitchRow.Children.Add(button);
    }

    private void AddModeButton(bool week, string text)
    {
        var label = new Label { Text = text, FontSize = 13, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        var button = new Border
        {
            HeightRequest = 30,
            Padding = new Thickness(12, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = label
        };
        AddTap(button, () =>
        {
            if (_week == week) return;
            _week = week;
            UpdateSwitches();
            ShowDate(_date);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        });
        _modeButtons.Add((week, button, label));
        CalendarModeRow.Children.Add(button);
    }

    /// <summary>The active option has a cyan background and bold navy text. The date controls hide in List view.</summary>
    private void UpdateSwitches()
    {
        foreach (var (view, button, text, icon) in _viewButtons)
        {
            bool active = view == _view;
            button.BackgroundColor = active ? ThemeResources.Color("Cyan") : Colors.Transparent;
            text.FontFamily = active ? "DMSansBold" : "DMSans";
            text.TextColor = ThemeResources.Color(active ? "Navy" : "BodyText");
            icon.StrokeColor = ThemeResources.Color(active ? "Navy" : "Muted");
        }
        foreach (var (week, button, text) in _modeButtons)
        {
            bool active = week == _week;
            button.BackgroundColor = active ? ThemeResources.Color("Cyan") : Colors.Transparent;
            text.FontFamily = active ? "DMSansBold" : "DMSans";
            text.TextColor = ThemeResources.Color(active ? "Navy" : "BodyText");
        }
        DateNav.IsVisible = _view != AppointmentsView.List;
        AllDatesLabel.IsVisible = _view == AppointmentsView.List;
    }

    // Helpers ----------------------------------------------------------------------------------------

    private void ChangeDate(DateTime date)
    {
        ShowDate(date);
        DateChanged?.Invoke(this, _date);
    }

    private void ShowOnly(View visible)
    {
        StateCard.IsVisible = false;
        QueueScroll.IsVisible = visible == QueueScroll;
        CalendarCard.IsVisible = visible == CalendarCard;
        TableCard.IsVisible = visible == TableCard;
    }

    private void ShowState(View content)
    {
        QueueScroll.IsVisible = false;
        CalendarCard.IsVisible = false;
        TableCard.IsVisible = false;
        StateHost.Content = content;
        StateCard.IsVisible = true;
    }

    private static void AddTap(View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => action();
        view.GestureRecognizers.Add(tap);
        HandCursor.Apply(view);
    }

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        return timer;
    }

    // Responsive -------------------------------------------------------------------------------------

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 4 : 2;

    /// <summary>The queue keeps 4 columns of at least 218px; narrower windows scroll it sideways.</summary>
    private void SizeQueue()
    {
        double available = Math.Max(0, (_width > 0 ? _width : 1000) - 80);
        QueueGrid.WidthRequest = Math.Max(available, 4 * QueueColumnMinWidth + 3 * 12);
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _width) < 1) return;
        int oldSummary = SummaryColumns();
        bool oldWide = _width <= 0 || _width >= 900;
        _width = width;

        if (SummaryColumns() != oldSummary) PageKit.Reflow(SummaryGrid, SummaryColumns());
        if (QueueGrid.Children.Count > 0) SizeQueue();
        if (CalendarHost.Content is Grid calendar)
        {
            double available = Math.Max(0, width - 80 - 2);
            int columns = calendar.ColumnDefinitions.Count - 1;
            double min = TimeColumnWidth + columns * (_week ? DayColumnMinWidth : VetColumnMinWidth);
            calendar.WidthRequest = Math.Max(available, min);
        }

        bool wide = width >= 900;
        if (wide == oldWide) return;

        // Wide: date controls and view switch on one row, filters on one row. Narrow: stacked.
        ToolbarTop.ColumnDefinitions = wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
        ToolbarTop.RowDefinitions = wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        Grid.SetColumn(ViewSwitch, wide ? 1 : 0);
        Grid.SetRow(ViewSwitch, wide ? 0 : 1);

        FilterGrid.ColumnDefinitions = wide
            ? new ColumnDefinitionCollection(new ColumnDefinition(new GridLength(200)), new ColumnDefinition(new GridLength(180)), new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
        FilterGrid.RowDefinitions = wide
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        Grid.SetColumn(SearchBox, wide ? 2 : 0);
        Grid.SetRow(SearchBox, wide ? 0 : 1);
        Grid.SetColumnSpan(SearchBox, wide ? 1 : 2);
    }
}
