namespace Recorder.Core.Locators;

/// <summary>
/// Generates, checks, refines and ranks locator candidates for one element (spec 7.2–7.5). Pure: it works on a tree
/// that was already fetched, so it is fast and fully testable without UI Automation.
/// </summary>
public static class LocatorEngine
{
    /// <summary>Checks every candidate against <paramref name="tree"/>, where <paramref name="target"/> is the captured element.</summary>
    public static LocatorReport Analyze(LocatorSubject subject, UiTree tree, UiNode target, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(target);
        var results = new List<(CandidateDraft Draft, IReadOnlyList<UiNode> Matches)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anchor = subject.ChooseAnchor(id => IsUniqueId(id, tree));

        void Add(CandidateDraft draft, IReadOnlyList<UiNode> matches)
        {
            if (seen.Add(draft.Key))
            {
                results.Add((draft, matches));
            }
        }

        foreach (var draft in CandidateGenerator.Generate(subject, anchor))
        {
            var matches = LocatorEvaluator.Evaluate(draft.Spec, tree);

            // A class name is offered only when it alone is unique (spec 7.2).
            if (draft.Kind == LocatorKind.ClassName && !IsUnique(matches, target))
            {
                continue;
            }

            Add(draft, matches);
            foreach (var (refined, refinedMatches) in LocatorRefiner.Refine(draft, matches, subject, target, tree, anchor))
            {
                Add(refined, refinedMatches);
            }
        }

        if (PositionFallback(subject, tree, target, anchor) is { } fallback)
        {
            Add(fallback.Draft, fallback.Matches);
        }

        var candidates = results.Select(r => ToCandidate(r.Draft, r.Matches, target, subject.Scope)).ToList();
        return Rank(candidates, isChecked: true, subject.Scope, tree.Count, elapsed);
    }

    /// <summary>Candidates from the captured snapshot alone, when the tree could not be read within the budget.</summary>
    public static LocatorReport Unchecked(LocatorSubject subject, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var candidates = CandidateGenerator.Generate(subject, subject.ChooseAnchor())
            .GroupBy(draft => draft.Key)
            .Select(group => group.First())
            .Select(draft => Candidate(draft, matchCount: -1, distinct: -1, isUnique: false, subject.Scope))
            .ToList();
        return Rank(candidates, isChecked: false, subject.Scope, treeSize: 0, elapsed);
    }

    public static int Score(LocatorKind kind, Stability stability, int warnings, bool isChecked, bool isUnique)
    {
        var score = kind switch
        {
            LocatorKind.Id or LocatorKind.IdPrefix => 100,
            LocatorKind.Name => 70,
            LocatorKind.ClassName => 50,
            LocatorKind.Anchored => 45,
            LocatorKind.Indexed => 20,
            _ => 0,
        };
        if (isChecked)
        {
            score += isUnique ? 50 : -80;
        }

        return score - (10 * warnings) - (stability == Stability.Low ? 30 : 0);
    }

    /// <summary>Exactly one element matches, and it is the target. The same element may be listed twice (F-21).</summary>
    public static bool IsUnique(IReadOnlyList<UiNode> matches, UiNode target)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(target);
        if (matches.Count == 0)
        {
            return false;
        }

        return target.RuntimeKey.Length > 0
            ? matches.All(match => match.RuntimeKey == target.RuntimeKey)
            : matches.Count == 1 && ReferenceEquals(matches[0], target);
    }

    public static int Distinct(IReadOnlyList<UiNode> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches.Select(match => match.RuntimeKey.Length > 0 ? match.RuntimeKey : "#" + match.Order).Distinct(StringComparer.Ordinal).Count();
    }

    private static bool IsUniqueId(string automationId, UiTree tree) =>
        Distinct(LocatorEvaluator.Evaluate(LocatorSpec.ForAttribute(AttributeName.AutomationId, automationId), tree)) == 1;

    // The last resort that always exists: position among similar controls inside the anchor or window.
    private static (CandidateDraft Draft, IReadOnlyList<UiNode> Matches)? PositionFallback(LocatorSubject subject, UiTree tree, UiNode target, string? anchorId)
    {
        var baseSpec = CandidateGenerator.IndexBase(subject, anchorId);
        var all = LocatorEvaluator.Evaluate(baseSpec, tree);
        var position = 0;
        for (var i = 0; i < all.Count && position == 0; i++)
        {
            if (ReferenceEquals(all[i], target) || (target.RuntimeKey.Length > 0 && all[i].RuntimeKey == target.RuntimeKey))
            {
                position = i + 1;
            }
        }

        if (position == 0)
        {
            return null;
        }

        IReadOnlyList<LocatorNote> notes = subject.Scope == LocatorScope.Desktop
            ? [LocatorNote.PositionBased, LocatorNote.PopupWindow]
            : [LocatorNote.PositionBased];
        var anchor = baseSpec.Steps.Count > 1 && baseSpec.Steps[0].ControlType is null
            ? ((AttrEquals)baseSpec.Steps[0].Predicates[0]).Value
            : null;
        var draft = new CandidateDraft(LocatorStrategy.XPath, baseSpec with { ResultIndex = position }, Stability.Low, notes,
            new LocatorExplanation(LocatorKind.Indexed, Anchor: anchor));
        return (draft, LocatorEvaluator.Evaluate(draft.Spec, tree));
    }

    private static LocatorCandidate ToCandidate(CandidateDraft draft, IReadOnlyList<UiNode> matches, UiNode target, LocatorScope scope) =>
        Candidate(draft, matches.Count, Distinct(matches), IsUnique(matches, target), scope);

    private static LocatorCandidate Candidate(CandidateDraft draft, int matchCount, int distinct, bool isUnique, LocatorScope scope)
    {
        var warnings = draft.Notes.Count(LocatorNotes.IsWarning);
        var score = Score(draft.Kind, draft.Stability, warnings, isChecked: matchCount >= 0, isUnique);
        return new LocatorCandidate(draft.Strategy, draft.Spec, draft.RenderedValue, scope, draft.Stability, score, matchCount, distinct, isUnique,
            draft.Notes, draft.Explanation);
    }

    private static LocatorReport Rank(List<LocatorCandidate> candidates, bool isChecked, LocatorScope scope, int treeSize, TimeSpan elapsed)
    {
        var ranked = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Strategy)
            .ThenBy(candidate => candidate.RenderedValue.Length)
            .ToList();
        var selected = ranked.FindIndex(candidate => candidate.IsUnique);
        if (selected < 0 && ranked.Count > 0)
        {
            selected = 0;
        }

        return new LocatorReport(ranked, selected, isChecked, scope, treeSize, elapsed);
    }
}
