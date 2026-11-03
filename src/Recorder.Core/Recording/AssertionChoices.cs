using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>One assertion the picker offers, pre-filled with the element's current value or state.</summary>
public sealed record AssertionChoice(StepKind Kind, string? Text, bool? State);

/// <summary>The assertions that make sense for an element (spec 9.6), most specific first: the first one is highlighted.</summary>
public static class AssertionChoices
{
    // Elements whose Name is the text they show. For an input, Name is the label next to it (F-4), not its text.
    private static readonly HashSet<string> ShowsText = new(StringComparer.Ordinal)
    {
        "Text", "Button", "CheckBox", "RadioButton", "TabItem", "MenuItem", "ListItem", "DataItem", "HeaderItem", "Hyperlink",
        "TreeItem", "StatusBar", "Group", "SplitButton", "ToolTip",
    };

    public static IReadOnlyList<AssertionChoice> For(ElementSnapshot element, ElementState? state)
    {
        ArgumentNullException.ThrowIfNull(element);
        var choices = new List<AssertionChoice>();
        if (state?.IsChecked is { } isChecked)
        {
            choices.Add(new AssertionChoice(StepKind.AssertChecked, null, isChecked));
        }

        // A password field's value is never read, so it can never be asserted.
        var value = element.IsPassword ? null : state?.Value ?? element.Value;
        if (value is not null && element.SupportedPatterns.Contains("Value"))
        {
            choices.Add(new AssertionChoice(StepKind.AssertValue, value, null));
        }

        var name = state?.Name ?? element.Name;
        if (!string.IsNullOrWhiteSpace(name) && ShowsText.Contains(element.ControlType))
        {
            choices.Add(new AssertionChoice(StepKind.AssertText, name, null));
        }

        choices.Add(new AssertionChoice(StepKind.AssertEnabled, null, state?.IsEnabled ?? element.IsEnabled));
        choices.Add(new AssertionChoice(StepKind.AssertVisible, null, null));
        return choices;
    }
}
