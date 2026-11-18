using Microsoft.Maui.Controls.Shapes;
using VetCare.Models.Entities;
using VetCare.Views.Controls;

namespace VetCare.Views.Shared;

/// <summary>
/// "Owner *" searchable dropdown: a search box that lists matching owners (name + contact no.)
/// and, once one is picked, shows that owner with a "Change" link. The search results come from
/// the controller (<see cref="SearchRequested"/> → <see cref="ShowResults"/>). Used by the pet and
/// appointment forms.
/// </summary>
public sealed class OwnerPickerField : ContentView
{
    private readonly Border _box;
    private readonly Grid _searchRow;
    private readonly Entry _search;
    private readonly Border _results;
    private readonly VerticalStackLayout _resultsStack;
    private readonly Label _hint;
    private readonly Label _error;
    private IDispatcherTimer _timer;

    public OwnerPickerField(string label = "Owner *")
    {
        _search = new Entry { Style = ThemeResources.Get<Style>("InputEntry"), Placeholder = "Search owner name or contact no." };
        _search.TextChanged += (s, e) => QueueSearch();
        _search.Focused += (s, e) => { if (!_results.IsVisible) RequestSearch(); };

        _searchRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        _searchRow.Add(new LucideIcon { Icon = "search", StrokeColor = ThemeResources.Color("Muted"), WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center }, 0, 0);
        _searchRow.Add(_search, 1, 0);

        _box = new Border { Style = ThemeResources.Get<Style>("InputBox"), Padding = new Thickness(12, 0), Content = _searchRow };
        _resultsStack = new VerticalStackLayout();
        _results = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 4),
            BackgroundColor = ThemeResources.Color("Surface"),
            Stroke = ThemeResources.Color("BorderColor"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = _resultsStack
        };
        _hint = new Label { IsVisible = false, FontSize = 13.5, TextColor = ThemeResources.Color("Warning"), Margin = new Thickness(0, 6, 0, 0) };
        _error = new Label { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _error.StyleClass = new[] { "field-error" };

        var title = new Label { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        title.StyleClass = new[] { "field-label" };

        Content = new VerticalStackLayout { Children = { title, _box, _results, _hint, _error } };
    }

    /// <summary>The search text changed (or the box got focus): answer with <see cref="ShowResults"/>.</summary>
    public event EventHandler<string> SearchRequested;

    /// <summary>An owner was picked, or cleared with "Change" (null).</summary>
    public event EventHandler<Owner> OwnerChanged;

    public Owner SelectedOwner { get; private set; }

    /// <summary>A note under the box (e.g. "Saving moves this pet to …"); null hides it.</summary>
    public string Hint
    {
        set
        {
            _hint.Text = value ?? string.Empty;
            _hint.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public void Select(Owner owner)
    {
        SelectedOwner = owner;
        _timer?.Stop();
        _results.IsVisible = false;
        SetError(null);

        var chosen = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        chosen.Add(PageKit.Avatar(owner.Initials, 32, 16, 12), 0, 0);
        chosen.Add(NameAndContact(owner, 15), 1, 0);
        var change = new TextLink { Text = "Change", FontSize = 14, VerticalOptions = LayoutOptions.Center };
        change.StyleClass = new[] { "link-accent" };
        change.Clicked += (s, e) => Clear();
        chosen.Add(change, 2, 0);
        _box.Content = chosen;

        OwnerChanged?.Invoke(this, owner);
    }

    /// <summary>The owners matching the search (at most a handful).</summary>
    public void ShowResults(IReadOnlyList<Owner> owners)
    {
        if (SelectedOwner != null) return; // picked while the search was running

        _resultsStack.Children.Clear();
        if (owners.Count == 0)
            _resultsStack.Children.Add(new Label
            {
                Text = "No owners match. Add the owner in Owners first.",
                FontSize = 14,
                TextColor = ThemeResources.Color("BodyText"),
                Padding = new Thickness(14, 10)
            });
        foreach (var owner in owners) _resultsStack.Children.Add(Result(owner));
        _results.IsVisible = true;
    }

    public void ShowSearchError(string message)
    {
        _resultsStack.Children.Clear();
        _resultsStack.Children.Add(new Label { Text = message, FontSize = 14, TextColor = ThemeResources.Color("Danger"), Padding = new Thickness(14, 10) });
        _results.IsVisible = true;
    }

    public void SetError(string message)
    {
        bool invalid = !string.IsNullOrEmpty(message);
        _error.Text = message ?? string.Empty;
        _error.IsVisible = invalid;
        _box.Stroke = ThemeResources.Color(invalid ? "Orange" : "BorderColor");
    }

    private void Clear()
    {
        SelectedOwner = null;
        Hint = null;
        _search.Text = string.Empty;
        _box.Content = _searchRow;
        _search.Focus();
        OwnerChanged?.Invoke(this, null);
        RequestSearch();
    }

    private void QueueSearch()
    {
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => RequestSearch();
        return timer;
    }

    private void RequestSearch()
    {
        if (SelectedOwner == null) SearchRequested?.Invoke(this, _search.Text?.Trim() ?? string.Empty);
    }

    private View Result(Owner owner)
    {
        var row = new Grid
        {
            Padding = new Thickness(14, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        row.Add(PageKit.Avatar(owner.Initials, 32, 16, 12), 0, 0);
        row.Add(NameAndContact(owner, 14), 1, 0);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Select(owner);
        row.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (s, e) => row.BackgroundColor = ThemeResources.Color("RowHover");
        hover.PointerExited += (s, e) => row.BackgroundColor = Colors.Transparent;
        row.GestureRecognizers.Add(hover);
        HandCursor.Apply(row);
        return row;
    }

    private static View NameAndContact(Owner owner, double nameSize) => new VerticalStackLayout
    {
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = owner.FullName, FontFamily = "DMSansBold", FontSize = nameSize, TextColor = ThemeResources.Color("Navy"), LineBreakMode = LineBreakMode.TailTruncation },
            new Label { Text = owner.ContactDisplay, FontSize = 13, TextColor = ThemeResources.Color("Muted") }
        }
    };
}
