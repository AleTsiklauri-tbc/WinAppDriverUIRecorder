using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// A step as the recording engine produces it, pointing at element snapshots. The app turns it into a
/// <see cref="RecordedStep"/> once the elements are in the catalog (which belongs to the UI thread).
/// </summary>
public sealed record StepDraft
{
    public required Guid Id { get; init; }

    public required StepKind Kind { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>The element the step acts on; null for a click whose element could not be identified, or a key press with nothing focused.</summary>
    public ElementSnapshot? Target { get; init; }

    public string? Text { get; init; }

    /// <summary>The value must not be stored (a password, or any typed value when masking is on): the step gets a test-data key instead.</summary>
    public bool IsSecret { get; init; }

    public bool? State { get; init; }

    public int? RowIndex { get; init; }

    public int? ColumnIndex { get; init; }

    public string? ColumnHeader { get; init; }

    public string? CellControlType { get; init; }

    public IReadOnlyList<string> MenuItems { get; init; } = [];

    public int? Scroll { get; init; }

    public IReadOnlyList<StepWarning> Warnings { get; init; } = [];
}

/// <summary>A change to the recorded steps: one added, or an earlier one replaced (a click that became a double-click, say).</summary>
public abstract record StepChange(StepDraft Step);

public sealed record StepAdded(StepDraft Step) : StepChange(Step);

/// <param name="ReplacedId">The step to replace; if the user deleted it meanwhile, the new step is added instead.</param>
public sealed record StepReplaced(Guid ReplacedId, StepDraft Step) : StepChange(Step);
