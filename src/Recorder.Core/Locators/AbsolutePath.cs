namespace Recorder.Core.Locators;

/// <summary>
/// The absolute XPath from the session root to an element (spec 7.2, last bullet): shown only on request in the
/// Inspector's Advanced section and never selected automatically, because any layout change breaks it.
/// </summary>
public static class AbsolutePath
{
    public static LocatorSpec Build(UiNode target, int processId)
    {
        ArgumentNullException.ThrowIfNull(target);
        var chain = target.Ancestors().Reverse().Append(target).ToList();
        var steps = new List<LocatorStep>(chain.Count);
        foreach (var node in chain)
        {
            var predicate = PredicateFor(node, processId);
            var step = new LocatorStep(Axis.Child, node.ControlType, predicate is null ? [] : [predicate]);
            if (node.Parent is { } parent)
            {
                var alike = parent.Children.Where(sibling => LocatorEvaluator.Matches(sibling, step)).ToList();
                if (alike.Count > 1)
                {
                    step = step with { Index = alike.IndexOf(node) + 1 };
                }
            }

            steps.Add(step);
        }

        return new LocatorSpec(steps);
    }

    // The node's own id if the rules accept it, else its text; position decides the rest.
    private static AttrEquals? PredicateFor(UiNode node, int processId)
    {
        var handles = node.Ancestors().Select(a => a.NativeWindowHandle).Prepend(node.NativeWindowHandle).ToList();
        if (IdRules.Evaluate(node.AutomationId, new IdFacts(handles, processId, node.FrameworkId, node.IsNonClient)).IsAccepted)
        {
            return new AttrEquals(AttributeName.AutomationId, node.AutomationId);
        }

        return NameRules.Evaluate(node.Name, node.ControlType).IsAccepted ? new AttrEquals(AttributeName.Name, node.Name) : null;
    }
}
