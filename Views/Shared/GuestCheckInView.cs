using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// "Check in guest" (section 4.C2, side panel, 480px). Step 1 – Owner: use the registered owner with the same
/// contact no., or register a new one (pre-filled from the guest booking). Step 2 – Pet: pick one of the owner's
/// pets or add a new one (pre-filled). The controller validates and saves everything in one transaction.
/// </summary>
public sealed class GuestCheckInView : ContentView
{
    public const double PanelWidth = 480;

    private readonly Appointment _appointment;
    private readonly Owner _match;
    private readonly VerticalStackLayout _body;
    private readonly (Border Circle, Label Number, Label Text) _step1, _step2;
    private readonly HoverButton _back, _next;
    private readonly Label _message;
    private OwnerFieldsView _ownerFields;
    private PetFieldsView _petFields;
    private int _step = 1;
    private int _selectedPetId;
    private string _nextText;

    public GuestCheckInView(Appointment a, Owner match)
    {
        _appointment = a;
        _match = match;

        var title = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "Check in guest", FontFamily = "NunitoExtraBold", FontSize = 24, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = a.TimeText + " · " + a.VetName, FontSize = 14, TextColor = ThemeResources.Color("BodyText") }
            }
        };
        var header = new Grid { Padding = new Thickness(24, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(title, 0, 0);
        header.Add(PageKit.IconButton("x", "Close", () => CancelRequested?.Invoke(this, EventArgs.Empty)), 1, 0);

        var (infoBox, info) = PharmacyKit.InfoBox("CyanSoft", "Navy");
        info.Text = a.GuestInfoText;

        _step1 = Step(1, "Owner");
        _step2 = Step(2, "Pet");
        var steps = new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                StepView(_step1),
                new LucideIcon { Icon = "arrow-right", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center },
                StepView(_step2)
            }
        };

        _body = new VerticalStackLayout { Spacing = 16 };
        _message = new Label { IsVisible = false, FontFamily = "DMSansMedium", FontSize = 14, TextColor = ThemeResources.Color("Danger") };
        var form = new VerticalStackLayout { Padding = new Thickness(24, 20), Spacing = 18, Children = { infoBox, steps, _body, _message } };

        _back = new HoverButton { Text = "Cancel", Style = ThemeResources.Get<Style>("OutlineButton"), Padding = new Thickness(22, 0) };
        _back.Clicked += (s, e) => { if (_step == 2) ShowOwnerStep(); else CancelRequested?.Invoke(this, EventArgs.Empty); };
        _next = new HoverButton { Style = ThemeResources.Get<Style>("WorkspaceButton") };
        _next.Clicked += (s, e) =>
        {
            ShowMessage(null);
            if (_step == 1) NextRequested?.Invoke(this, EventArgs.Empty);
            else CheckInRequested?.Invoke(this, EventArgs.Empty);
        };
        var footer = new Grid { Padding = new Thickness(24, 16), ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        footer.Add(_back, 0, 0);
        footer.Add(_next, 1, 0);

        var scroll = new ScrollView { Content = form };
        ScrollFocus.DontScrollOnFocusWhileOpening(scroll);
        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, Line(LayoutOptions.End) } }, 0, 0);
        layout.Add(scroll, 0, 1);
        layout.Add(new Grid { Children = { footer, Line(LayoutOptions.Start) } }, 0, 2);
        Content = layout;

        UseMatch = match != null;
        ShowOwnerStep();
    }

    public event EventHandler CancelRequested;

    /// <summary>Step 1 is done: check the owner and answer with <see cref="ShowPetStep"/>.</summary>
    public event EventHandler NextRequested;

    public event EventHandler CheckInRequested;

    /// <summary>Step 1: true = use the matched registered owner; false = register a new owner.</summary>
    public bool UseMatch { get; private set; }

    public int ExistingOwnerId => UseMatch ? _match.OwnerId : 0;

    /// <summary>The new owner's values (when not using the match).</summary>
    public OwnerFormData NewOwner => UseMatch ? null : _ownerFields?.Read();

    /// <summary>Step 2: the picked pet, or 0 when adding a new one.</summary>
    public int ExistingPetId => _petFields == null ? _selectedPetId : 0;

    public PetFormData NewPet => _petFields?.Read();

    /// <summary>Step 2 with the owner's pets as cards (and "+ Add new pet"); a new owner goes straight to the pet form.</summary>
    public void ShowPetStep(IReadOnlyList<Pet> pets)
    {
        _step = 2;
        PaintSteps();
        _body.Children.Clear();
        _selectedPetId = 0;
        _petFields = null;

        if (pets.Count == 0)
        {
            ShowNewPetForm();
        }
        else
        {
            _body.Children.Add(new Label { Text = "Which pet is it?", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
            var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            var all = new List<Border>();
            int i = 0;
            foreach (var pet in pets)
            {
                Border card = null;
                card = PetCard(pet, () =>
                {
                    _selectedPetId = pet.PetId;
                    foreach (var c in all) Paint(c, c == card);
                    UpdateNext();
                });
                all.Add(card);
                if (i % 2 == 0) cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                cards.Add(card, i % 2, i / 2);
                i++;
            }
            if (i % 2 == 0) cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            cards.Add(AddPetCard(), i % 2, i / 2);
            _body.Children.Add(cards);

            // A pet with the guest pet's name is picked for you.
            var same = pets.FirstOrDefault(p => string.Equals(p.PetName, _appointment.PetName, StringComparison.OrdinalIgnoreCase));
            if (same != null)
            {
                _selectedPetId = same.PetId;
                Paint(all[pets.ToList().IndexOf(same)], true);
            }
        }
        _back.Text = "Back";
        _nextText = "Check in";
        _next.Text = _nextText;
        UpdateNext();
    }

    public void ShowResult(OperationResult result)
    {
        var errors = result.FieldErrors ?? new Dictionary<string, string>();
        var ownerErrors = errors.Where(e => e.Key.StartsWith(AppointmentFormData.NewOwnerPrefix, StringComparison.Ordinal))
            .ToDictionary(e => e.Key.Substring(AppointmentFormData.NewOwnerPrefix.Length), e => e.Value);
        var petErrors = errors.Where(e => e.Key.StartsWith(AppointmentFormData.NewPetPrefix, StringComparison.Ordinal))
            .ToDictionary(e => e.Key.Substring(AppointmentFormData.NewPetPrefix.Length), e => e.Value);
        if (ownerErrors.Count > 0 && _step == 2) ShowOwnerStep();
        if (ownerErrors.Count > 0) _ownerFields?.ShowErrors(ownerErrors);
        if (petErrors.Count > 0) _petFields?.ShowErrors(petErrors);
        ShowMessage(result.Message);
    }

    public void SetBusy(bool busy)
    {
        _next.IsEnabled = !busy && (_step == 1 || ExistingPetId > 0 || _petFields != null);
        _next.Text = busy ? "Please wait…" : _nextText;
    }

    // Step 1 ------------------------------------------------------------------------------------------

    private void ShowOwnerStep()
    {
        _step = 1;
        PaintSteps();
        _body.Children.Clear();
        _back.Text = "Cancel";

        if (UseMatch)
        {
            _body.Children.Add(new Label { Text = "A registered owner has this contact no.", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
            _body.Children.Add(MatchCard());
            var other = new TextLink { Text = "Register as a new owner instead", FontSize = 14 };
            other.Clicked += (s, e) => { UseMatch = false; ShowOwnerStep(); };
            _body.Children.Add(other);
            _nextText = "Use this owner";
        }
        else
        {
            if (_ownerFields == null)
            {
                var (first, last) = PersonName.Split(_appointment.GuestOwnerName ?? string.Empty);
                _ownerFields = new OwnerFieldsView(first, last, _appointment.GuestContactNo);
            }
            _body.Children.Add(new Label { Text = "Register the owner", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
            _body.Children.Add(_ownerFields);
            if (_match != null)
            {
                var use = new TextLink { Text = "Use the registered owner " + _match.FullName + " instead", FontSize = 14 };
                use.Clicked += (s, e) => { UseMatch = true; ShowOwnerStep(); };
                _body.Children.Add(use);
            }
            _nextText = "Next";
        }
        _next.Text = _nextText;
        _next.IsEnabled = true;
    }

    private View MatchCard()
    {
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(PageKit.Avatar(PersonName.Initials(_match.FirstName, _match.LastName), 44), 0, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = _match.FullName, FontFamily = "DMSansBold", FontSize = 16, TextColor = ThemeResources.Color("Navy") },
                new Label { Text = Validator.FormatPhMobile(_match.ContactNo) + (string.IsNullOrEmpty(_match.Email) ? string.Empty : " · " + _match.Email), FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") }
            }
        }, 1, 0);
        return new Border
        {
            Padding = new Thickness(16, 14),
            StrokeThickness = 1,
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color("Surface"),
            Content = grid
        };
    }

    // Step 2 ------------------------------------------------------------------------------------------

    private void ShowNewPetForm()
    {
        _body.Children.Clear();
        _selectedPetId = 0;
        _petFields = new PetFieldsView(_appointment.PetName, _appointment.Species);
        _body.Children.Add(new Label { Text = "Register the pet", FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy") });
        _body.Children.Add(_petFields);
        UpdateNext();
    }

    private Border PetCard(Pet pet, Action select)
    {
        var card = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    Staff.PetsPage.SpeciesAvatar(pet.Species, 36),
                    new VerticalStackLayout
                    {
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = pet.PetName, FontFamily = "DMSansBold", FontSize = 14.5, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 130 },
                            new Label { Text = pet.SpeciesAndBreed, FontSize = 12.5, TextColor = ThemeResources.Color("Muted"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 130 }
                        }
                    }
                }
            }
        };
        Paint(card, false);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => select();
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        SemanticProperties.SetDescription(card, pet.PetName);
        return card;
    }

    private View AddPetCard()
    {
        var card = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            MinimumHeightRequest = 62,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new LucideIcon { Icon = "plus", StrokeColor = ThemeResources.Color("Orange"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Add new pet", FontFamily = "DMSansBold", FontSize = 14, TextColor = ThemeResources.Color("Orange"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => ShowNewPetForm();
        card.GestureRecognizers.Add(tap);
        HandCursor.Apply(card);
        SemanticProperties.SetDescription(card, "Add new pet");
        return card;
    }

    private static void Paint(Border card, bool selected)
    {
        card.BackgroundColor = ThemeResources.Color(selected ? "CyanSoft" : "Surface");
        card.Stroke = ThemeResources.Color(selected ? "Navy" : "BorderColor");
    }

    private void UpdateNext() => _next.IsEnabled = _step == 1 || _selectedPetId > 0 || _petFields != null;

    // Steps ---------------------------------------------------------------------------------------------

    private static (Border Circle, Label Number, Label Text) Step(int number, string text)
    {
        var numberLabel = new Label { Text = number.ToString(), FontFamily = "DMSansBold", FontSize = 13, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var circle = new Border { WidthRequest = 26, HeightRequest = 26, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 13 }, Content = numberLabel };
        return (circle, numberLabel, new Label { Text = text, FontSize = 14.5, VerticalOptions = LayoutOptions.Center });
    }

    private static View StepView((Border Circle, Label Number, Label Text) step) =>
        new HorizontalStackLayout { Spacing = 8, Children = { step.Circle, step.Text } };

    /// <summary>Active step = orange circle; done step = mint circle with a check; next step = gray.</summary>
    private void PaintSteps()
    {
        PaintStep(_step1, active: _step == 1, done: _step > 1);
        PaintStep(_step2, active: _step == 2, done: false);
    }

    private static void PaintStep((Border Circle, Label Number, Label Text) step, bool active, bool done)
    {
        step.Circle.BackgroundColor = ThemeResources.Color(active ? "Orange" : done ? "Mint" : "LightGray");
        if (done)
            step.Circle.Content = new LucideIcon { Icon = "check", StrokeColor = ThemeResources.Color("CompletedText"), StrokeWidth = 2.5f, WidthRequest = 14, HeightRequest = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        else
        {
            step.Circle.Content = step.Number;
            step.Number.TextColor = active ? Colors.White : ThemeResources.Color("BodyText");
        }
        step.Text.FontFamily = active ? "DMSansBold" : "DMSans";
        step.Text.TextColor = ThemeResources.Color(active || done ? "Navy" : "BodyText");
    }

    private void ShowMessage(string message)
    {
        _message.Text = message ?? string.Empty;
        _message.IsVisible = !string.IsNullOrEmpty(message);
    }

    private static BoxView Line(LayoutOptions position) => new() { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = position };
}
