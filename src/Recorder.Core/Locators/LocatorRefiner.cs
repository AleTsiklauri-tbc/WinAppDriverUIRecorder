namespace Recorder.Core.Locators;

/// <summary>
/// Makes a candidate that matches several elements unique (spec 7.5): add the control type, then the nearest anchor
/// ancestor, then a second attribute, then a position. Each step is a candidate of its own with an explanation.
/// </summary>
internal static class LocatorRefiner
{
    /// <summary>The refinements tried for <paramref name="draft"/>, with their matches, ending with the first unique one.</summary>
    public static IEnumerable<(CandidateDraft Draft, IReadOnlyList<UiNode> Matches)> Refine(
        CandidateDraft draft, IReadOnlyList<UiNode> matches, LocatorSubject subject, UiNode target, UiTree tree, string? anchor)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(subject);
        var current = draft;
        var currentMatches = matches;
        foreach (var step in new[] { Refinement.ControlType, Refinement.Anchor, Refinement.Attribute, Refinement.Index })
        {
            if (LocatorEngine.IsUnique(currentMatches, target) || currentMatches.Count == 0)
            {
                yield break;
            }

            var next = Apply(step, current, currentMatches, subject, target, anchor);
            if (next is null)
            {
                continue;
            }

            currentMatches = LocatorEvaluator.Evaluate(next.Spec, tree);
            current = next;
            yield return (current, currentMatches);
        }
    }

    private static CandidateDraft? Apply(Refinement step, CandidateDraft current, IReadOnlyList<UiNode> matches, LocatorSubject subject, UiNode target, string? anchor)
    {
        var spec = current.Spec;
        var last = spec.Last;
        var before = LocatorEngine.Distinct(matches);
        var explanation = current.Explanation with { Refinement = step, MatchesBefore = before };
        switch (step)
        {
            case Refinement.ControlType when last.ControlType is null:
                return current with
                {
                    Strategy = LocatorStrategy.XPath,
                    Spec = spec.WithLast(last with { ControlType = subject.Element.ControlType }),
                    Explanation = explanation,
                };

            case Refinement.Anchor when spec.Steps.Count == 1 && anchor is not null:
                var targetStep = last with { Axis = Axis.Descendant, ControlType = subject.Element.ControlType };
                return current with
                {
                    Strategy = LocatorStrategy.XPath,
                    Spec = new LocatorSpec([CandidateGenerator.AnchorStep(anchor), targetStep]),
                    Explanation = explanation with { Kind = current.Kind == LocatorKind.Indexed ? LocatorKind.Indexed : LocatorKind.Anchored, Anchor = anchor },
                };

            case Refinement.Attribute when SecondPredicate(subject, last) is { } extra:
                return current with
                {
                    Strategy = LocatorStrategy.XPath,
                    Spec = spec.WithLast(last with { ControlType = subject.Element.ControlType, Predicates = [.. last.Predicates, extra.Predicate] }),
                    Stability = Weaker(current.Stability, extra.Stability),
                    Notes = [.. current.Notes.Union(extra.Notes)],
                    Explanation = explanation,
                };

            case Refinement.Index:
                var position = IndexOf(matches, target);
                return position < 1 ? null : current with
                {
                    Strategy = LocatorStrategy.XPath,
                    Spec = spec with { ResultIndex = position },
                    Stability = Stability.Low,
                    Notes = [.. current.Notes.Union([LocatorNote.PositionBased])],
                    Explanation = explanation with { Kind = LocatorKind.Indexed },
                };

            default:
                return null;
        }
    }

    // Another attribute of the target that the last step does not test yet.
    private static (Predicate Predicate, Stability Stability, IReadOnlyList<LocatorNote> Notes)? SecondPredicate(LocatorSubject subject, LocatorStep last)
    {
        var used = last.Predicates.Select(p => p.Attribute).ToHashSet();
        foreach (var attribute in new[] { AttributeName.Name, AttributeName.AutomationId, AttributeName.ClassName })
        {
            if (!used.Contains(attribute) && CandidateGenerator.PredicateFor(subject, attribute) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static int IndexOf(IReadOnlyList<UiNode> matches, UiNode target)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            if (ReferenceEquals(matches[i], target) || (target.RuntimeKey.Length > 0 && matches[i].RuntimeKey == target.RuntimeKey))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static Stability Weaker(Stability a, Stability b) => (Stability)Math.Max((int)a, (int)b);
}
