using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Views.Veterinarian;

/// <summary>
/// The medical records list: summary cards, filters and the records table. Displays what the
/// controller gives it and raises events; the rules live in Controllers/MedicalRecordController.
/// </summary>
public partial class MedicalRecordsPage : ContentView
{
    private static readonly TableColumn[] Columns =
    {
        new("Date", 130), new("Pet", 210), new("Owner", 170), new("Vet", 180), new("Diagnosis", 240),
        new("Rx", 70), new("Vaccines", 90), new("Sync", 120), new("", 60)
    };

    private IReadOnlyList<User> _vets = Array.Empty<User>();
    private IDispatcherTimer _searchTimer;
    private PagedResult<MedicalRecord> _page;
    private double _width;

    public MedicalRecordsPage()
    {
        InitializeComponent();

        VetPicker.SelectedIndexChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        FromDate.DateChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        ToDate.DateChanged += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        ClearDatesButton.Clicked += (s, e) =>
        {
            FromDate.Date = null;
            ToDate.Date = null;
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        };
        MineSwitch.Toggled += (s, e) =>
        {
            VetBox.IsEnabled = !e.Value;
            VetBox.Opacity = e.Value ? 0.6 : 1;
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        };
        SearchEntry.TextChanged += (s, e) =>
        {
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        PrevButton.Clicked += (s, e) => { if (_page?.HasPrevious == true) PageRequested?.Invoke(this, _page.Page - 1); };
        NextButton.Clicked += (s, e) => { if (_page?.HasNext == true) PageRequested?.Invoke(this, _page.Page + 1); };

        ScrollFocus.DontScrollOnFocus(PageScroll, TableScroll);
        SizeChanged += (s, e) =>
        {
            if (Width <= 0 || Math.Abs(Width - _width) < 1) return;
            bool wasWide = _width <= 0 || _width >= 980;
            _width = Width;
            if ((_width >= 980) != wasWide) PageKit.Reflow(SummaryGrid, SummaryColumns());
        };
    }

    public event EventHandler FiltersChanged;
    public event EventHandler<int> PageRequested;
    public event EventHandler<MedicalRecord> RecordSelected;
    public event EventHandler<(View Anchor, MedicalRecord Record)> MenuRequested;

    public string CurrentSearch => SearchEntry.Text?.Trim();
    public DateTime? CurrentFrom => FromDate.Date;
    public DateTime? CurrentTo => ToDate.Date;
    public bool MyRecordsOnly => MineRow.IsVisible && MineSwitch.IsToggled;

    public int? CurrentVetId => VetPicker.SelectedIndex > 0 && VetPicker.SelectedIndex - 1 < _vets.Count ? _vets[VetPicker.SelectedIndex - 1].UserId : null;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(CurrentSearch) || CurrentVetId != null || CurrentFrom != null || CurrentTo != null || MyRecordsOnly;

    /// <summary>Vets get the "My records only" toggle.</summary>
    public void SetIsVet(bool isVet) => MineRow.IsVisible = isVet;

    public void SetVets(IReadOnlyList<User> vets)
    {
        _vets = vets;
        VetPicker.Items.Clear();
        VetPicker.Items.Add("All vets");
        foreach (var vet in vets) VetPicker.Items.Add("Dr. " + vet.FullName);
        VetPicker.SelectedIndex = 0;
    }

    public void ShowSummary(MedicalRecordSummary s)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("clipboard-plus", "Cyan", s.Today.ToString(), "Records today"),
            PageKit.SummaryCard("calendar-range", "PaleBlue", s.ThisWeek.ToString(), "Records this week"),
            PageKit.SummaryCard("stethoscope", "WarningBox", s.OngoingConsultations.ToString(), "Ongoing consultations", "ConsultText"),
            PageKit.SummaryCard("pill", "OrangeSoft", s.PrescriptionsNotDispensed.ToString(), "Prescriptions not dispensed", "Orange")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, SummaryColumns());
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading medical records…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    public void ShowRecords(PagedResult<MedicalRecord> page)
    {
        _page = page;
        TableStack.Children.Clear();
        if (page.TotalCount == 0)
        {
            ShowState(IsFiltering
                ? PageKit.StateMessage("No records match your filters.")
                : PageKit.EmptyState("clipboard-plus", "No medical records yet", "Records appear here after a consultation."));
            return;
        }

        StateHost.IsVisible = false;
        TableScroll.IsVisible = true;
        PagerRow.IsVisible = true;
        TableStack.Children.Add(PageKit.HeaderRow(Columns));
        foreach (var r in page.Items) TableStack.Children.Add(Row(r));
        PagerLabel.Text = $"Showing {page.From}–{page.To} of {page.TotalCount} {(page.TotalCount == 1 ? "record" : "records")}";
        PrevButton.IsEnabled = page.HasPrevious;
        NextButton.IsEnabled = page.HasNext;
        Dispatcher.Dispatch(async () => await TableScroll.ScrollToAsync(0, 0, false));
    }

    private View Row(MedicalRecord r)
    {
        var grid = PageKit.RowGrid(Columns, 64);
        grid.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = r.RecordDate.ToString("MMM d, yyyy"), FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = r.RecordDate.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture), FontSize = 12.5, TextColor = ThemeResources.Color("Muted") }
            }
        }, 0, 0);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                PetsPage.SpeciesAvatar(r.Species, 40),
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = r.PetName, FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150 },
                        new Label { Text = r.SpeciesAndBreed, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 150 }
                    }
                }
            }
        }, 1, 0);
        grid.Add(PageKit.Cell(r.OwnerName), 2, 0);
        grid.Add(PageKit.Cell(r.VetName), 3, 0);
        grid.Add(PageKit.Cell(r.Diagnosis, muted: true), 4, 0);
        grid.Add(CountCell("pill", r.PrescriptionCount), 5, 0);
        grid.Add(CountCell("syringe", r.VaccinationCount), 6, 0);
        var sync = Badges.Sync(r.SyncStatus);
        if (r.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Two versions found — review needed");
        grid.Add(sync, 7, 0);

        Border menuButton = null;
        menuButton = PageKit.IconButton("ellipsis-vertical", "Actions for " + r.PetName + "'s record",
            () => MenuRequested?.Invoke(this, (menuButton, r)));
        grid.Add(menuButton, 8, 0);

        var open = new TapGestureRecognizer();
        open.Tapped += (s, e) =>
        {
            if (e.GetPosition(grid) is Point p && p.X > grid.Width - Columns[^1].Width - 20) return;
            RecordSelected?.Invoke(this, r);
        };
        grid.GestureRecognizers.Add(open);
        HandCursor.Apply(grid);
        PageKit.AddRowHover(grid);
        return PageKit.WithBottomLine(grid);
    }

    private static View CountCell(string icon, int count) => new HorizontalStackLayout
    {
        Spacing = 6,
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color(count > 0 ? "NavyMuted" : "Placeholder"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
            new Label { Text = count.ToString(), FontSize = 14, TextColor = ThemeResources.Color(count > 0 ? "Navy" : "Placeholder"), VerticalOptions = LayoutOptions.Center }
        }
    };

    private void ShowState(View content)
    {
        TableStack.Children.Clear();
        TableScroll.IsVisible = false;
        PagerRow.IsVisible = false;
        StateHost.Content = content;
        StateHost.IsVisible = true;
    }

    private int SummaryColumns() => _width <= 0 || _width >= 980 ? 4 : 2;

    private IDispatcherTimer CreateSearchTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => FiltersChanged?.Invoke(this, EventArgs.Empty);
        return timer;
    }
}
