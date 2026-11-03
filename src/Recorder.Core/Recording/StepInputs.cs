using Recorder.Core.Model;

namespace Recorder.Core.Recording;

public enum PointerButton
{
    Left,
    Right,
}

/// <summary>A press and release within a few pixels, with the element under the press.</summary>
/// <param name="Element">Null when the target never answered (busy for too long): the click is kept, unidentified.</param>
/// <param name="Owner">The ancestor the step targets (<see cref="StepTargets.OwnerIndex"/>), read in full; null if none or unreadable.</param>
/// <param name="Window">The top-level window, read in full when it differs from <see cref="StepBuilder.CurrentWindow"/>.</param>
/// <param name="Time">The press's millisecond tick count from the hook, for double-clicks.</param>
/// <param name="ElementChanged">The element had to be looked up after the press and may not be what was clicked (spec 9.2).</param>
public sealed record ClickInput(
    PointerButton Button, ElementSnapshot? Element, ElementSnapshot? Owner, ElementSnapshot? Window, PixelPoint Point, uint Time, DateTimeOffset At,
    bool ElementChanged = false);

/// <summary>An input's value changed between focus-in and focus-out (spec 9.3).</summary>
/// <param name="Element">The input (for the text part of a combo box, the combo box).</param>
/// <param name="IsSecret">A password field: no value is known, only that something was typed.</param>
public sealed record ValueInput(ElementSnapshot Element, ElementSnapshot? Window, string? Before, string? After, bool IsSecret, DateTimeOffset At);
