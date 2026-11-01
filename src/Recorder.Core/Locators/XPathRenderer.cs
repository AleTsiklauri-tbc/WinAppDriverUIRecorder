using System.Globalization;
using System.Text;

namespace Recorder.Core.Locators;

public static class XPathRenderer
{
    public static string Render(LocatorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Steps.Count == 0)
        {
            throw new ArgumentException("A locator needs at least one step.", nameof(spec));
        }

        var builder = new StringBuilder();
        foreach (var step in spec.Steps)
        {
            builder.Append(step.Axis == Axis.Descendant ? "//" : "/");
            builder.Append(Tag(step.ControlType));
            foreach (var predicate in step.Predicates)
            {
                builder.Append('[').Append(Render(predicate)).Append(']');
            }

            if (step.Index is { } index)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(index, 1, nameof(spec));
                builder.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
        }

        if (spec.ResultIndex is { } position)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(position, 1, nameof(spec));
            builder.Insert(0, '(').Append(")[").Append(position.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        return builder.ToString();
    }

    // WinAppDriver writes an element without a control type as <Custom> (Findings F-6); the tag is never "Unknown".
    private static string Tag(string? controlType) =>
        string.IsNullOrEmpty(controlType) ? "*" : controlType == "Unknown" ? "Custom" : controlType;

    private static string Render(Predicate predicate) => predicate switch
    {
        AttrEquals equals => $"@{equals.Attribute}={XPathLiteral.Quote(equals.Value)}",
        AttrStartsWith startsWith => $"starts-with(@{startsWith.Attribute},{XPathLiteral.Quote(startsWith.Prefix)})",
        AttrContains contains => $"contains(@{contains.Attribute},{XPathLiteral.Quote(contains.Part)})",
        _ => throw new NotSupportedException("Unknown predicate " + predicate.GetType().Name),
    };
}

/// <summary>XPath 1.0 string literals: no escape characters exist, so quotes are chosen or concatenated.</summary>
public static class XPathLiteral
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\'', StringComparison.Ordinal))
        {
            return "'" + value + "'";
        }

        if (!value.Contains('"', StringComparison.Ordinal))
        {
            return "\"" + value + "\"";
        }

        // Both kinds of quote: concat('It', "'", 's "x"').
        var parts = value.Split('\'');
        var pieces = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                pieces.Add("'" + parts[i] + "'");
            }

            if (i < parts.Length - 1)
            {
                pieces.Add("\"'\"");
            }
        }

        return "concat(" + string.Join(",", pieces) + ")";
    }
}
