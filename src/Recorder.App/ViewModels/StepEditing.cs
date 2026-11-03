using System.Globalization;
using Recorder.App.Resources;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

/// <summary>What can be edited in a step (Enter in the Steps list), and the edited step. Steps without a value edit their comment.</summary>
internal static class StepEditing
{
    private static readonly string[] MenuSeparators = ["›", ">"];

    public static string TextLabel(RecordedStep step) => step.Kind switch
    {
        StepKind.SetText when step.TestDataKey is not null => Strings.Step_EditTestData,
        StepKind.SetText or StepKind.SelectOption or StepKind.SelectTab or StepKind.PressKeys or StepKind.AssertText or StepKind.AssertValue => Strings.Step_EditText,
        StepKind.MenuPath => Strings.Step_EditMenu,
        StepKind.GridCellClick or StepKind.DoubleClick or StepKind.RightClick when step.RowIndex is not null => Strings.Step_EditColumn,
        _ => Strings.Step_EditComment,
    };

    public static string? StateLabel(RecordedStep step) => step.Kind switch
    {
        StepKind.SetChecked or StepKind.AssertChecked => Strings.Step_EditState,
        StepKind.AssertEnabled => Strings.Step_EditEnabled,
        _ => null,
    };

    public static string? Text(RecordedStep step) => Field(step) switch
    {
        EditField.TestDataKey => step.TestDataKey,
        EditField.Text => step.Text,
        EditField.Menu => string.Join(" › ", step.MenuItems),
        EditField.Column => step.ColumnHeader,
        _ => step.Comment,
    };

    /// <summary>The edited step; null when nothing changed or the row is not a whole number of zero or more.</summary>
    public static RecordedStep? Apply(RecordedStep step, string? text, bool state, string? row)
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var edited = Field(step) switch
        {
            EditField.TestDataKey => step with { TestDataKey = value ?? step.TestDataKey },
            EditField.Text => step with { Text = text },
            EditField.Menu => step with
            {
                MenuItems = value is null ? step.MenuItems : [.. value.Split(MenuSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            },
            EditField.Column => step with { ColumnHeader = value },
            _ => step with { Comment = value },
        };

        if (StateLabel(step) is not null)
        {
            edited = edited with { State = state, Warnings = [.. edited.Warnings.Where(w => w != StepWarning.StateUnavailable)] };
        }

        if (step.RowIndex is not null)
        {
            if (!int.TryParse(row, NumberStyles.None, CultureInfo.CurrentCulture, out var rowIndex))
            {
                return null;
            }

            edited = edited with { RowIndex = rowIndex };
        }

        return Same(edited, step) ? null : edited;
    }

    private static bool Same(RecordedStep a, RecordedStep b) =>
        a == b || (a with { MenuItems = b.MenuItems, Warnings = b.Warnings } == b && a.MenuItems.SequenceEqual(b.MenuItems) && a.Warnings.SequenceEqual(b.Warnings));

    private static EditField Field(RecordedStep step) => step.Kind switch
    {
        StepKind.SetText when step.TestDataKey is not null => EditField.TestDataKey,
        StepKind.SetText or StepKind.SelectOption or StepKind.SelectTab or StepKind.PressKeys or StepKind.AssertText or StepKind.AssertValue => EditField.Text,
        StepKind.MenuPath => EditField.Menu,
        StepKind.GridCellClick or StepKind.DoubleClick or StepKind.RightClick when step.RowIndex is not null => EditField.Column,
        _ => EditField.Comment,
    };

    private enum EditField
    {
        Comment,
        Text,
        TestDataKey,
        Menu,
        Column,
    }
}
