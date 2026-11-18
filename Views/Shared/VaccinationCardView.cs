using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Views.Controls;
using VetCare.Views.Staff;

namespace VetCare.Views.Shared;

/// <summary>
/// The pet vaccination card (520px side panel): pet and owner, an overall status banner, and every
/// vaccine grouped by name with the status of its latest dose. Raises events only.
/// </summary>
public sealed class VaccinationCardView : ContentView
{
    public const double PanelWidth = 520;

    public VaccinationCardView(Pet pet, IReadOnlyList<RecordVaccination> vaccinations, bool canPrint, bool canBook)
    {
        var today = DateTime.Today;

        // Header: avatar, name, details, owner; close button
        var close = PageKit.IconButton("x", "Close", () => CloseRequested?.Invoke(this, EventArgs.Empty));
        var header = new Grid
        {
            Padding = new Thickness(24, 18),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 14
        };
        header.Add(PetsPage.SpeciesAvatar(pet.Species, 56), 0, 0);
        header.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = pet.PetName, FontFamily = "NunitoExtraBold", FontSize = 22, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = string.Join(" · ", new[] { pet.Species, pet.Breed, pet.Sex, pet.AgeText }.Where(t => !string.IsNullOrWhiteSpace(t))), FontSize = 13.5, TextColor = ThemeResources.Color("BodyText") },
                new Label { Text = pet.OwnerName + " · " + pet.OwnerContactDisplay, FontSize = 13.5, TextColor = ThemeResources.Color("Muted") }
            }
        }, 1, 0);
        close.VerticalOptions = LayoutOptions.Start;
        header.Add(close, 2, 0);

        // The latest dose of each vaccine decides its status.
        var groups = vaccinations.GroupBy(v => v.VaccineName).OrderBy(g => g.Key).ToList();
        var latest = groups.Select(g => g.OrderByDescending(v => v.DateGiven).ThenByDescending(v => v.VaccinationId).First()).ToList();
        int overdue = latest.Count(v => VaccineScheduleHelper.StatusOf(v.NextDueDate, today) == VaccineStatus.Overdue);
        int dueSoon = latest.Count(v => VaccineScheduleHelper.StatusOf(v.NextDueDate, today) == VaccineStatus.DueSoon);

        var body = new VerticalStackLayout { Padding = new Thickness(24, 16), Spacing = 16 };
        body.Children.Add(Banner(vaccinations.Count == 0, overdue, dueSoon));

        if (vaccinations.Count == 0)
            body.Children.Add(new Label { Text = "No vaccines recorded yet. Vaccines are recorded by the vet during a consultation.", FontSize = 14, TextColor = ThemeResources.Color("BodyText") });

        foreach (var group in groups)
        {
            var newest = group.OrderByDescending(v => v.DateGiven).ThenByDescending(v => v.VaccinationId).First();
            var table = new VerticalStackLayout { Spacing = 0 };
            table.Children.Add(new Label { Text = group.Key, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color("Navy"), Margin = new Thickness(0, 0, 0, 6) });
            table.Children.Add(Row(new[] { "DOSE", "DATE GIVEN", "GIVEN BY", "NEXT DUE", "STATUS" }, header: true));
            foreach (var v in group.OrderByDescending(v => v.DateGiven).ThenByDescending(v => v.VaccinationId))
            {
                var row = Row(new[] { v.DoseText, v.DateGiven.ToString("MMM d, yyyy"), v.GivenByName, v.NextDueDate?.ToString("MMM d, yyyy") ?? "—", null }, header: false);
                var status = v == newest
                    ? Badges.VaccineStatus(VaccineScheduleHelper.StatusOf(v.NextDueDate, today))
                    : new Label { Text = "—", FontSize = 13, TextColor = ThemeResources.Color("Muted"), VerticalOptions = LayoutOptions.Center };
                ((Grid)((Grid)row).Children[0]).Add(status, 4, 0);
                table.Children.Add(row);
            }
            body.Children.Add(new Border
            {
                Padding = new Thickness(14, 12),
                Stroke = ThemeResources.Color("BorderColor"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = ThemeResources.Color("Surface"),
                Content = table
            });
        }

        // Footer: Print card / Book vaccination
        var footer = new Grid { Padding = new Thickness(24, 14), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        footer.Add(new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(-24, -14, -24, 0) }, 0, 0);
        Grid.SetColumnSpan(footer.Children[0] as BoxView, 2);
        int column = 0;
        if (canPrint)
        {
            var print = new HoverButton { Text = "Print card", Style = ThemeResources.Get<Style>("OutlineButton") };
            print.Clicked += (s, e) => PrintRequested?.Invoke(this, EventArgs.Empty);
            footer.Add(print, column++, 0);
        }
        if (canBook)
        {
            var book = new HoverButton { Text = "Book vaccination", Style = ThemeResources.Get<Style>("WorkspaceButton") };
            book.Clicked += (s, e) => BookRequested?.Invoke(this, EventArgs.Empty);
            footer.Add(book, column++, 0);
        }
        if (column == 1) Grid.SetColumnSpan(footer.Children[^1] as View, 2);
        footer.IsVisible = column > 0;

        var scroll = new ScrollView { Content = body };
        ScrollFocus.DontScrollOnFocusWhileOpening(scroll);
        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Grid { Children = { header, new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End } } }, 0, 0);
        layout.Add(scroll, 0, 1);
        layout.Add(footer, 0, 2);
        Content = layout;
    }

    public event EventHandler CloseRequested;
    public event EventHandler PrintRequested;
    public event EventHandler BookRequested;

    /// <summary>"Up to date" (mint), "1 vaccine due soon" (amber) or "1 vaccine overdue" (light orange).</summary>
    private static View Banner(bool none, int overdue, int dueSoon)
    {
        var status = none ? VaccineStatus.Completed : overdue > 0 ? VaccineStatus.Overdue : dueSoon > 0 ? VaccineStatus.DueSoon : VaccineStatus.UpToDate;
        var (_, background, color, icon) = VaccineScheduleHelper.Style(status);
        string text = none ? "No vaccines recorded yet"
            : overdue > 0 ? overdue + (overdue == 1 ? " vaccine overdue" : " vaccines overdue") + (dueSoon > 0 ? " · " + dueSoon + " due soon" : string.Empty)
            : dueSoon > 0 ? dueSoon + (dueSoon == 1 ? " vaccine due soon" : " vaccines due soon")
            : "Up to date";
        return new Border
        {
            Padding = new Thickness(14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = ThemeResources.Color(none ? "LightGray" : background),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new LucideIcon { Icon = none ? "syringe" : icon, StrokeColor = ThemeResources.Color(none ? "BodyText" : color), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = text, FontFamily = "DMSansBold", FontSize = 15, TextColor = ThemeResources.Color(none ? "BodyText" : color), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
    }

    /// <summary>One table row (5 columns); the status cell is filled by the caller.</summary>
    private static View Row(IReadOnlyList<string> cells, bool header)
    {
        var grid = new Grid
        {
            MinimumHeightRequest = header ? 26 : 38,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(64)), new ColumnDefinition(new GridLength(92)),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(92)),
                new ColumnDefinition(new GridLength(100))
            },
            ColumnSpacing = 8
        };
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null) continue;
            grid.Add(new Label
            {
                Text = cells[i],
                FontFamily = header ? "DMSansBold" : "DMSans",
                FontSize = header ? 11 : 13,
                CharacterSpacing = header ? 0.5 : 0,
                TextColor = ThemeResources.Color(header ? "SectionLabel" : "Navy"),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, i, 0);
        }
        return new Grid
        {
            Children =
            {
                grid,
                new BoxView { HeightRequest = 1, Color = ThemeResources.Color("BorderColor"), VerticalOptions = LayoutOptions.End, IsVisible = !header }
            }
        };
    }
}
