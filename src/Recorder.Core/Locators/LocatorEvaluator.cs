namespace Recorder.Core.Locators;

/// <summary>
/// Evaluates a <see cref="LocatorSpec"/> against a fetched tree with exactly the XPath 1.0 meaning of what
/// <see cref="XPathRenderer"/> writes. This is not an XPath engine: it evaluates only the generated AST.
/// The tree is WinAppDriver's document for one session: a virtual document node whose only child is the root, so a
/// search includes the root itself (Findings F-20).
/// </summary>
public static class LocatorEvaluator
{
    /// <summary>The matching nodes in document order, as WinAppDriver would return them.</summary>
    public static IReadOnlyList<UiNode> Evaluate(LocatorSpec spec, UiTree tree)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(tree);
        if (spec.Steps.Count == 0)
        {
            return [];
        }

        // The first step starts at the document node: "/X" can only be the root, "//X" can be any node.
        var current = spec.Steps[0].Axis == Axis.Child ? [tree.Root] : (IReadOnlyList<UiNode>)tree.Nodes;
        current = Filter(current, spec.Steps[0]);
        for (var i = 1; i < spec.Steps.Count && current.Count > 0; i++)
        {
            var step = spec.Steps[i];
            var reached = step.Axis == Axis.Child ? ChildrenOf(current) : DescendantsOf(current, tree);
            current = Filter(reached, step);
        }

        if (spec.ResultIndex is { } position)
        {
            return position >= 1 && position <= current.Count ? [current[position - 1]] : [];
        }

        return current;
    }

    public static bool Matches(UiNode node, LocatorStep step)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(step);
        if (step.ControlType is { } tag && !string.Equals(node.ControlType, tag == "Unknown" ? "Custom" : tag, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var predicate in step.Predicates)
        {
            var value = node.Attribute(predicate.Attribute);
            var holds = predicate switch
            {
                AttrEquals equals => string.Equals(value, equals.Value, StringComparison.Ordinal),
                AttrStartsWith startsWith => value.StartsWith(startsWith.Prefix, StringComparison.Ordinal),
                AttrContains contains => value.Contains(contains.Part, StringComparison.Ordinal),
                _ => throw new NotSupportedException("Unknown predicate " + predicate.GetType().Name),
            };
            if (!holds)
            {
                return false;
            }
        }

        return true;
    }

    // Node test and predicates, then the positional predicate, which counts per parent: in "//Edit[2]" each
    // context node contributes its own second Edit child.
    private static List<UiNode> Filter(IReadOnlyList<UiNode> nodes, LocatorStep step)
    {
        var matching = nodes.Where(node => Matches(node, step)).ToList();
        if (step.Index is not { } index)
        {
            return matching;
        }

        return matching
            .Where(node => PositionAmongMatchingSiblings(node, step) == index)
            .ToList();
    }

    private static int PositionAmongMatchingSiblings(UiNode node, LocatorStep step)
    {
        if (node.Parent is null)
        {
            return 1; // the root is the document node's only child
        }

        var position = 0;
        foreach (var sibling in node.Parent.Children)
        {
            if (Matches(sibling, step))
            {
                position++;
            }

            if (ReferenceEquals(sibling, node))
            {
                return position;
            }
        }

        return 0;
    }

    private static List<UiNode> ChildrenOf(IReadOnlyList<UiNode> context) =>
        context.SelectMany(node => node.Children).Distinct().OrderBy(node => node.Order).ToList();

    // Every node strictly below some context node, each once, in document order.
    private static List<UiNode> DescendantsOf(IReadOnlyList<UiNode> context, UiTree tree)
    {
        var contextSet = context.ToHashSet();
        return tree.Nodes.Where(node => node.Ancestors().Any(contextSet.Contains)).ToList();
    }
}
