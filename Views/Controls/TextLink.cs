namespace VetCare.Views.Controls;

/// <summary>Clickable text that underlines while the pointer is over it.</summary>
public sealed class TextLink : Label
{
    public TextLink()
    {
        // A background makes the whole text box respond to the pointer, not just the glyphs.
        BackgroundColor = Colors.Transparent;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => Clicked?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (s, e) => TextDecorations = TextDecorations.Underline;
        pointer.PointerExited += (s, e) => TextDecorations = TextDecorations.None;
        GestureRecognizers.Add(pointer);

        HandCursor.Apply(this);
    }

    public event EventHandler Clicked;
}
