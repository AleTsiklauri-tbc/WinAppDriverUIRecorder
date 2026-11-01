namespace Recorder.Core.Locators;

/// <summary>The WinAppDriver strategies the recorder generates (spec 7.1). "id" (RuntimeId) and "tag name" never are.</summary>
public enum LocatorStrategy
{
    AccessibilityId,
    Name,
    ClassName,
    XPath,
}

public enum Stability
{
    High,
    Medium,
    Low,
}

/// <summary>Where WinAppDriver can find an element: from a session on the app's window, or only from a desktop ("Root") session.</summary>
public enum LocatorScope
{
    Window,
    Desktop,
}

/// <summary>What a candidate is built on. Decides the base score (spec 7.5) and the explanation.</summary>
public enum LocatorKind
{
    /// <summary>The control's AutomationId (its name in the app's code).</summary>
    Id,

    /// <summary>The stable start of an AutomationId that ends in a changing number.</summary>
    IdPrefix,

    /// <summary>The control's visible text.</summary>
    Name,

    /// <summary>A class name that is specific to this kind of control.</summary>
    ClassName,

    /// <summary>Inside the nearest container that has a stable AutomationId.</summary>
    Anchored,

    /// <summary>Position among similar controls.</summary>
    Indexed,

    /// <summary>Full path from the window root. Generated only on request.</summary>
    Absolute,
}

/// <summary>Warnings (each costs 10 points) and notes about a candidate. The app turns them into sentences.</summary>
public enum LocatorNote
{
    /// <summary>The AutomationId is a plain number, which may change between runs.</summary>
    NumericId,

    /// <summary>The AutomationId ends in a number that looks generated at run time.</summary>
    DynamicIdSuffix,

    /// <summary>The text contains a number, date, time or amount.</summary>
    LooksDynamic,

    /// <summary>Non-Latin UI text: breaks if the wording or the app language changes.</summary>
    UiText,

    /// <summary>Counts position among similar controls.</summary>
    PositionBased,

    /// <summary>In a separate pop-up window (for example a drop-down list): WinAppDriver finds it only from a desktop session.</summary>
    PopupWindow,

    /// <summary>Not a warning: a designer default name such as textBox12, stable but not descriptive.</summary>
    DesignerDefaultName,
}

/// <summary>What a refinement added to make a locator unique (spec 7.5).</summary>
public enum Refinement
{
    None,
    ControlType,
    Anchor,
    Attribute,
    Index,
}

/// <param name="Anchor">AutomationId of the container the locator is scoped to, if any.</param>
/// <param name="MatchesBefore">For a refinement: how many controls the locator matched before it.</param>
public sealed record LocatorExplanation(LocatorKind Kind, Refinement Refinement = Refinement.None, string? Anchor = null, int MatchesBefore = 0);

/// <summary>One way to find an element, checked against the live UI.</summary>
/// <param name="RenderedValue">What goes into the locator call: "txtAmount", "Save" or an XPath.</param>
/// <param name="MatchCount">Matches in the tree WinAppDriver searches, counted as WinAppDriver counts them; -1 = not checked.</param>
/// <param name="DistinctMatchCount">Distinct elements among the matches. An open menu lists its items twice (Findings F-21).</param>
/// <param name="IsUnique">Exactly one element matches, and it is the captured one.</param>
public sealed record LocatorCandidate(
    LocatorStrategy Strategy,
    LocatorSpec Spec,
    string RenderedValue,
    LocatorScope Scope,
    Stability Stability,
    int Score,
    int MatchCount,
    int DistinctMatchCount,
    bool IsUnique,
    IReadOnlyList<LocatorNote> Notes,
    LocatorExplanation Explanation)
{
    public LocatorKind Kind => Explanation.Kind;

    public bool IsChecked => MatchCount >= 0;

    public IEnumerable<LocatorNote> Warnings => Notes.Where(LocatorNotes.IsWarning);

    /// <summary>Identifies the locator itself, independent of checks and scores.</summary>
    public string Key => Strategy + ":" + RenderedValue;
}

public static class LocatorNotes
{
    public static bool IsWarning(this LocatorNote note) => note != LocatorNote.DesignerDefaultName;
}

/// <summary>All candidates for one element, best first.</summary>
/// <param name="SelectedIndex">The auto-selected candidate: the top-scoring unique one, else the top-scoring one; -1 if there are none.</param>
/// <param name="IsChecked">False when the uniqueness check ran out of time (spec 7.5 budget) or could not run.</param>
/// <param name="TreeSize">Elements in the searched tree (0 when not checked).</param>
public sealed record LocatorReport(
    IReadOnlyList<LocatorCandidate> Candidates, int SelectedIndex, bool IsChecked, LocatorScope Scope, int TreeSize, TimeSpan Elapsed)
{
    public LocatorCandidate? Best => SelectedIndex >= 0 ? Candidates[SelectedIndex] : null;

    public bool HasUniqueLocator => Candidates.Any(candidate => candidate.IsUnique);

    public int IndexOf(string key)
    {
        for (var i = 0; i < Candidates.Count; i++)
        {
            if (Candidates[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }
}
