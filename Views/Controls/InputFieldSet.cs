using VetCare.Models;

namespace VetCare.Views.Controls;

/// <summary>
/// The <see cref="InputField"/>s of one form: builds them, reads their values,
/// shows errors and clears them. Shared by the sign-in card and the registration modal.
/// </summary>
internal sealed class InputFieldSet
{
    private readonly List<InputField> _fields = new();

    /// <summary>Enter was pressed in one of the fields.</summary>
    public event EventHandler SubmitRequested;

    /// <summary>The user typed in one of the fields.</summary>
    public event EventHandler ValueChanged;

    public bool HasFocus => _fields.Any(f => f.IsInputFocused);

    /// <summary>Removes the current fields and adds one per definition to the layout chosen by <paramref name="containerFor"/>.</summary>
    public void Build(IEnumerable<AuthField> definitions, Func<AuthField, Layout> containerFor)
    {
        foreach (var old in _fields) (old.Parent as Layout)?.Remove(old);
        _fields.Clear();

        foreach (var definition in definitions)
        {
            var field = new InputField(definition);
            field.SubmitRequested += (s, e) => SubmitRequested?.Invoke(this, EventArgs.Empty);
            field.ValueChanged += (s, e) => ValueChanged?.Invoke(this, EventArgs.Empty);
            _fields.Add(field);
            containerFor(definition).Add(field);
        }
    }

    public IDictionary<string, string> ReadValues() => _fields.ToDictionary(f => f.FieldName, f => f.Value);

    public void ShowErrors(IDictionary<string, string> errors)
    {
        InputField firstInvalid = null;
        foreach (var field in _fields)
        {
            errors.TryGetValue(field.FieldName, out var message);
            field.SetError(message);
            if (message != null && firstInvalid == null) firstInvalid = field;
        }

        firstInvalid?.FocusInput();
    }

    public void ClearInputs()
    {
        foreach (var field in _fields)
        {
            field.Value = string.Empty;
            field.SetError(null);
        }
    }

    public void SetValue(string fieldName, string value)
    {
        var field = _fields.FirstOrDefault(f => f.FieldName == fieldName);
        if (field != null) field.Value = value;
    }

    public void ClearErrors()
    {
        foreach (var field in _fields) field.SetError(null);
    }

    public void FocusFirst()
    {
        if (_fields.Count > 0) _fields[0].FocusInput();
    }
}
