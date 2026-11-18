using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;

namespace VetCare.Views.Veterinarian;

/// <summary>
/// My schedule: the signed-in vet's own appointments for one day, in time order. Each card has
/// "Start consultation" once the pet is checked in, and "Complete" during the consultation.
/// </summary>
public partial class VetSchedulePage : ContentView
{
    private DateTime _date = DateTime.Today;
    private bool _settingDate;
    private double _width;
    private IReadOnlyList<View> _cards = Array.Empty<View>();

    public VetSchedulePage()
    {
        InitializeComponent();

        TodayButton.Clicked += (s, e) => ChangeDate(DateTime.Today);
        AddTap(PrevDayButton, () => ChangeDate(_date.AddDays(-1)));
        AddTap(NextDayButton, () => ChangeDate(_date.AddDays(1)));
        DatePickerOverlay.DateSelected += (s, e) => { if (!_settingDate) ChangeDate(e.NewDate); };
        ShowDate(_date);

        SizeChanged += (s, e) =>
        {
            if (Width <= 0 || Math.Abs(Width - _width) < 1) return;
            int old = CardColumns();
            _width = Width;
            if (CardColumns() != old && _cards.Count > 0) PageKit.PlaceInGrid(CardsGrid, _cards, CardColumns());
        };

        ScrollFocus.DontScrollOnFocus(PageScroll);
    }

    public event EventHandler<DateTime> DateChanged;
    public event EventHandler<Appointment> AppointmentSelected;
    public event EventHandler<(Appointment Appointment, string Action)> ActionRequested;

    public DateTime SelectedDate => _date;

    public void ShowDate(DateTime date)
    {
        _date = date.Date;
        DateLabel.Text = _date.ToString("dddd, MMM d, yyyy");
        _settingDate = true;
        DatePickerOverlay.Date = _date;
        _settingDate = false;
    }

    public void ShowSummary(AppointmentDaySummary summary, bool today)
    {
        var cards = new[]
        {
            PageKit.SummaryCard("calendar-days", "Cyan", summary.Total.ToString(), today ? "My visits today" : "My visits this day"),
            PageKit.SummaryCard("clock", "PaleBlue", summary.Waiting.ToString(), "Waiting"),
            PageKit.SummaryCard("stethoscope", "WarningBox", summary.InConsultation.ToString(), "In consultation", "ConsultText"),
            PageKit.SummaryCard("circle-check", "Mint", summary.Completed.ToString(), "Completed", "CompletedText")
        };
        PageKit.PlaceInGrid(SummaryGrid, cards, _width <= 0 || _width >= 980 ? 4 : 2);
    }

    public void ShowLoading() => ShowState(PageKit.StateMessage("Loading your schedule…"));

    public void ShowError(string message) => ShowState(PageKit.StateMessage(message, "circle-alert", "DangerSoft"));

    public void ShowAppointments(IReadOnlyList<Appointment> appointments, Func<Appointment, AppointmentAction> actionFor)
    {
        if (appointments.Count == 0)
        {
            ShowState(PageKit.EmptyState("calendar-days", "No visits on this day", "Appointments booked for you will appear here."));
            return;
        }

        StateCard.IsVisible = false;
        CardsGrid.IsVisible = true;
        _cards = appointments.Select(a => AppointmentCards.Card(a, actionFor(a),
            () => AppointmentSelected?.Invoke(this, a),
            key => ActionRequested?.Invoke(this, (a, key)),
            showVet: false)).ToList();
        PageKit.PlaceInGrid(CardsGrid, _cards, CardColumns());
    }

    private int CardColumns() => _width <= 0 || _width >= 1000 ? 3 : _width >= 680 ? 2 : 1;

    private void ShowState(View content)
    {
        _cards = Array.Empty<View>();
        CardsGrid.Children.Clear();
        CardsGrid.IsVisible = false;
        StateHost.Content = content;
        StateCard.IsVisible = true;
    }

    private void ChangeDate(DateTime date)
    {
        ShowDate(date);
        DateChanged?.Invoke(this, _date);
    }

    private static void AddTap(View view, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => action();
        view.GestureRecognizers.Add(tap);
        HandCursor.Apply(view);
    }
}
