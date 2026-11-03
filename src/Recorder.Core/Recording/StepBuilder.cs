using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// Turns clicks, value changes and keys into meaningful steps (spec 9.5). Pure and single-threaded: the recording session
/// feeds it in order, and every rule is unit-tested with synthetic events.
/// <list type="bullet">
/// <item>A second click on the same element within the double-click time becomes a DoubleClick; the right button a RightClick.</item>
/// <item>Check boxes and radio buttons become SetChecked (state read after the click), tab items SelectTab, menu items one MenuPath.</item>
/// <item>A click on a combo box followed by a click on an item of its drop-down list becomes one SelectOption.</item>
/// <item>A click on a grid cell or row becomes GridCellClick on the grid.</item>
/// <item>A click that only put the caret into an input is replaced by the value step that follows on that input.</item>
/// <item>The first step in another top-level window of the app gets a SwitchWindow before it (owned dialogs do not, F-7, D-43).</item>
/// </list>
/// </summary>
public sealed class StepBuilder
{
    private readonly TimeSpan _doubleClickTime;
    private readonly int _slop;
    private readonly bool _maskTypedValues;
    private readonly Dictionary<string, string?> _lastValues = new(StringComparer.Ordinal);
    private StepDraft? _last;
    private (Guid StepId, string Key, PixelPoint Point, uint Time)? _lastClick;
    private bool _lastMenuOpensSubmenu;

    /// <param name="mainWindow">The window the test's session starts in: steps there need no SwitchWindow.</param>
    /// <param name="slop">How far (pixels) two clicks may be apart and still be a double-click.</param>
    /// <param name="maskTypedValues">Spec 9.3 "Mask all typed values": every SetText gets a test-data key instead of its text.</param>
    public StepBuilder(nint mainWindow, TimeSpan doubleClickTime, int slop, bool maskTypedValues)
    {
        CurrentWindow = mainWindow;
        _doubleClickTime = doubleClickTime;
        _slop = slop;
        _maskTypedValues = maskTypedValues;
    }

    /// <summary>The top-level window the recorded steps are in now.</summary>
    public nint CurrentWindow { get; private set; }

    public IReadOnlyList<StepChange> Click(ClickInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var changes = new List<StepChange>();
        if (input.Element is not { } element)
        {
            Add(changes, Draft(input.Button == PointerButton.Right ? StepKind.RightClick : StepKind.Click, null, input.At) with { Warnings = [StepWarning.ElementUnavailable] });
            _lastClick = null;
            return changes;
        }

        SwitchIfNeeded(changes, element, input.Window, input.At);
        if (input.Button == PointerButton.Left && IsSecondClick(element, input) && _last is { } first)
        {
            Replace(changes, first with { Kind = StepKind.DoubleClick, At = input.At });
            _lastClick = null;
            return changes;
        }

        var draft = input.Button == PointerButton.Right ? RightClickDraft(input, element) : ClickDraft(input, element);
        if (input.ElementChanged)
        {
            draft = draft with { Warnings = [StepWarning.ElementChanged] };
        }

        if (draft.Kind == StepKind.SelectOption && _last is { Kind: StepKind.Click } opened && SameTarget(opened, draft.Target) && changes.Count == 0)
        {
            Replace(changes, draft with { Id = opened.Id });
        }
        else if (draft.Kind == StepKind.MenuPath && _last is { Kind: StepKind.MenuPath } menu && changes.Count == 0
            && (StartsWith(draft.MenuItems, menu.MenuItems) || _lastMenuOpensSubmenu))
        {
            Replace(changes, draft with { Id = menu.Id });
        }
        else
        {
            Add(changes, draft);
        }

        if (draft.Kind == StepKind.SelectOption)
        {
            _lastValues[draft.Target!.SessionKey] = draft.Text;
        }

        _lastMenuOpensSubmenu = draft.Kind == StepKind.MenuPath && element.SupportedPatterns.Contains("ExpandCollapse");
        _lastClick = input.Button == PointerButton.Left && draft.Kind is StepKind.Click or StepKind.GridCellClick
            ? (_last!.Id, element.SessionKey, input.Point, input.Time)
            : null;
        return changes;
    }

    /// <summary>The state of a SetChecked step, read after the click. Null when it could not be read.</summary>
    public IReadOnlyList<StepChange> CompleteState(StepDraft pending, bool? state)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var completed = pending with { State = state, Warnings = state is null ? [.. pending.Warnings, StepWarning.StateUnavailable] : pending.Warnings };
        if (_last?.Id == pending.Id)
        {
            _last = completed;
        }

        return [new StepReplaced(pending.Id, completed)];
    }

    public IReadOnlyList<StepChange> Value(ValueInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var element = input.Element;
        var key = element.SessionKey;
        var secret = input.IsSecret || element.IsPassword;
        StepDraft draft;
        if (secret)
        {
            draft = Draft(StepKind.SetText, element, input.At) with { IsSecret = true };
        }
        else
        {
            // Unchanged, or already recorded (a drop-down selection made with clicks is recorded when it is made).
            var repeated = _lastValues.TryGetValue(key, out var last) && last == input.After;
            if (repeated || input.After == input.Before)
            {
                return [];
            }

            draft = element.ControlType == "ComboBox"
                ? Draft(StepKind.SelectOption, element, input.At) with { Text = input.After }
                : string.IsNullOrEmpty(input.After)
                    ? Draft(StepKind.ClearText, element, input.At)
                    : Draft(StepKind.SetText, element, input.At) with { Text = _maskTypedValues ? null : input.After, IsSecret = _maskTypedValues };
            _lastValues[key] = input.After;
        }

        var changes = new List<StepChange>();
        SwitchIfNeeded(changes, element, input.Window, input.At);

        // A click that only put the caret into this input (or an earlier value of it) gives way to the value (D-45).
        if (changes.Count == 0 && _last is { Kind: StepKind.Click or StepKind.DoubleClick or StepKind.SetText or StepKind.ClearText or StepKind.SelectOption } previous
            && SameTarget(previous, element))
        {
            Replace(changes, draft with { Id = previous.Id });
        }
        else
        {
            Add(changes, draft);
        }

        _lastClick = null;
        return changes;
    }

    /// <summary>
    /// False for keys whose effect is recorded otherwise: editing keys inside an input (its new value is recorded) and Enter in
    /// a combo box (its selection is) (D-46).
    /// </summary>
    public static bool Records(HotkeyGesture chord, ElementSnapshot? focused)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (focused is null || !StepTargets.IsValueInput(focused))
        {
            return true;
        }

        var plainEnter = chord.VirtualKey == 0x0D && chord.Modifiers == HotkeyModifiers.None;
        return !KeyClassifier.EditsText(chord) && !(plainEnter && focused.ControlType == "ComboBox");
    }

    public IReadOnlyList<StepChange> Keys(HotkeyGesture chord, ElementSnapshot? focused, ElementSnapshot? window, DateTimeOffset at)
    {
        if (!Records(chord, focused))
        {
            return [];
        }

        var changes = new List<StepChange>();
        if (focused is not null)
        {
            SwitchIfNeeded(changes, focused, window, at);
        }

        Add(changes, Draft(StepKind.PressKeys, focused, at) with { Text = chord.ToString() });
        _lastClick = null;
        return changes;
    }

    /// <summary>A wheel scroll, recorded as a comment when that setting is on. Consecutive notches in one direction are one step.</summary>
    public IReadOnlyList<StepChange> Scroll(ElementSnapshot element, ElementSnapshot? window, int notches, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(element);
        var changes = new List<StepChange>();
        SwitchIfNeeded(changes, element, window, at);
        if (changes.Count == 0 && _last is { Kind: StepKind.Comment, Scroll: { } previous } last && SameTarget(last, element) && Math.Sign(previous) == Math.Sign(notches))
        {
            Replace(changes, last with { Scroll = previous + notches, At = at });
        }
        else
        {
            Add(changes, Draft(StepKind.Comment, element, at) with { Scroll = notches });
        }

        _lastClick = null;
        return changes;
    }

    /// <summary>An assertion chosen in Assert mode (spec 9.6).</summary>
    public IReadOnlyList<StepChange> Assert(StepKind kind, ElementSnapshot element, ElementSnapshot? window, string? text, bool? state, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (kind is not (StepKind.AssertVisible or StepKind.AssertEnabled or StepKind.AssertText or StepKind.AssertValue or StepKind.AssertChecked))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an assertion.");
        }

        var changes = new List<StepChange>();
        SwitchIfNeeded(changes, element, window, at);
        Add(changes, Draft(kind, element, at) with { Text = text, State = state });
        _lastClick = null;
        return changes;
    }

    private static StepDraft ClickDraft(ClickInput input, ElementSnapshot element)
    {
        if (StepTargets.IsGridPart(element) && input.Owner is { } grid)
        {
            return GridDraft(StepKind.GridCellClick, element, grid, input.At);
        }

        var owner = input.Owner;
        return element.ControlType switch
        {
            "CheckBox" or "RadioButton" => Draft(StepKind.SetChecked, element, input.At),
            "TabItem" => Draft(StepKind.SelectTab, element, input.At) with { Text = element.Name },
            "MenuItem" => Draft(StepKind.MenuPath, owner ?? element, input.At) with { MenuItems = StepTargets.MenuPathOf(element) },
            "ListItem" when owner is { ControlType: "ComboBox" } => Draft(StepKind.SelectOption, owner, input.At) with { Text = element.Name },
            "Edit" or "Button" when owner is { ControlType: "ComboBox" } => Draft(StepKind.Click, owner, input.At),
            _ => Draft(StepKind.Click, element, input.At),
        };
    }

    private static StepDraft RightClickDraft(ClickInput input, ElementSnapshot element) =>
        StepTargets.IsGridPart(element) && input.Owner is { } grid
            ? GridDraft(StepKind.RightClick, element, grid, input.At)
            : Draft(StepKind.RightClick, input.Owner is { ControlType: "ComboBox" } combo ? combo : element, input.At);

    // A column without a header (the check box column in LMS grids) is identified by its index alone. The cell's control
    // type decides whether a framework's grid helper can click it (D-83).
    private static StepDraft GridDraft(StepKind kind, ElementSnapshot part, ElementSnapshot grid, DateTimeOffset at)
    {
        var header = part.Cell?.ColumnHeader;
        return Draft(kind, grid, at) with
        {
            RowIndex = StepTargets.RowOf(part),
            ColumnIndex = part.Cell?.Column,
            ColumnHeader = string.IsNullOrEmpty(header) ? null : header,
            CellControlType = part.Cell is null ? null : part.ControlType,
        };
    }

    private bool IsSecondClick(ElementSnapshot element, ClickInput input) =>
        _lastClick is { } previous
        && _last?.Id == previous.StepId
        && element.SessionKey.Length > 0
        && previous.Key == element.SessionKey
        && unchecked(input.Time - previous.Time) <= _doubleClickTime.TotalMilliseconds
        && Math.Abs(input.Point.X - previous.Point.X) <= _slop
        && Math.Abs(input.Point.Y - previous.Point.Y) <= _slop;

    private void SwitchIfNeeded(List<StepChange> changes, ElementSnapshot element, ElementSnapshot? window, DateTimeOffset at)
    {
        var handle = element.TopLevelWindow.Handle;
        if (handle == 0 || handle == CurrentWindow)
        {
            return;
        }

        CurrentWindow = handle;
        Add(changes, Draft(StepKind.SwitchWindow, window, at) with { Warnings = window is null ? [StepWarning.ElementUnavailable] : [] });
    }

    private void Add(List<StepChange> changes, StepDraft draft)
    {
        changes.Add(new StepAdded(draft));
        _last = draft;
    }

    private void Replace(List<StepChange> changes, StepDraft draft)
    {
        changes.Add(new StepReplaced(draft.Id, draft));
        _last = draft;
    }

    private static bool SameTarget(StepDraft step, ElementSnapshot? element) =>
        element is not null && step.Target is { } target && target.SessionKey.Length > 0 && target.SessionKey == element.SessionKey;

    private static bool StartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix) =>
        prefix.Count <= path.Count && prefix.Select((item, i) => item == path[i]).All(same => same);

    private static StepDraft Draft(StepKind kind, ElementSnapshot? target, DateTimeOffset at) => new() { Id = Guid.NewGuid(), Kind = kind, Target = target, At = at };
}
