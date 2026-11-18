using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Views.Veterinarian;

/// <summary>
/// The Vaccinations page: summary cards, the "Due &amp; overdue" follow-up list and "All records".
/// Displays what the controller gives it and raises events; the rules live in VaccinationController.
/// </summary>
public partial class VaccinationPage : ContentView
{
    // Owner + contact no., vaccine + dose, and due date + days share a column each, so the
    // "Book" button stays visible at the normal window size.
    private static readonly TableColumn[] DueColumns =
    {
        new("Pet", 170), new("Owner · contact no.", 165), new("Vaccine · dose", 160),
        new("Due", 130), new("Status", 110), new("", 115)
    };

    // Vaccine + dose and next due + status share a column; the sync badge shows under the date
    // only when the record isn't synced, so the table fits the normal window size.
    private static readonly TableColumn[] RecordColumns =
    {
        new("Date given", 115), new("Pet", 175), new("Owner", 145), new("Vaccine · dose", 170),
        new("Given by", 155), new("Next due", 135), new("", 55)
    };

    private static readonly (DueFilter Filter, string Text)[] Chips =
    {
        (DueFilter.All, "All"), (DueFilter.Overdue, "Overdue"), (DueFilter.Due7Days, "Due in 7 days"), (DueFilter.Due30Days, "Due in " + VaccineScheduleHelper.DueSoonDays + " days")
    };

    private readonly List<(Label Label, BoxView Line, Border Count)> _tabs = new();
    private readonly List<(DueFilter Filter, Border Chip, Label Text)> _chips = new();
    private IReadOnlyList<Medicine> _vaccines = Array.Empty<Medicine>();
    private IReadOnlyList<User> _vets = Array.Empty<User>();
    private IDispatcherTimer _searchTimer;
    private PagedResult<RecordVaccination> _page;
    private DueFilter _filter = DueFilter.All;
    private bool _canBook;
    private bool _silent;
    private double _width;

    public VaccinationPage()
    {
        InitializeComponent();

        AddTab("Due & overdue", 0, withCount: true);
        AddTab("All records", 1, withCount: false);
        SelectTab(0, raise: false);

        foreach (var (filter, text) in Chips) AddChip(filter, text);
        UpdateChips();

        SpeciesPicker.Items.Add("All species");
        foreach (string species in PetSpecies.All) SpeciesPicker.Items.Add(species);
        SpeciesPicker.SelectedIndex = 0;
        SpeciesPicker.SelectedIndexChanged += (s, e) => RaiseDue();
        DueVaccinePicker.SelectedIndexChanged += (s, e) => RaiseDue();

        RecordVaccinePicker.SelectedIndexChanged += (s, e) => RaiseRecords();
        VetPicker.SelectedIndexChanged += (s, e) => RaiseRecords();
        FromDate.DateChanged += (s, e) => RaiseRecords();
        ToDate.DateChanged += (s, e) => RaiseRecords();
        ClearDatesButton.Clicked += (s, e) => { _silent = true; FromDate.Date = null; ToDate.Date = null; _silent = false; RaiseRecords(); };
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };
        PrintDueButton.Clicked += (s, e) => PrintDueListRequested?.Invoke(this, EventArgs.Empty);

        ScrollFocus.DontScrollOnFocus(PageScroll, DueScroll, RecordsScroll);
        SizeChanged += (s, e) =>
        {
            if (Width <= 0 || Math.Abs(Width - _width) < 1) return;
            bool wasWide = _width <= 0 || _width >= 980;
            _width = Width;
            if ((_width >= 980) != wasWide) PageKit.Reflow(SummaryGrid, SummaryColumns());
        };
    }

    public event EventHandler DueFiltersChanged;
    public event EventHandler RecordFiltersChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<int> TabChanged;
    public event EventHandler PrintDueListRequested;

    /// <summary>"Book" on a due row.</summary>
    public event EventHandler<RecordVaccination> BookRequested;

    /// <summary>A due row or a record row was clicked: open the pet's vaccination card.</summary>
    public event EventHandler<RecordVaccination> CardRequested;

    /// <summary>A row's three-dot button (anchor, vaccination, true on the due list).</summary>
    public event EventHandler<(View Anchor, RecordVaccination Vaccination, bool Due)> MenuRequested;

    public int CurrentTab { get; private set; }
    public DueFilter CurrentFilter => _filter;
    public string CurrentSpecies => SpeciesPicker.SelectedIndex > 0 ? PetSpecies.All[SpeciesPicker.SelectedIndex - 1] : null;
    public int? CurrentDueVaccineId => Pick(DueVaccinePicker, _vaccines, v => v.MedicineId);
    public int? CurrentRecordVaccineId => Pick(RecordVaccinePicker, _vaccines, v => v.MedicineId);
    public int? CurrentVetId => Pick(VetPicker, _vets, v => v.UserId);
    public string CurrentSearch => SearchEntry.Text?.Trim();
    public DateTime? CurrentFrom => FromDate.Date;
    public DateTime? CurrentTo => ToDate.Date;

    /// <summary>"Overdue · Dog" — what the due list shows (for the printed list).</summary>
    public string DueFilterText =>
        Chips.First(c => c.Filter == _filter).Text + (CurrentSpecies != null ? " · " + CurrentSpecies : string.Empty) +
        (CurrentDueVaccineId is int id ? " · " + _vaccines.First(v => v.MedicineId == id).MedicineName : string.Empty);

    public void SetCanBook(bool canBook) => _canBook = canBook;

    public void SetOptions(IReadOnlyList<Medicine> vaccines, IReadOnlyList<User> vets)
    {
        _silent = true;
        _vaccines = vaccines;
        _vets = vets;
        Fill(DueVaccinePicker, "All vaccines", vaccines.Select(v => v.MedicineName));
        Fill(RecordVaccinePicker, "All vaccines", vaccines.Select(v => v.MedicineName));
        Fill(VetPicker, "All vets", vets.Select(v => "Dr. " + v.FullName));
        _silent = false;
    }

    /// <summary>Applies a due-list filter (from a summary card) and switches to the due tab.</summary>
    public void ApplyFilter(DueFilter filter)
    {
        _filter = filter;
        UpdateChips();
        if (CurrentTab != 0) SelectTab(0, raise: true);
        else DueFiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowSummary(VaccinationSummary s)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("syringe", "Cyan", s.GivenThisMonth.ToString(), "Given this month"),
            Clickable(PageKit.SummaryCard("clock", "WarningBox", s.DueIn30Days.ToString(), "Due in " + VaccineScheduleHelper.DueSoonDays + " days", "ConsultText"), DueFilter.Due30Days),
            Clickable(PageKit.SummaryCard("circle-alert", "OrangeSoft", s.Overdue.ToString(), "Overdue", "Orange"), DueFilter.Overdue),
            PageKit.SummaryCard("shield-check", "Mint", s.PetsUpToDate.ToString(), "Pets up to date", "CompletedText")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());

        var (_, _, count) = _tabs[0];
        ((Label)count.Content).Text = (s.Overdue + s.DueIn30Days).ToString();
        count.IsVisible = s.Overdue + s.DueIn30Days > 0;
    }

    public void ShowLoading() => ShowDueState(PageKit.StateMessage("Loading vaccinations…"));

    public void ShowError(string message)
    {
        ShowDueState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
        ShowRecordsState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));
    }

    // Due & overdue --------------------------------------------------------------------------------

    public void ShowDueList(IReadOnlyList<RecordVaccination> due)
    {
        DueStack.Children.Clear();
        if (due.Count == 0)
        {
            bool filtered = _filter != DueFilter.All || CurrentSpecies != null || CurrentDueVaccineId != null;
            var empty = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(24, 44), HorizontalOptions = LayoutOptions.Center };
            empty.Children.Add(new IconBadge { Icon = "shield-check", Size = 56, IconSize = 26, CircleColor = ThemeResources.Color("Mint"), IconColor = ThemeResources.Color("CompletedText"), HorizontalOptions = LayoutOptions.Center });
            empty.Children.Add(new Label { Text = filtered ? "Nothing matches these filters" : "All caught up!", FontFamily = "DMSansBold", FontSize = 18, TextColor = ThemeResources.Color("Navy"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 4, 0, 0) });
            empty.Children.Add(new Label { Text = filtered ? "Try another filter." : "No pets are due or overdue right now.", FontSize = 14, TextColor = ThemeResources.Color("BodyText"), HorizontalOptions = LayoutOptions.Center });
            ShowDueState(empty);
            return;
        }

        DueState.IsVisible = false;
        DueScroll.IsVisible = true;
        DueStack.Children.Add(PageKit.HeaderRow(DueColumns));
        var today = DateTime.Today;
        foreach (var v in due) DueStack.Children.Add(DueRow(v, today));
        Dispatcher.Dispatch(async () => await DueScroll.ScrollToAsync(0, 0, false));
    }

    private View DueRow(RecordVaccination v, DateTime today)
    {
        var grid = PageKit.RowGrid(DueColumns, 64);
        var status = VaccineScheduleHelper.StatusOf(v.NextDueDate, today);
        bool overdue = status == VaccineStatus.Overdue;

        grid.Add(PetCell(v), 0, 0);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = v.OwnerName, FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150, HorizontalOptions = LayoutOptions.Start },
                ContactCell(v.OwnerContactDisplay, v.OwnerContactNo)
            }
        }, 1, 0);
        grid.Add(TwoLines(v.VaccineName, VaccinationPrintDocument.NextDoseText(v)), 2, 0);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = v.NextDueDate?.ToString("MMM d, yyyy") ?? "—", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                new Label
                {
                    Text = VaccineScheduleHelper.DaysText(v.NextDueDate ?? today, today),
                    FontFamily = overdue ? "DMSansBold" : "DMSansMedium",
                    FontSize = 12.5,
                    TextColor = ThemeResources.Color(overdue ? "Orange" : "ConsultText")
                }
            }
        }, 3, 0);
        grid.Add(Badges.VaccineStatus(status), 4, 0);

        var actions = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (_canBook) actions.Children.Add(BookButton(v));
        Border menu = null;
        menu = PageKit.IconButton("ellipsis-vertical", "More for " + v.PetName, () => MenuRequested?.Invoke(this, (menu, v, true)));
        actions.Children.Add(menu);
        grid.Add(actions, 5, 0);

        AddRowTap(grid, DueColumns, () => CardRequested?.Invoke(this, v));
        return PageKit.WithBottomLine(grid);
    }

    /// <summary>Small orange "Book" button with a calendar-plus icon.</summary>
    private View BookButton(RecordVaccination v)
    {
        var button = new Border
        {
            HeightRequest = 34,
            Padding = new Thickness(12, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = ThemeResources.Color("Orange"),
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = "calendar-plus", StrokeColor = Colors.White, WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Book", FontFamily = "DMSansBold", FontSize = 13.5, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => BookRequested?.Invoke(this, v);
        button.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => button.BackgroundColor = ThemeResources.Color("OrangeHover");
        hover.PointerExited += (s, e) => button.BackgroundColor = ThemeResources.Color("Orange");
        button.GestureRecognizers.Add(hover);
        HandCursor.Apply(button);
        SemanticProperties.SetDescription(button, "Book a vaccination visit for " + v.PetName);
        return button;
    }

    /// <summary>The number with a small copy button; the icon turns into a check for a moment after copying.</summary>
    private static View ContactCell(string display, string digits)
    {
        var icon = new LucideIcon { Icon = "copy", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var copy = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            Content = icon
        };
        ToolTipProperties.SetText(copy, "Copy contact no.");
        SemanticProperties.SetDescription(copy, "Copy contact number");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (s, e) =>
        {
            await Clipboard.Default.SetTextAsync(digits);
            icon.Icon = "check";
            icon.StrokeColor = ThemeResources.Color("Success");
            await Task.Delay(1500);
            icon.Icon = "copy";
            icon.StrokeColor = ThemeResources.Color("Muted");
        };
        copy.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => copy.BackgroundColor = ThemeResources.Color("Cyan");
        hover.PointerExited += (s, e) => copy.BackgroundColor = Colors.Transparent;
        copy.GestureRecognizers.Add(hover);
        HandCursor.Apply(copy);

        return new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = display, FontSize = 14, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }, copy }
        };
    }

    // All records -------------------------------------------------------------------------------------

    public void ShowRecords(PagedResult<RecordVaccination> page)
    {
        _page = page;
        RecordsStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            bool filtered = !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentRecordVaccineId != null || CurrentVetId != null || CurrentFrom != null || CurrentTo != null;
            ShowRecordsState(filtered
                ? PageKit.StateMessage("No vaccinations match your filters.")
                : PageKit.EmptyState("syringe", "No vaccinations yet", "Vaccines appear here after the vet records them during a consultation."));
            return;
        }

        RecordsState.IsVisible = false;
        RecordsScroll.IsVisible = true;
        PagerRow.IsVisible = true;
        RecordsStack.Children.Add(PageKit.HeaderRow(RecordColumns));
        var today = DateTime.Today;
        foreach (var v in page.Items) RecordsStack.Children.Add(RecordRow(v, today));
        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "record" : "records")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await RecordsScroll.ScrollToAsync(0, 0, false));
    }

    private View RecordRow(RecordVaccination v, DateTime today)
    {
        var grid = PageKit.RowGrid(RecordColumns, 68);
        var date = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = v.DateGiven.ToString("MMM d, yyyy"), FontSize = 14, TextColor = ThemeResources.Color("Navy") } }
        };
        if (v.SyncStatus != SyncStatuses.Synced)
        {
            var sync = Badges.Sync(v.SyncStatus);
            if (v.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Two versions found — review needed");
            date.Children.Add(sync);
        }
        grid.Add(date, 0, 0);
        grid.Add(PetCell(v), 1, 0);
        grid.Add(PageKit.Cell(v.OwnerName), 2, 0);
        grid.Add(TwoLines(v.VaccineName, v.DoseText), 3, 0);
        grid.Add(PageKit.Cell(v.GivenByName), 4, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = v.NextDueDate?.ToString("MMM d, yyyy") ?? "—", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                Badges.VaccineStatus(VaccineScheduleHelper.StatusOf(v.NextDueDate, today))
            }
        }, 5, 0);
        Border menu = null;
        menu = PageKit.IconButton("ellipsis-vertical", "Actions for " + v.PetName + "'s vaccination", () => MenuRequested?.Invoke(this, (menu, v, false)));
        grid.Add(menu, 6, 0);

        AddRowTap(grid, RecordColumns, () => CardRequested?.Invoke(this, v));
        return PageKit.WithBottomLine(grid);
    }

    // Building blocks -------------------------------------------------------------------------------

    private static View TwoLines(string first, string second) => new VerticalStackLayout
    {
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = first, FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 148, HorizontalOptions = LayoutOptions.Start },
            new Label { Text = second, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), HorizontalOptions = LayoutOptions.Start }
        }
    };

    private static View PetCell(RecordVaccination v) => new HorizontalStackLayout
    {
        Spacing = 10,
        Children =
        {
            PetsPage.SpeciesAvatar(v.Species, 40),
            new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = v.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 115, HorizontalOptions = LayoutOptions.Start },
                    new Label { Text = v.SpeciesAndBreed, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 140 }
                }
            }
        }
    };

    private static void AddRowTap(Grid grid, TableColumn[] columns, Action open)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (e.GetPosition(grid) is Point p && p.X > grid.Width - columns[^1].Width - 20) return; // actions column
            open();
        };
        grid.GestureRecognizers.Add(tap);
        HandCursor.Apply(grid);
        PageKit.AddRowHover(grid);
    }

    private View Clickable(View card, DueFilter filter)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => ApplyFilter(filter);
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        ToolTipProperties.SetText(card, "Show these pets");
        return card;
    }

    private void AddTab(string text, int index, bool withCount)
    {
        var label = new Label { Text = text, FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var count = new Border
        {
            IsVisible = false,
            Padding = new Thickness(7, 1),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color("Orange"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label { FontFamily = "DMSansBold", FontSize = 11.5, TextColor = Colors.White }
        };
        var line = new BoxView { HeightRequest = 2, CornerRadius = 1, VerticalOptions = LayoutOptions.End };
        var content = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { label } };
        if (withCount) content.Children.Add(count);
        var tab = new Grid { HeightRequest = 40, Children = { content, line } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => SelectTab(index, raise: true);
        tab.GestureRecognizers.Add(tap);
        HandCursor.Apply(tab);
        _tabs.Add((label, line, count));
        TabsRow.Children.Add(tab);
    }

    private void SelectTab(int index, bool raise)
    {
        bool changed = CurrentTab != index;
        CurrentTab = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool active = i == index;
            _tabs[i].Label.FontFamily = active ? "DMSansBold" : "DMSansMedium";
            _tabs[i].Label.TextColor = ThemeResources.Color(active ? "Orange" : "BodyText");
            _tabs[i].Line.Color = active ? ThemeResources.Color("Orange") : Colors.Transparent;
        }
        DueSection.IsVisible = index == 0;
        RecordsSection.IsVisible = index == 1;
        PrintDueButton.IsVisible = index == 0;
        if (raise && changed) TabChanged?.Invoke(this, index);
        else if (raise) DueFiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddChip(DueFilter filter, string text)
    {
        var label = new Label { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        var chip = new Border
        {
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 0, 8, 0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = label
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (_filter == filter) return;
            _filter = filter;
            UpdateChips();
            DueFiltersChanged?.Invoke(this, EventArgs.Empty);
        };
        chip.GestureRecognizers.Add(tap);
        HandCursor.Apply(chip);
        _chips.Add((filter, chip, label));
        ChipsRow.Children.Add(chip);
    }

    /// <summary>Selected chip = cyan background, bold navy text.</summary>
    private void UpdateChips()
    {
        foreach (var (filter, chip, text) in _chips)
        {
            bool active = filter == _filter;
            chip.BackgroundColor = ThemeResources.Color(active ? "Cyan" : "Surface");
            chip.Stroke = ThemeResources.Color(active ? "Cyan" : "BorderColor");
            text.FontFamily = active ? "DMSansBold" : "DMSans";
            text.TextColor = ThemeResources.Color(active ? "Navy" : "BodyText");
        }
    }

    private void ShowDueState(View content)
    {
        DueStack.Children.Clear();
        DueScroll.IsVisible = false;
        DueState.Content = content;
        DueState.IsVisible = true;
    }

    private void ShowRecordsState(View content)
    {
        RecordsStack.Children.Clear();
        RecordsScroll.IsVisible = false;
        PagerRow.IsVisible = false;
        RecordsState.Content = content;
        RecordsState.IsVisible = true;
    }

    private void RaiseDue() { if (!_silent) DueFiltersChanged?.Invoke(this, EventArgs.Empty); }

    private void RaiseRecords() { if (!_silent) RecordFiltersChanged?.Invoke(this, EventArgs.Empty); }

    private static void Fill(Picker picker, string all, IEnumerable<string> items)
    {
        picker.Items.Clear();
        picker.Items.Add(all);
        foreach (string item in items) picker.Items.Add(item);
        picker.SelectedIndex = 0;
    }

    private static int? Pick<T>(Picker picker, IReadOnlyList<T> items, Func<T, int> id) =>
        picker.SelectedIndex > 0 && picker.SelectedIndex - 1 < items.Count ? id(items[picker.SelectedIndex - 1]) : null;

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 4 : 2;

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => RaiseRecords();
        return timer;
    }
}
