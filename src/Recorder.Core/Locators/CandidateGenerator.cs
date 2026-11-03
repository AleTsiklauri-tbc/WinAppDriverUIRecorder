namespace Recorder.Core.Locators;

/// <summary>A candidate before it is checked against the tree.</summary>
internal sealed record CandidateDraft(LocatorStrategy Strategy, LocatorSpec Spec, Stability Stability, IReadOnlyList<LocatorNote> Notes, LocatorExplanation Explanation)
{
    public string RenderedValue => Strategy == LocatorStrategy.XPath
        ? XPathRenderer.Render(Spec)
        : ((AttrEquals)Spec.Steps[0].Predicates[0]).Value;

    public string Key => Strategy + ":" + RenderedValue;

    public LocatorKind Kind => Explanation.Kind;
}

/// <summary>Builds the initial candidates of spec 7.2, in spec order. Pure: no UI Automation.</summary>
internal static class CandidateGenerator
{
    public static List<CandidateDraft> Generate(LocatorSubject subject, string? anchor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var element = subject.Element;
        var drafts = new List<CandidateDraft>(6);
        var id = subject.IdVerdict();
        var name = subject.NameVerdict();

        if (id.IsAccepted)
        {
            drafts.Add(new(LocatorStrategy.AccessibilityId, LocatorSpec.ForAttribute(AttributeName.AutomationId, element.AutomationId), id.Stability, id.Notes,
                new LocatorExplanation(LocatorKind.Id)));
            if (id.StablePrefix is { } prefix)
            {
                var step = new LocatorStep(Axis.Descendant, element.ControlType, [new AttrStartsWith(AttributeName.AutomationId, prefix)]);
                drafts.Add(new(LocatorStrategy.XPath, new LocatorSpec([step]), Stability.Medium, [], new LocatorExplanation(LocatorKind.IdPrefix)));
            }
        }

        if (name.IsAccepted)
        {
            drafts.Add(new(LocatorStrategy.Name, LocatorSpec.ForAttribute(AttributeName.Name, element.Name), name.Stability, name.Notes,
                new LocatorExplanation(LocatorKind.Name)));
        }

        if (ClassNameRules.CanBeLocator(element.ClassName, element.FrameworkId))
        {
            drafts.Add(new(LocatorStrategy.ClassName, LocatorSpec.ForAttribute(AttributeName.ClassName, element.ClassName), Stability.Medium, [],
                new LocatorExplanation(LocatorKind.ClassName)));
        }

        // A stable id of its own makes an anchored variant redundant.
        if (!id.IsHigh && anchor is not null && BestPredicate(subject, exclude: null) is { } best)
        {
            var spec = new LocatorSpec([AnchorStep(anchor), new LocatorStep(Axis.Descendant, element.ControlType, [best.Predicate])]);
            drafts.Add(new(LocatorStrategy.XPath, spec, best.Stability, best.Notes, new LocatorExplanation(LocatorKind.Anchored, Anchor: anchor)));
        }

        if (subject.Scope == LocatorScope.Desktop)
        {
            drafts = [.. drafts.Select(d => d with { Notes = [.. d.Notes, LocatorNote.PopupWindow] })];
        }

        return drafts;
    }

    public static LocatorStep AnchorStep(string anchorId) =>
        new(Axis.Descendant, null, [new AttrEquals(AttributeName.AutomationId, anchorId)]);

    /// <summary>
    /// The base of the position-based fallback: the nearest anchor (or the window) and the target by control type and
    /// stable class prefix, e.g. <c>//Window[@AutomationId='frmLoan']//Edit[starts-with(@ClassName,'WindowsForms10.EDIT')]</c>.
    /// </summary>
    public static LocatorSpec IndexBase(LocatorSubject subject, string? anchor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var element = subject.Element;
        IReadOnlyList<Predicate> predicates = ClassNameRules.StablePrefix(element.ClassName) is { } prefix
            ? [new AttrStartsWith(AttributeName.ClassName, prefix)]
            : [];
        var target = new LocatorStep(Axis.Descendant, element.ControlType, predicates);
        var scope = anchor is not null ? AnchorStep(anchor) : WindowStep(subject);
        return new LocatorSpec(scope is null ? [target] : [scope, target]);
    }

    /// <summary>
    /// The target's most specific usable attribute other than <paramref name="exclude"/>: Name, then AutomationId
    /// (or the stable start of it), then its class.
    /// </summary>
    public static (Predicate Predicate, Stability Stability, IReadOnlyList<LocatorNote> Notes)? BestPredicate(LocatorSubject subject, AttributeName? exclude)
    {
        foreach (var attribute in new[] { AttributeName.Name, AttributeName.AutomationId, AttributeName.ClassName })
        {
            if (attribute != exclude && PredicateFor(subject, attribute) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A predicate on one attribute of the target, if the rules accept that attribute.</summary>
    public static (Predicate Predicate, Stability Stability, IReadOnlyList<LocatorNote> Notes)? PredicateFor(LocatorSubject subject, AttributeName attribute)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var element = subject.Element;
        switch (attribute)
        {
            case AttributeName.Name:
                var name = subject.NameVerdict();
                return name.IsAccepted ? (new AttrEquals(AttributeName.Name, element.Name), name.Stability, name.Notes) : null;

            case AttributeName.AutomationId:
                var id = subject.IdVerdict();
                if (!id.IsAccepted)
                {
                    return null;
                }

                return id.StablePrefix is { } idPrefix
                    ? (new AttrStartsWith(AttributeName.AutomationId, idPrefix), Stability.Medium, [])
                    : (new AttrEquals(AttributeName.AutomationId, element.AutomationId), id.Stability, id.Notes);

            default:
                if (ClassNameRules.StablePrefix(element.ClassName) is { } classPrefix)
                {
                    return (new AttrStartsWith(AttributeName.ClassName, classPrefix), Stability.Low, []);
                }

                return ClassNameRules.CanBeLocator(element.ClassName, element.FrameworkId)
                    ? (new AttrEquals(AttributeName.ClassName, element.ClassName), Stability.Medium, [])
                    : null;
        }
    }

    // The nearest window with a usable AutomationId. Never a window by its title: in LMS it shows the open customer (D-70);
    // without such a window the position counts across the whole window the session is attached to.
    private static LocatorStep? WindowStep(LocatorSubject subject)
    {
        for (var i = 0; i < subject.Ancestors.Count; i++)
        {
            if (subject.Ancestors[i].ControlType == "Window" && subject.AncestorIdVerdict(i).IsAccepted)
            {
                return new LocatorStep(Axis.Descendant, "Window", [new AttrEquals(AttributeName.AutomationId, subject.Ancestors[i].AutomationId)]);
            }
        }

        return null;
    }
}
