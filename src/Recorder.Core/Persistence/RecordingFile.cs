using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Persistence;

// The *.wfrec.json schema, version 1 (spec 12). Plain records so the file stays readable and diffable.
// Deliberately absent: element values (a value can be customer data, and passwords are never read anyway), window titles
// (LMS puts the open client in them), ancestors and runtime ids (valid only while the app runs). A page known only by its
// title is stored as "window:1", "window:2"…, and a window's Name (its title) is not stored (D-70).

internal sealed record RecordingFile
{
    public const string FormatName = "WinForms Recorder recording";

    public string? Format { get; init; }

    public int SchemaVersion { get; init; }

    public string? TargetProcessName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public List<PageFile> Pages { get; init; } = [];

    public List<StepFile> Steps { get; init; } = [];
}

internal sealed record PageFile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The page class name the user typed; absent for the derived one.</summary>
    public string? ClassName { get; init; }

    public List<ElementFile> Elements { get; init; } = [];
}

internal sealed record ElementFile
{
    public Guid Id { get; init; }

    public required string VariableName { get; init; }

    public bool UserRenamed { get; init; }

    public required string ControlType { get; init; }

    public string? AutomationId { get; init; }

    public string? Name { get; init; }

    public string? ClassName { get; init; }

    public string? FrameworkId { get; init; }

    public bool IsPassword { get; init; }

    public string? LabelText { get; init; }

    public string? WindowAutomationId { get; init; }

    public string? WindowClassName { get; init; }

    /// <summary>In a pop-up only a desktop session reaches (F-23).</summary>
    public bool InPopup { get; init; }

    public LocatorsFile? Locators { get; init; }
}

internal sealed record LocatorsFile
{
    public int SelectedIndex { get; init; }

    public bool UserChose { get; init; }

    public bool IsChecked { get; init; }

    public LocatorScope Scope { get; init; }

    public int TreeSize { get; init; }

    public List<CandidateFile> Candidates { get; init; } = [];
}

internal sealed record CandidateFile
{
    public LocatorStrategy Strategy { get; init; }

    public required string Value { get; init; }

    public LocatorScope Scope { get; init; }

    public Stability Stability { get; init; }

    public int Score { get; init; }

    public int MatchCount { get; init; }

    public int DistinctMatchCount { get; init; }

    public bool IsUnique { get; init; }

    public List<LocatorNote> Notes { get; init; } = [];

    public LocatorKind Kind { get; init; }

    public Refinement Refinement { get; init; }

    public string? Anchor { get; init; }

    public int MatchesBefore { get; init; }

    public required SpecFile Spec { get; init; }
}

internal sealed record SpecFile
{
    public List<SpecStepFile> Steps { get; init; } = [];

    public int? ResultIndex { get; init; }
}

internal sealed record SpecStepFile
{
    public Axis Axis { get; init; }

    public string? ControlType { get; init; }

    public List<PredicateFile> Predicates { get; init; } = [];

    public int? Index { get; init; }
}

internal enum PredicateOperator
{
    Exact,
    StartsWith,
    Contains,
}

internal sealed record PredicateFile
{
    public PredicateOperator Operator { get; init; }

    public AttributeName Attribute { get; init; }

    public required string Value { get; init; }
}

internal sealed record StepFile
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    public StepKind Kind { get; init; }

    public Guid? Target { get; init; }

    public string? Text { get; init; }

    public string? TestDataKey { get; init; }

    public bool? State { get; init; }

    public int? RowIndex { get; init; }

    public int? ColumnIndex { get; init; }

    public string? ColumnHeader { get; init; }

    /// <summary>Absent in files saved before 0.7.1 (D-83).</summary>
    public string? CellControlType { get; init; }

    public List<string>? MenuItems { get; init; }

    public string? Comment { get; init; }

    public int? Scroll { get; init; }

    public List<StepWarning>? Warnings { get; init; }
}
