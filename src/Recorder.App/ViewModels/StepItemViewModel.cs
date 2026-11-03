using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

/// <summary>
/// One row of the Steps tab (spec 11.2): number, icon, sentence, the target's variable-name chip and a warning icon.
/// In edit mode it holds the edited values until they are saved through the undo history.
/// </summary>
public sealed partial class StepItemViewModel : ObservableObject
{
    private readonly Func<Guid?, PageElement?> _resolve;
    private PageElement? _element;

    public StepItemViewModel(RecordedStep step, Func<Guid?, PageElement?> resolve)
    {
        ArgumentNullException.ThrowIfNull(step);
        _resolve = resolve;
        Step = step;
        Attach();
    }

    public RecordedStep Step { get; private set; }

    public PageElement? Element => _element;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial int Number { get; set; }

    public string Glyph => StepText.Glyph(Step.Kind);

    public string Sentence => StepText.Sentence(Step, _element);

    public string? VariableName => _element?.VariableName;

    public bool HasWarning => Step.Warnings.Count > 0;

    public string? WarningText => HasWarning ? string.Join(Environment.NewLine, Step.Warnings.Distinct().Select(StepText.Warning)) : null;

    /// <summary>Everything at once for screen readers: "3. Type “5000” into Amount, amountInput".</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Number.ToString(CultureInfo.CurrentCulture) + ". " + Sentence, VariableName, WarningText }.Where(part => !string.IsNullOrEmpty(part)));

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string? EditText { get; set; }

    [ObservableProperty]
    public partial bool EditState { get; set; }

    [ObservableProperty]
    public partial string? EditRow { get; set; }

    /// <summary>What the text box edits: the typed text, a test-data key, a menu path, a column header or the comment.</summary>
    public string EditTextLabel => StepEditing.TextLabel(Step);

    public string? EditStateLabel => StepEditing.StateLabel(Step);

    public bool EditsState => EditStateLabel is not null;

    public bool EditsRow => Step.RowIndex is not null;

    public void Update(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        Detach();
        Step = step;
        Attach();
        IsEditing = false;
        Refresh();
    }

    /// <summary>Starts editing with the step's current values.</summary>
    public void BeginEdit()
    {
        EditText = StepEditing.Text(Step);
        EditState = Step.State ?? false;
        EditRow = Step.RowIndex?.ToString(CultureInfo.CurrentCulture);
        IsEditing = true;
    }

    /// <summary>The step with the edited values, or null when nothing changed or the row is not a number.</summary>
    public RecordedStep? Edited() => StepEditing.Apply(Step, EditText, EditState, EditRow);

    public void Detach()
    {
        if (_element is not null)
        {
            _element.Changed -= OnElementChanged;
        }
    }

    private void Attach()
    {
        _element = _resolve(Step.Target);
        if (_element is not null)
        {
            _element.Changed += OnElementChanged;
        }
    }

    private void OnElementChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => OnPropertyChanged(string.Empty);
}
