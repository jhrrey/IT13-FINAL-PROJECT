using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>A time-slot button's state in the slot grid.</summary>
public enum SlotState
{
    Available,

    /// <summary>The vet already has an active appointment in this slot (rule 4).</summary>
    Taken,

    /// <summary>The slot has already started (rule 5).</summary>
    Past
}

/// <summary>
/// The Book / Edit / Walk-in / Reschedule appointment panel. Reports what was picked; the
/// controller answers with the owner's pets (<see cref="ShowPets"/>), the vets (<see cref="ShowVets"/>)
/// and the vet's free slots (<see cref="ShowSlots"/>), and validates on save. No rules in here.
/// </summary>
public partial class AppointmentFormView : ContentView
{
    public const double PanelWidth = 480;

    private readonly AppointmentFormData _initial;
    private readonly bool _timeLocked;

    private readonly OwnerPickerField _owner;
    private readonly PickerField _pet;
    private readonly PickerField _vet;
    private readonly VerticalStackLayout _dateSection;
    private readonly DateBox _date;
    private readonly Label _dateError;
    private readonly VerticalStackLayout _timeSection;
    private readonly Label _slotCaption;
    private readonly Grid _slotGrid;
    private readonly Label _timeError;
    private readonly VerticalStackLayout _reasonSection;
    private readonly WrapLayout _chips;
    private readonly Editor _details;
    private readonly Border _detailsBox;
    private readonly Label _reasonError;

    // Client type and guest / new-client fields
    private readonly Grid _clientSwitch;
    private readonly (Border Button, Label Text) _guestTab, _returningTab;
    private readonly VerticalStackLayout _guestSection, _guestContactSection, _newClientSection;
    private readonly Border _guestOwnerBox, _guestContactBox, _guestPetBox, _match;
    private readonly Entry _guestOwner, _guestContact, _guestPet;
    private readonly PickerField _guestSpecies;
    private readonly Label _guestOwnerError, _guestContactError, _guestPetError;
    private readonly OwnerFieldsView _newOwner;
    private readonly PetFieldsView _newPet;
    private bool _isGuest;

    private TimeSpan? _time;
    private string _chip;

    /// <param name="initial">Starting values (owner / pet / vet / date / time / reason as known).</param>
    /// <param name="initialOwner">The owner to show as already picked, or null.</param>
    /// <param name="timeLocked">Editing a checked-in visit: date and time stay as they are.</param>
    public AppointmentFormView(AppointmentFormData initial, Owner initialOwner, bool timeLocked = false)
    {
        InitializeComponent();
        ScrollFocus.DontScrollOnFocusWhileOpening(FormScroll);
        _initial = initial;
        _timeLocked = timeLocked;
        var mode = initial.Mode;

        TitleLabel.Text = mode switch
        {
            AppointmentFormMode.Edit => "Edit appointment",
            AppointmentFormMode.WalkIn => "Walk-in",
            AppointmentFormMode.Reschedule => "Reschedule appointment",
            _ => "Book appointment"
        };
        SaveButton.Text = SaveText();

        if (mode == AppointmentFormMode.WalkIn)
        {
            WalkInBar.IsVisible = true;
            WalkInLabel.Text = "Walk-in · Today, " + TimeSlotHelper.Format(TimeSlotHelper.NowToTheMinute(DateTime.Now));
        }

        // Owner and pet (not when only rescheduling) -----------------------------------------
        _owner = new OwnerPickerField();
        _owner.SearchRequested += (s, text) => { ShowMessage(null); OwnerSearchRequested?.Invoke(this, text); };
        _owner.OwnerChanged += (s, owner) =>
        {
            ShowMessage(null);
            _pet.SetItems(Array.Empty<(string, object)>());
            _pet.IsLocked = owner == null;
            if (owner != null) PetsRequested?.Invoke(this, owner.OwnerId);
        };

        _pet = new PickerField(AppointmentFormData.PetField, "Pet *") { IsLocked = true };
        _pet.SelectionChanged += (s, e) => ShowMessage(null);

        // Vet -----------------------------------------------------------------------------------
        _vet = new PickerField(AppointmentFormData.VetField, "Vet *");
        _vet.SelectionChanged += (s, e) =>
        {
            ShowMessage(null);
            UpdateSlotCaption();
            RequestSlots();
        };

        // Date ----------------------------------------------------------------------------------
        _date = new DateBox { MinimumDate = DateTime.Today, MaximumDate = DateTime.Today.AddYears(1) };
        _date.DateChanged += (s, e) =>
        {
            SetError(_dateError, null, _date.Box);
            ShowMessage(null);
            RequestSlots();
        };
        _dateError = ErrorLabel();
        _dateSection = new VerticalStackLayout { Children = { FieldLabel("Date *"), _date, _dateError } };

        // Time slots ------------------------------------------------------------------------------
        _slotCaption = new Label { FontSize = 14, TextColor = ThemeResources.Color("BodyText"), Margin = new Thickness(0, -2, 0, 10) };
        _slotGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (int i = 0; i < 4; i++) _slotGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _timeError = ErrorLabel();
        _timeSection = new VerticalStackLayout { Children = { FieldLabel("Time *"), _slotCaption, _slotGrid, _timeError } };

        // Reason ---------------------------------------------------------------------------------
        _chips = new WrapLayout();
        foreach (string chip in AppointmentFormData.QuickReasons) _chips.Children.Add(ReasonChip(chip));
        _details = new Editor
        {
            FontFamily = "DMSans",
            FontSize = 15,
            TextColor = ThemeResources.Color("Navy"),
            PlaceholderColor = ThemeResources.Color("Placeholder"),
            BackgroundColor = Colors.Transparent,
            Placeholder = "Add details (optional)",
            HeightRequest = 72,
            AutoSize = EditorAutoSizeOption.Disabled
        };
        _details.TextChanged += (s, e) =>
        {
            SetError(_reasonError, null, null);
            ShowMessage(null);
            UpdateDetailsLimit();
        };
        _detailsBox = new Border { Style = ThemeResources.Get<Style>("InputBox"), HeightRequest = 88, Padding = new Thickness(12, 6), Content = _details, Margin = new Thickness(0, 4, 0, 0) };
        _reasonError = ErrorLabel();
        _reasonSection = new VerticalStackLayout { Children = { FieldLabel("Reason *"), _chips, _detailsBox, _reasonError } };

        // Client type: New client (guest) | Returning client -------------------------------------------
        bool walkIn = mode == AppointmentFormMode.WalkIn;
        _clientSwitch = new Grid { ColumnSpacing = 0, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        _guestTab = SwitchOption(walkIn ? "New client" : "New client (guest)", () => SelectClient(true));
        _returningTab = SwitchOption("Returning client", () => SelectClient(false));
        _clientSwitch.Add(_guestTab.Button, 0, 0);
        _clientSwitch.Add(_returningTab.Button, 1, 0);

        _match = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ThemeResources.Color("CyanSoft")
        };

        // Guest booking: only these four are kept, on the appointment.
        (_guestOwnerBox, _guestOwner) = PharmacyKit.TextInput("e.g. Pedro Garcia", maxLength: 100);
        (_guestContactBox, _guestContact) = PharmacyKit.TextInput("09XX XXX XXXX", Keyboard.Telephone, 13);
        (_guestPetBox, _guestPet) = PharmacyKit.TextInput("e.g. Bruno", maxLength: 50);
        _guestSpecies = new PickerField(AppointmentFormData.GuestSpeciesField, "Species *");
        _guestSpecies.SetItems(PetSpecies.All.Select(s => (s, (object)s)), PetSpecies.All.Contains(initial.GuestSpecies) ? initial.GuestSpecies : null);
        _guestOwnerError = ErrorLabel();
        _guestContactError = ErrorLabel();
        _guestPetError = ErrorLabel();
        _guestOwner.Text = initial.GuestOwnerName;
        _guestContact.Text = initial.GuestContactNo;
        _guestPet.Text = initial.GuestPetName;
        _guestOwner.TextChanged += (s, e) => { SetError(_guestOwnerError, null, _guestOwnerBox); ShowMessage(null); };
        _guestContact.TextChanged += (s, e) => { SetError(_guestContactError, null, _guestContactBox); ShowMessage(null); LookUpContact(_guestContact.Text); };
        _guestPet.TextChanged += (s, e) => { SetError(_guestPetError, null, _guestPetBox); ShowMessage(null); };
        _guestSpecies.SelectionChanged += (s, e) => _guestSpecies.SetError(null);
        var petRow = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        petRow.Add(new VerticalStackLayout { Children = { FieldLabel("Pet name *"), _guestPetBox, _guestPetError } }, 0, 0);
        petRow.Add(_guestSpecies, 1, 0);
        _guestContactSection = new VerticalStackLayout { Children = { FieldLabel("Contact no. *"), _guestContactBox, _guestContactError } };
        _guestSection = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                new VerticalStackLayout { Children = { FieldLabel("Owner name *"), _guestOwnerBox, _guestOwnerError } },
                _guestContactSection,
                new VerticalStackLayout
                {
                    Children =
                    {
                        petRow,
                        new Label { Text = "The owner and pet will be registered when they arrive.", FontSize = 13, TextColor = ThemeResources.Color("Muted"), Margin = new Thickness(0, 8, 0, 0) }
                    }
                }
            }
        };

        // Walk-in, new client: they're already here, so the full Add owner and Add pet fields.
        if (walkIn)
        {
            _newOwner = new OwnerFieldsView();
            _newOwner.ContactChanged += (s, text) => { ShowMessage(null); LookUpContact(text); };
            _newPet = new PetFieldsView();
            _newClientSection = new VerticalStackLayout
            {
                Spacing = 18,
                Children = { SectionTitle("user", "Owner"), _newOwner, SectionTitle("paw-print", "Pet"), _newPet }
            };
        }

        // Which sections this mode shows ----------------------------------------------------------
        bool reschedule = mode == AppointmentFormMode.Reschedule;
        bool showTime = mode != AppointmentFormMode.WalkIn && !timeLocked;
        bool showSwitch = mode is AppointmentFormMode.Book or AppointmentFormMode.WalkIn || (mode == AppointmentFormMode.Edit && initial.IsGuest);
        if (!reschedule)
        {
            if (showSwitch) FieldsStack.Add(_clientSwitch);
            FieldsStack.Add(walkIn ? _newClientSection : _guestSection);
            FieldsStack.Add(_owner);
            FieldsStack.Add(_pet);
            FieldsStack.Add(_vet);
        }
        if (showTime)
        {
            FieldsStack.Add(_dateSection);
            FieldsStack.Add(_timeSection);
        }
        if (!reschedule) FieldsStack.Add(_reasonSection);

        // Initial values ---------------------------------------------------------------------------
        SelectChip(initial.ReasonChip);
        _details.Text = initial.ReasonDetails;
        _date.Date = initial.Date;
        _time = initial.Time;
        if (initialOwner != null) _owner.Select(initialOwner);
        UpdateSlotCaption();
        if (!reschedule)
            SelectClient(mode switch
            {
                AppointmentFormMode.Edit => initial.IsGuest,
                _ => initialOwner == null && initial.PetId == 0   // opened from a pet / owner profile = returning
            });

        // Buttons -----------------------------------------------------------------------------------
        SaveButton.Clicked += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
        CancelButton.Clicked += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.GestureRecognizers.Add(closeTap);
        var closeHover = new PointerGestureRecognizer();
        closeHover.PointerEntered += (s, e) => CloseButton.BackgroundColor = ThemeResources.Color("InputBackground");
        closeHover.PointerExited += (s, e) => CloseButton.BackgroundColor = Colors.Transparent;
        CloseButton.GestureRecognizers.Add(closeHover);
        HandCursor.Apply(CloseButton);
    }

    public event EventHandler SaveRequested;
    public event EventHandler CancelRequested;

    /// <summary>Owner search text: answer with <see cref="ShowOwnerResults"/>.</summary>
    public event EventHandler<string> OwnerSearchRequested;

    /// <summary>An owner was picked (argument: OwnerID): answer with <see cref="ShowPets"/>.</summary>
    public event EventHandler<int> PetsRequested;

    /// <summary>The vet or date changed: answer with <see cref="ShowSlots"/>.</summary>
    public event EventHandler<(int VetId, DateTime Date)> SlotsRequested;

    /// <summary>The contact no. typed for a new client: answer with <see cref="ShowContactMatch"/>.</summary>
    public event EventHandler<string> ContactLookupRequested;

    public AppointmentFormMode Mode => _initial.Mode;

    /// <summary>"This number belongs to Juan Dela Cruz (registered). Use Returning client?" with a Switch link (null hides it).</summary>
    public void ShowContactMatch(Owner owner)
    {
        if (owner == null || !_isGuest) { _match.IsVisible = false; return; }
        var link = new TextLink { Text = _initial.Mode == AppointmentFormMode.WalkIn ? "Use this owner" : "Switch", FontSize = 14, VerticalOptions = LayoutOptions.Center };
        link.Clicked += (s, e) =>
        {
            SelectClient(false);
            _owner.Select(owner);
        };
        _match.Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "This number belongs to " + owner.FullName + " (registered). Use Returning client?", FontSize = 14, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.WordWrap },
                link
            }
        };
        var host = _initial.Mode == AppointmentFormMode.WalkIn ? _newOwner?.AfterContact : _guestContactSection;
        if (_match.Parent is Layout old && old != host) old.Remove(_match);
        if (host != null && _match.Parent == null) host.Add(_match);
        _match.IsVisible = true;
    }

    public AppointmentFormData Read() => new()
    {
        Mode = _initial.Mode,
        AppointmentId = _initial.AppointmentId,
        IsGuest = _initial.Mode == AppointmentFormMode.Reschedule ? _initial.IsGuest : _isGuest,
        GuestOwnerName = _guestOwner.Text,
        GuestContactNo = _guestContact.Text,
        GuestPetName = _guestPet.Text,
        GuestSpecies = _guestSpecies.SelectedValue as string,
        NewOwner = _newOwner?.Read(),
        NewPet = _newPet?.Read(),
        OwnerId = _initial.Mode == AppointmentFormMode.Reschedule ? _initial.OwnerId : _owner.SelectedOwner?.OwnerId ?? 0,
        PetId = _initial.Mode == AppointmentFormMode.Reschedule ? _initial.PetId : _pet.SelectedValue is int pet ? pet : 0,
        VetId = _initial.Mode == AppointmentFormMode.Reschedule ? _initial.VetId : _vet.SelectedValue is int vet ? vet : 0,
        Date = _initial.Mode == AppointmentFormMode.WalkIn || _timeLocked ? _initial.Date : _date.Date,
        Time = _initial.Mode == AppointmentFormMode.WalkIn || _timeLocked ? _initial.Time : _time,
        ReasonChip = _initial.Mode == AppointmentFormMode.Reschedule ? null : _chip,
        ReasonDetails = _initial.Mode == AppointmentFormMode.Reschedule ? null : _details.Text,
    };

    public void ShowOwnerResults(IReadOnlyList<Owner> owners) => _owner.ShowResults(owners);

    public void ShowOwnerSearchError(string message) => _owner.ShowSearchError(message);

    /// <summary>The owner's active pets ("Bantay · Dog"); keeps <paramref name="selectedPetId"/> selected.</summary>
    public void ShowPets(IReadOnlyList<Pet> pets, int selectedPetId)
    {
        _pet.SetItems(pets.Select(p => (p.PetName + " · " + p.Species, (object)p.PetId)), selectedPetId == 0 ? null : selectedPetId);
        _pet.IsLocked = false;
        if (pets.Count == 0) _pet.SetError("This owner has no active pets. Add a pet first.");
    }

    /// <summary>The clinic's active vets; walk-ins get "Next available (Dr. …)" first.</summary>
    public void ShowVets(IReadOnlyList<User> vets, int selectedVetId, User nextAvailable)
    {
        var items = new List<(string, object)>();
        if (_initial.Mode == AppointmentFormMode.WalkIn && nextAvailable != null)
            items.Add(("Next available (Dr. " + nextAvailable.FullName + ")", (object)AppointmentFormData.NextAvailableVet));
        items.AddRange(vets.Select(v => ("Dr. " + v.FullName, (object)v.UserId)));

        object selected = selectedVetId != 0 ? selectedVetId
            : _initial.Mode == AppointmentFormMode.WalkIn && nextAvailable != null ? AppointmentFormData.NextAvailableVet : null;
        _vet.SetItems(items, selected);
        if (vets.Count == 0) _vet.SetError("There are no active vets in this clinic.");
        UpdateSlotCaption();
        RequestSlots();
    }

    /// <summary>One button per slot, 4 per row. A picked time that's no longer available is cleared.</summary>
    public void ShowSlots(IReadOnlyList<(TimeSpan Time, SlotState State)> slots)
    {
        _slots = slots;
        if (_time is TimeSpan picked && !slots.Any(s => s.Time == picked && s.State == SlotState.Available))
            _time = null;
        RenderSlots();

        if (slots.Count > 0 && slots.All(s => s.State != SlotState.Available))
            _slotCaption.Text = "No free slots left on this day. Pick another date or vet.";
        else UpdateSlotCaption();
    }

    private IReadOnlyList<(TimeSpan Time, SlotState State)> _slots = Array.Empty<(TimeSpan, SlotState)>();

    private void RenderSlots()
    {
        _slotGrid.Children.Clear();
        _slotGrid.RowDefinitions.Clear();
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i % 4 == 0) _slotGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _slotGrid.Add(SlotButton(_slots[i].Time, _slots[i].State), i % 4, i / 4);
        }
    }

    /// <summary>Clears the slot grid (no vet or date picked yet).</summary>
    public void ClearSlots(string caption)
    {
        _slots = Array.Empty<(TimeSpan, SlotState)>();
        RenderSlots();
        _slotCaption.Text = caption;
    }

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        string Error(string field) => errors.TryGetValue(field, out var message) ? message : null;

        _owner.SetError(Error(AppointmentFormData.OwnerField));
        _pet.SetError(Error(AppointmentFormData.PetField));
        _vet.SetError(Error(AppointmentFormData.VetField));
        SetError(_dateError, Error(AppointmentFormData.DateField), _date.Box);
        SetError(_timeError, Error(AppointmentFormData.TimeField), null);
        SetError(_reasonError, Error(AppointmentFormData.ReasonField), _detailsBox);
        SetError(_guestOwnerError, Error(AppointmentFormData.GuestOwnerField), _guestOwnerBox);
        SetError(_guestContactError, Error(AppointmentFormData.GuestContactField), _guestContactBox);
        SetError(_guestPetError, Error(AppointmentFormData.GuestPetField), _guestPetBox);
        _guestSpecies.SetError(Error(AppointmentFormData.GuestSpeciesField));
        _newOwner?.ShowErrors(Prefixed(errors, AppointmentFormData.NewOwnerPrefix));
        _newPet?.ShowErrors(Prefixed(errors, AppointmentFormData.NewPetPrefix));
        ShowMessage(result.Message);
    }

    private static Dictionary<string, string> Prefixed(IDictionary<string, string> errors, string prefix) =>
        errors.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(e => e.Key.Substring(prefix.Length), e => e.Value);

    public void SetBusy(bool busy)
    {
        SaveButton.IsEnabled = !busy;
        SaveButton.Text = busy ? "Saving…" : SaveText();
    }

    /// <summary>Asks for the slots again (used once the panel is open in Reschedule mode).</summary>
    public void RefreshSlots() => RequestSlots();

    /// <summary>Loads the pets of the owner shown when the panel opened.</summary>
    public int InitialOwnerId => _owner.SelectedOwner?.OwnerId ?? 0;

    // Slots -------------------------------------------------------------------------------------

    private void RequestSlots()
    {
        if (_initial.Mode == AppointmentFormMode.WalkIn || _timeLocked) return;
        int vetId = _initial.Mode == AppointmentFormMode.Reschedule ? _initial.VetId : _vet.SelectedValue is int v ? v : 0;
        if (vetId <= 0) { ClearSlots("Pick a vet to see the available times."); return; }
        if (_date.Date is not DateTime date) { ClearSlots("Pick a date to see the available times."); return; }
        SlotsRequested?.Invoke(this, (vetId, date));
    }

    private void UpdateSlotCaption()
    {
        string vetName = _initial.Mode == AppointmentFormMode.Reschedule
            ? _initial.VetName
            : _vet.SelectedValue is int v && v > 0 ? _vet.SelectedText : null;
        _slotCaption.Text = vetName == null ? "Pick a vet to see the available times." : "Available slots for " + vetName;
    }

    /// <summary>Available = white with border; selected = orange, white text; taken = gray, struck through; past = gray.</summary>
    private View SlotButton(TimeSpan time, SlotState state)
    {
        bool selected = state == SlotState.Available && _time == time;
        var label = new Label
        {
            Text = TimeSlotHelper.Format(time),
            FontFamily = selected ? "DMSansBold" : "DMSansMedium",
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = selected ? Colors.White : ThemeResources.Color(state == SlotState.Available ? "Navy" : "Placeholder"),
            TextDecorations = state == SlotState.Taken ? TextDecorations.Strikethrough : TextDecorations.None
        };
        var button = new Border
        {
            HeightRequest = 40,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            StrokeThickness = state == SlotState.Available && !selected ? 1 : 0,
            Stroke = ThemeResources.Color("BorderColor"),
            BackgroundColor = selected ? ThemeResources.Color("Orange")
                : state == SlotState.Available ? ThemeResources.Color("Surface") : ThemeResources.Color("LightGray"),
            Content = label
        };
        SemanticProperties.SetDescription(button, TimeSlotHelper.Format(time) + (state == SlotState.Taken ? ", taken" : state == SlotState.Past ? ", past" : string.Empty));

        if (state == SlotState.Available)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                _time = time;
                SetError(_timeError, null, null);
                ShowMessage(null);
                RenderSlots(); // redraw with the new selection (no new query needed)
            };
            button.GestureRecognizers.Add(tap);
            HandCursor.Apply(button);
        }
        return button;
    }

    // Reason --------------------------------------------------------------------------------------

    private View ReasonChip(string text)
    {
        var label = new Label { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        var chip = new Border
        {
            Padding = new Thickness(12, 7),
            Margin = new Thickness(0, 0, 8, 8),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = label,
            ClassId = text
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            SelectChip(_chip == text ? null : text);
            SetError(_reasonError, null, _detailsBox);
            ShowMessage(null);
        };
        chip.GestureRecognizers.Add(tap);
        HandCursor.Apply(chip);
        return chip;
    }

    /// <summary>Selected chip = cyan background, bold navy text.</summary>
    private void SelectChip(string chip)
    {
        _chip = chip;
        foreach (var border in _chips.Children.OfType<Border>())
        {
            bool active = border.ClassId == chip;
            border.BackgroundColor = ThemeResources.Color(active ? "Cyan" : "Surface");
            border.Stroke = ThemeResources.Color(active ? "Cyan" : "BorderColor");
            var label = (Label)border.Content;
            label.FontFamily = active ? "DMSansBold" : "DMSans";
            label.TextColor = ThemeResources.Color(active ? "Navy" : "BodyText");
        }
        _details.Placeholder = chip == "Other" ? "Describe the reason" : "Add details (optional)";
        UpdateDetailsLimit();
    }

    /// <summary>The stored reason ("Vaccination: …") must fit the 100-character column.</summary>
    private void UpdateDetailsLimit()
    {
        int prefix = string.IsNullOrEmpty(_chip) || _chip == "Other" ? 0 : _chip.Length + 2;
        _details.MaxLength = AppointmentFormData.ReasonMaxLength - prefix;
    }

    // Building blocks -----------------------------------------------------------------------------

    // Client type -------------------------------------------------------------------------------

    private void SelectClient(bool guest)
    {
        _isGuest = guest;
        PaintOption(_guestTab, guest);
        PaintOption(_returningTab, !guest);
        if (_initial.Mode == AppointmentFormMode.WalkIn) _newClientSection.IsVisible = guest;
        else _guestSection.IsVisible = guest;
        _owner.IsVisible = !guest;
        _pet.IsVisible = !guest;
        if (!guest) _match.IsVisible = false;
        SaveButton.Text = SaveText();
        ShowMessage(null);
    }

    private IDispatcherTimer _lookupTimer;
    private string _lookupText;

    /// <summary>Once the contact no. has 11 digits (after a short pause), asks whether it belongs to a registered owner.</summary>
    private void LookUpContact(string text)
    {
        _match.IsVisible = false;
        _lookupText = text;
        if (Validator.Digits(text).Length != 11) return;
        if (_lookupTimer == null)
        {
            _lookupTimer = Dispatcher.CreateTimer();
            _lookupTimer.Interval = TimeSpan.FromMilliseconds(400);
            _lookupTimer.IsRepeating = false;
            _lookupTimer.Tick += (s, e) => ContactLookupRequested?.Invoke(this, _lookupText);
        }
        _lookupTimer.Stop();
        _lookupTimer.Start();
    }

    /// <summary>One half of the segmented control; selected = cyan background, bold navy text.</summary>
    private static (Border Button, Label Text) SwitchOption(string text, Action select)
    {
        var label = new Label { Text = text, FontSize = 14.5, FontFamily = "DMSans", HorizontalOptions = LayoutOptions.Fill, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap };
        var button = new Border { HeightRequest = 44, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 6 }, Content = label };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => select();
        button.GestureRecognizers.Add(tap);
        HandCursor.Apply(button);
        SemanticProperties.SetDescription(button, text);
        return (button, label);
    }

    private static void PaintOption((Border Button, Label Text) option, bool selected)
    {
        option.Button.BackgroundColor = ThemeResources.Color(selected ? "Cyan" : "Surface");
        option.Button.Stroke = ThemeResources.Color(selected ? "Cyan" : "BorderColor");
        option.Text.FontFamily = selected ? "DMSansBold" : "DMSans";
        option.Text.TextColor = ThemeResources.Color(selected ? "Navy" : "BodyText");
    }

    /// <summary>"Owner" / "Pet" sub-heading of the walk-in registration fields.</summary>
    private static View SectionTitle(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 8,
        Margin = new Thickness(0, 4, 0, -6),
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontFamily = "NunitoExtraBold", FontSize = 17, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center }
        }
    };

    private string SaveText() => _initial.Mode switch
    {
        AppointmentFormMode.Edit => "Save changes",
        AppointmentFormMode.WalkIn => _isGuest ? "Register & check in" : "Book & check in",
        AppointmentFormMode.Reschedule => "Save new time",
        _ => "Book appointment"
    };

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(0, 0, 0, 8) };
        label.StyleClass = new[] { "field-label" };
        return label;
    }

    private static Label ErrorLabel()
    {
        var label = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        label.StyleClass = new[] { "field-error" };
        return label;
    }

    private static void SetError(Label error, string message, Border box)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        error.Text = message ?? string.Empty;
        error.IsVisible = invalid;
        if (box != null) box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }

    private void ShowMessage(string message)
    {
        MessageLabel.Text = message ?? string.Empty;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(message);
    }
}
