using System.Globalization;
using System.Text.RegularExpressions;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// What a clicked element is, for the step rules: part of a combo box, a grid cell, a menu item. Decided from the
/// snapshot alone (control types and the ancestor chain), so the session knows which ancestor to read in full.
/// </summary>
public static partial class StepTargets
{
    /// <summary>
    /// The ancestor that a step on <paramref name="element"/> really targets, as an index into its ancestors, or null for the
    /// element itself: the combo box of a drop-down list item or of its own button or text part, the grid of a cell or row,
    /// the first item of a menu path (F-26).
    /// </summary>
    public static int? OwnerIndex(ElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ancestors = element.Ancestors;
        if (element.ControlType == "ListItem")
        {
            for (var i = 0; i < Math.Min(2, ancestors.Count); i++)
            {
                if (ancestors[i].ControlType == "ComboBox")
                {
                    return i;
                }
            }
        }

        if (ancestors.Count > 0 && ancestors[0].ControlType == "ComboBox" && element.ControlType is "Edit" or "Button")
        {
            return 0;
        }

        if (GridIndex(element) is { } grid)
        {
            return grid;
        }

        if (element.ControlType == "MenuItem")
        {
            var top = -1;
            for (var i = 0; i < ancestors.Count && ancestors[i].ControlType == "MenuItem"; i++)
            {
                top = i;
            }

            return top >= 0 ? top : null;
        }

        return null;
    }

    /// <summary>A cell (it reports a row and column) or a row of a grid; header cells and scroll bars are not.</summary>
    public static bool IsGridPart(ElementSnapshot element) => GridIndex(element) is not null;

    /// <summary>A control whose value the recorder records on focus-out (spec 9.3): a text box, combo box or document with a value.</summary>
    public static bool IsValueInput(ElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.ControlType is "Edit" or "ComboBox" or "Document" or "Spinner" && element.SupportedPatterns.Contains("Value") && !IsGridPart(element);
    }

    /// <summary>The row of a grid part: the cell's row, or the trailing number of a row's name ("Row 2", F-6).</summary>
    public static int? RowOf(ElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Cell is { } cell)
        {
            return cell.Row;
        }

        var match = TrailingNumber().Match(element.Name ?? string.Empty);
        return match.Success && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? row : null;
    }

    /// <summary>The menu path of a menu item, from the menu bar down (F-26): "File", "Export", "PDF".</summary>
    public static IReadOnlyList<string> MenuPathOf(ElementSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var path = new List<string> { item.Name ?? string.Empty };
        foreach (var ancestor in item.Ancestors.TakeWhile(a => a.ControlType == "MenuItem"))
        {
            path.Insert(0, ancestor.Name ?? string.Empty);
        }

        return path;
    }

    private static int? GridIndex(ElementSnapshot element)
    {
        var ancestors = element.Ancestors;
        if (element.ControlType is "Header" or "HeaderItem" or "ScrollBar" or "Thumb")
        {
            return null;
        }

        for (var i = 0; i < Math.Min(2, ancestors.Count); i++)
        {
            if (ancestors[i].ControlType is "Table" or "DataGrid")
            {
                // A cell sits in a row; a row (or a row header) sits directly in the grid.
                var isRow = i == 0 && element.ControlType is "Custom" or "DataItem";
                return element.Cell is not null || isRow ? i : null;
            }

            if (ancestors[i].ControlType is "Header" or "HeaderItem")
            {
                return null;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();
}
