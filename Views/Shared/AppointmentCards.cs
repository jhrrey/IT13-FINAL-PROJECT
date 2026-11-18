using Microsoft.Maui.Controls.Shapes;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Staff;

namespace VetCare.Views.Shared;

/// <summary>The appointment card shared by the queue (Appointments page) and My schedule (vet).</summary>
public static class AppointmentCards
{
    /// <summary>
    /// White card with a 4px left border in the status colour: time (+ sync badge when not synced),
    /// pet, owner, vet, reason pill and the next action button.
    /// </summary>
    /// <param name="action">The button at the bottom, or null for none.</param>
    public static View Card(Appointment a, AppointmentAction action, Action open, Action<string> act, bool showVet = true)
    {
        // Time row
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        top.Add(TimeLabel(a, 16), 0, 0);
        if (a.SyncStatus is SyncStatuses.Pending or SyncStatuses.Conflict)
        {
            var sync = Badges.Sync(a.SyncStatus);
            if (a.SyncStatus == SyncStatuses.Conflict) ToolTipProperties.SetText(sync, "Double booking — please reschedule");
            top.Add(sync, 1, 0);
        }

        // Pet row
        var pet = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        pet.Add(PetAvatar(a, 32), 0, 0);
        pet.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                WithGuestPill(a, new Label { Text = a.PetName, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation }),
                new Label { Text = a.SpeciesAndBreed, FontSize = 13, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.TailTruncation }
            }
        }, 1, 0);

        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { top, pet, IconLine("user", a.OwnerName) }
        };
        if (showVet) stack.Children.Add(IconLine("stethoscope", a.VetName));
        stack.Children.Add(ReasonPill(a.Reason));

        if (action != null)
        {
            View button;
            if (action.Style == "Link")
            {
                var link = new TextLink { Text = action.Text, FontSize = 14, Margin = new Thickness(0, 4, 0, 0) };
                link.StyleClass = new[] { "link" };
                link.Clicked += (s, e) => act(action.Key);
                button = link;
            }
            else
            {
                var hb = new HoverButton { Text = action.Text, Style = ThemeResources.Get<Style>(action.Style), HeightRequest = 38, Margin = new Thickness(0, 4, 0, 0) };
                hb.Clicked += (s, e) => act(action.Key);
                button = hb;
            }
            stack.Children.Add(button);
        }

        var body = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(4)), new ColumnDefinition(GridLength.Star) } };
        body.Add(new BoxView { Color = ThemeResources.Color(AppointmentStatuses.AccentFor(a.Status)) }, 0, 0);
        body.Add(new ContentView { Padding = new Thickness(12), Content = stack }, 1, 0);

        var card = new Border
        {
            BackgroundColor = ThemeResources.Color("Surface"),
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = 0,
            Content = body
        };

        // Clicking the card (not its button) opens the details panel.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (action != null && e.GetPosition(card) is Point p && p.Y > card.Height - 56) return; // the action button
            open();
        };
        card.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => card.Stroke = ThemeResources.Color("CardHoverBorder");
        hover.PointerExited += (s, e) => card.Stroke = ThemeResources.Color("BorderColor");
        card.GestureRecognizers.Add(hover);
        HandCursor.Apply(card);
        return card;
    }

    /// <summary>"9:30 AM" in bold navy; struck through when cancelled.</summary>
    public static Label TimeLabel(Appointment a, double size) => new()
    {
        Text = a.TimeText,
        FontFamily = "DMSansBold",
        FontSize = size,
        TextColor = ThemeResources.Color(a.Status == AppointmentStatuses.Cancelled ? "Muted" : "Navy"),
        TextDecorations = a.Status == AppointmentStatuses.Cancelled ? TextDecorations.Strikethrough : TextDecorations.None,
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>Light gray pill with the reason.</summary>
    public static View ReasonPill(string reason) => new Border
    {
        Padding = new Thickness(10, 3),
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        BackgroundColor = ThemeResources.Color("LightGray"),
        HorizontalOptions = LayoutOptions.Start,
        Content = new Label { Text = reason, FontSize = 12.5, TextColor = ThemeResources.Color("BodyText"), LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 220 }
    };

    private static View IconLine(string icon, string text) => new HorizontalStackLayout
    {
        Spacing = 8,
        Children =
        {
            new LucideIcon { Icon = icon, StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 15, HeightRequest = 15, VerticalOptions = LayoutOptions.Center },
            new Label { Text = text, FontSize = 13.5, TextColor = ThemeResources.Color("Navy"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 220 }
        }
    };

    /// <summary>The species avatar; a guest booking gets the generic paw icon.</summary>
    public static View PetAvatar(Appointment a, double size) => PetsPage.SpeciesAvatar(a.IsGuest ? PetSpecies.Other : a.Species, size);

    /// <summary>The pet name, followed by a light-gray "Guest" pill for a guest booking.</summary>
    public static View WithGuestPill(Appointment a, Label name)
    {
        if (!a.IsGuest) return name;
        name.VerticalOptions = LayoutOptions.Center;
        var pill = Badges.Pill("Guest", "BodyText", "LightGray");
        ToolTipProperties.SetText(pill, "New client – registered at check-in");
        return new HorizontalStackLayout { Spacing = 8, Children = { name, pill } };
    }
}
