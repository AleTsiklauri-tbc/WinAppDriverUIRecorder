using Recorder.App.Resources;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

/// <summary>Human sentences and icons for steps (spec 11.2): "Type “5000” into Amount", "Click File › Export › PDF".</summary>
internal static class StepText
{
    private const int MaxNameLength = 40;

    public static string Sentence(RecordedStep step, PageElement? element)
    {
        ArgumentNullException.ThrowIfNull(step);
        var target = element is null ? Strings.Step_UnknownElement : Describe(element);
        return step.Kind switch
        {
            StepKind.Click => Text.Format(Strings.Step_Click, target),
            StepKind.DoubleClick => Text.Format(Strings.Step_DoubleClick, InGrid(step, target)),
            StepKind.RightClick => Text.Format(Strings.Step_RightClick, InGrid(step, target)),
            StepKind.GridCellClick => Text.Format(Strings.Step_Click, InGrid(step, target)),
            StepKind.SetText when step.TestDataKey is { } key => Text.Format(Strings.Step_SetTextSecret, target, key),
            StepKind.SetText => Text.Format(Strings.Step_SetText, target, step.Text),
            StepKind.ClearText => Text.Format(Strings.Step_ClearText, target),
            StepKind.SelectOption => Text.Format(Strings.Step_SelectOption, target, step.Text),
            StepKind.SetChecked => Checked(step, element, target),
            StepKind.SelectTab => Text.Format(Strings.Step_SelectTab, target, step.Text),
            StepKind.MenuPath => Text.Format(Strings.Step_MenuPath, string.Join(" › ", step.MenuItems)),
            StepKind.PressKeys => Text.Format(element is null ? Strings.Step_PressKeys : Strings.Step_PressKeysIn, target, step.Text),
            StepKind.SwitchWindow => Text.Format(Strings.Step_SwitchWindow, target),
            StepKind.AssertVisible => Text.Format(Strings.Step_AssertVisible, target),
            StepKind.AssertEnabled => Text.Format(step.State == false ? Strings.Step_AssertDisabled : Strings.Step_AssertEnabled, target),
            StepKind.AssertText => Text.Format(Strings.Step_AssertText, target, step.Text),
            StepKind.AssertValue => Text.Format(Strings.Step_AssertValue, target, step.Text),
            StepKind.AssertChecked => Text.Format(step.State == false ? Strings.Step_AssertUnchecked : Strings.Step_AssertChecked, target),
            StepKind.Comment when step.Scroll is { } notches => Text.Format(notches < 0 ? Strings.Step_ScrollDown : Strings.Step_ScrollUp, target, Math.Abs(notches)),
            _ => string.IsNullOrWhiteSpace(step.Comment) ? Strings.Step_EmptyComment : step.Comment,
        };
    }

    /// <summary>The control as a person would name it: its label or visible text, else its variable name.</summary>
    public static string Describe(PageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var snapshot = element.Snapshot;
        var name = Clean(snapshot.LabelText) ?? Clean(snapshot.Name);
        return name is null || name == snapshot.ControlType || name == "DataGridView" ? element.VariableName : name;
    }

    /// <summary>Segoe MDL2 Assets glyphs, one per kind of step.</summary>
    public static string Glyph(StepKind kind) => kind switch
    {
        StepKind.SetText or StepKind.ClearText => "",
        StepKind.SelectOption => "",
        StepKind.SetChecked => "",
        StepKind.SelectTab => "",
        StepKind.MenuPath => "",
        StepKind.PressKeys => "",
        StepKind.GridCellClick => "",
        StepKind.SwitchWindow => "",
        StepKind.AssertVisible or StepKind.AssertEnabled or StepKind.AssertText or StepKind.AssertValue or StepKind.AssertChecked => "",
        StepKind.Comment => "",
        _ => "",
    };

    public static string Warning(StepWarning warning) => warning switch
    {
        StepWarning.ElementChanged => Strings.Step_Warning_ElementChanged,
        StepWarning.ElementUnavailable => Strings.Step_Warning_ElementUnavailable,
        StepWarning.StateUnavailable => Strings.Step_Warning_StateUnavailable,
        _ => Strings.Step_Warning_LocatorUnchecked,
    };

    // "row 2, “Status” in loansGrid" for a grid cell; just the target otherwise.
    private static string InGrid(RecordedStep step, string target)
    {
        if (step.RowIndex is not { } row)
        {
            return target;
        }

        var cell = step.ColumnHeader is { Length: > 0 } header ? Text.Format(Strings.Step_Cell, row, header)
            : step.ColumnIndex is { } column ? Text.Format(Strings.Step_CellIndex, row, column)
            : Text.Format(Strings.Step_Row, row);
        return Text.Format(Strings.Step_InGrid, target, cell);
    }

    private static string Checked(RecordedStep step, PageElement? element, string target) => step.State switch
    {
        null => Text.Format(Strings.Step_CheckUnknown, target),
        true when element?.Snapshot.ControlType == "RadioButton" => Text.Format(Strings.Step_SelectRadio, target),
        true => Text.Format(Strings.Step_Tick, target),
        false => Text.Format(Strings.Step_Untick, target),
    };

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxNameLength ? line : string.Concat(line.AsSpan(0, MaxNameLength - 1), "…");
    }
}
