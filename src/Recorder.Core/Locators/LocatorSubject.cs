using Recorder.Core.Model;

namespace Recorder.Core.Locators;

/// <summary>The attributes of one element that locator rules look at, as WinAppDriver reports them ("" for none).</summary>
public sealed record ElementFacts(string ControlType, string AutomationId, string Name, string ClassName, string? FrameworkId, nint NativeWindowHandle)
{
    public static ElementFacts From(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(node.ControlType, node.AutomationId, node.Name, node.ClassName, node.FrameworkId, node.NativeWindowHandle);
    }

    public static ElementFacts From(AncestorInfo ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        return new(ancestor.ControlType, ancestor.AutomationId ?? string.Empty, ancestor.Name ?? string.Empty, ancestor.ClassName ?? string.Empty,
            ancestor.FrameworkId, ancestor.NativeWindowHandle);
    }

    public static ElementFacts From(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.ControlType, snapshot.AutomationId ?? string.Empty, snapshot.Name ?? string.Empty, snapshot.ClassName ?? string.Empty,
            snapshot.FrameworkId, snapshot.NativeWindowHandle);
    }
}

/// <summary>The element to find, with its ancestors (nearest first, up to the session root).</summary>
public sealed record LocatorSubject(ElementFacts Element, IReadOnlyList<ElementFacts> Ancestors, int ProcessId, LocatorScope Scope)
{
    /// <summary>Inside a title bar (F-22): its AutomationIds are not the ones WinAppDriver sees.</summary>
    public bool IsNonClient => IsNonClientAt(-1);

    /// <summary>The element's own window handle and its ancestors': an id containing one of them is a handle.</summary>
    public IReadOnlyList<nint> WindowHandles => [Element.NativeWindowHandle, .. Ancestors.Select(a => a.NativeWindowHandle)];

    public static LocatorSubject From(UiNode node, int processId, LocatorScope scope)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(ElementFacts.From(node), [.. node.Ancestors().Select(ElementFacts.From)], processId, scope);
    }

    /// <summary>From a snapshot alone, when the tree could not be read in time. Ancestors stop at the session root.</summary>
    public static LocatorSubject From(ElementSnapshot snapshot, LocatorScope scope)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(ElementFacts.From(snapshot), [.. snapshot.Ancestors.Select(ElementFacts.From)], snapshot.ProcessId, scope);
    }

    public IdVerdict IdVerdict() => IdRules.Evaluate(Element.AutomationId, new IdFacts(WindowHandles, ProcessId, Element.FrameworkId, IsNonClient));

    public NameVerdict NameVerdict() => NameRules.Evaluate(Element.Name, Element.ControlType);

    /// <summary>The verdict for an ancestor's AutomationId, by its position in <see cref="Ancestors"/>.</summary>
    public IdVerdict AncestorIdVerdict(int index)
    {
        var ancestor = Ancestors[index];
        return IdRules.Evaluate(ancestor.AutomationId, new IdFacts(WindowHandles, ProcessId, ancestor.FrameworkId, IsNonClientAt(index)));
    }

    /// <summary>Ancestors with a stable, accepted AutomationId, nearest first: possible anchors for scoped locators.</summary>
    public IEnumerable<string> Anchors()
    {
        for (var i = 0; i < Ancestors.Count; i++)
        {
            if (AncestorIdVerdict(i).IsHigh)
            {
                yield return Ancestors[i].AutomationId;
            }
        }
    }

    /// <summary>
    /// The anchor to scope locators to: the nearest one whose id is unique in the window when that can be checked
    /// (a title bar's id repeats in every window), else the nearest one.
    /// </summary>
    public string? ChooseAnchor(Func<string, bool>? isUniqueId = null)
    {
        var anchors = Anchors().ToList();
        return (isUniqueId is null ? null : anchors.FirstOrDefault(isUniqueId)) ?? anchors.FirstOrDefault();
    }

    // An ancestor (or the element, at -1) is non-client when something farther up is a title bar.
    private bool IsNonClientAt(int index)
    {
        for (var i = index + 1; i < Ancestors.Count; i++)
        {
            if (Ancestors[i].ControlType == "TitleBar")
            {
                return true;
            }
        }

        return false;
    }
}
