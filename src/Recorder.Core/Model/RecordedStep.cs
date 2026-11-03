namespace Recorder.Core.Model;

public enum StepKind
{
    Click, DoubleClick, RightClick, SetText, ClearText, SelectOption, SetChecked, SelectTab,
    MenuPath, PressKeys, GridCellClick, SwitchWindow, AssertVisible, AssertEnabled, AssertText,
    AssertValue, AssertChecked, Comment,
}

public enum StepWarning
{
    ElementChanged, ElementUnavailable, StateUnavailable, LocatorUnchecked,
}

/// <summary>A semantic action. Passwords and masked input use TestDataKey, never Text.</summary>
/// <remarks>
/// Which fields a kind uses:
/// <list type="bullet">
/// <item>SetText: Text, or TestDataKey for a secret. SelectOption, SelectTab, AssertText, AssertValue: Text. PressKeys: Text is the chord ("Ctrl+S").</item>
/// <item>SetChecked, AssertEnabled, AssertChecked: State (null when it could not be read).</item>
/// <item>GridCellClick, and DoubleClick or RightClick on a grid cell: RowIndex and ColumnIndex (0-based, D-12), ColumnHeader, and
/// CellControlType (Edit for a text cell, CheckBox…; null in recordings made before 0.7.1, or for a row header); the target is the grid.</item>
/// <item>MenuPath: MenuItems from the menu bar (or context menu) down; the target is the first item.</item>
/// <item>SwitchWindow: the target is the window. Comment: Comment, and Scroll (wheel notches, negative = down) for a recorded scroll.</item>
/// </list>
/// </remarks>
public sealed record RecordedStep
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public required StepKind Kind { get; init; }

    public Guid? Target { get; init; }

    public string? Text { get; init; }

    public string? TestDataKey { get; init; }

    public bool? State { get; init; }

    public int? RowIndex { get; init; }

    public int? ColumnIndex { get; init; }

    public string? ColumnHeader { get; init; }

    /// <summary>The clicked cell's control type: it decides whether a framework's grid helper can click it (D-83).</summary>
    public string? CellControlType { get; init; }

    public IReadOnlyList<string> MenuItems { get; init; } = [];

    public string? Comment { get; init; }

    public int? Scroll { get; init; }

    public IReadOnlyList<StepWarning> Warnings { get; init; } = [];
}
