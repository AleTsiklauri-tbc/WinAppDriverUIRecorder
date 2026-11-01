namespace Recorder.Core.Locators;

/// <summary>How a step is reached from the previous one: "//" (any depth) or "/" (direct child).</summary>
public enum Axis
{
    Descendant,
    Child,
}

/// <summary>The only attributes generated locators use. RuntimeId and LocalizedControlType are never generated.</summary>
public enum AttributeName
{
    AutomationId,
    Name,
    ClassName,
}

public abstract record Predicate(AttributeName Attribute);

public sealed record AttrEquals(AttributeName Attribute, string Value) : Predicate(Attribute);

public sealed record AttrStartsWith(AttributeName Attribute, string Prefix) : Predicate(Attribute);

public sealed record AttrContains(AttributeName Attribute, string Part) : Predicate(Attribute);

/// <param name="ControlType">XPath tag (a UIA control type name as WinAppDriver writes it); null renders as "*".</param>
/// <param name="Index">
/// 1-based position among the siblings that match this step's tag and predicates, as in XPath <c>/Pane[2]</c>.
/// Used by the absolute path. Generated locators use <see cref="LocatorSpec.ResultIndex"/> instead.
/// </param>
public sealed record LocatorStep(Axis Axis, string? ControlType, IReadOnlyList<Predicate> Predicates, int? Index = null);

/// <summary>
/// A locator as a small structured tree. It is evaluated against UI Automation directly and only rendered to text at
/// the end; XPath strings are never parsed back.
/// </summary>
/// <param name="ResultIndex">
/// 1-based position in the whole result, in document order: <c>(//Window[…]//Edit[…])[2]</c>. Note that
/// <c>//Edit[2]</c> would mean "every Edit that is the second Edit child of its parent" (Findings F-20).
/// </param>
public sealed record LocatorSpec(IReadOnlyList<LocatorStep> Steps, int? ResultIndex = null)
{
    public LocatorStep Last => Steps[^1];

    public LocatorSpec WithLast(LocatorStep step) => this with { Steps = [.. Steps.Take(Steps.Count - 1), step] };

    public static LocatorSpec ForAttribute(AttributeName attribute, string value) =>
        new([new LocatorStep(Axis.Descendant, null, [new AttrEquals(attribute, value)])]);
}
