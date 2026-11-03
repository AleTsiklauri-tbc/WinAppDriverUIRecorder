using Recorder.Core.Locators;

namespace Recorder.Core.Model;

/// <summary>
/// A captured element: what it is, its variable name and its locators. Owned by <see cref="ElementCatalog"/> on the
/// UI thread; changes go through the catalog, which keeps names unique within the page.
/// </summary>
public sealed class PageElement
{
    internal PageElement(Guid id, PageKey page, ElementSnapshot snapshot, string variableName)
    {
        Id = id;
        Page = page;
        Snapshot = snapshot;
        VariableName = variableName;
    }

    /// <summary>Something about the element changed: name, locators or the chosen locator.</summary>
    public event EventHandler? Changed;

    public Guid Id { get; }

    /// <summary>The page it belongs to. Changes only when a numbered page from a recording finds its window again (D-70).</summary>
    public PageKey Page { get; internal set; }

    /// <summary>The latest capture of this element.</summary>
    public ElementSnapshot Snapshot { get; private set; }

    public string Label => ElementLabel.For(Snapshot);

    public string VariableName { get; private set; }

    /// <summary>The user typed the name; suggestions never replace it.</summary>
    public bool UserRenamed { get; private set; }

    /// <summary>Null until the locators have been worked out.</summary>
    public LocatorReport? Locators { get; private set; }

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>The user picked a locator; it stays selected when the locators are checked again, as long as it is still offered.</summary>
    public bool UserChoseLocator { get; private set; }

    public LocatorCandidate? Selected =>
        Locators is { } report && SelectedIndex >= 0 && SelectedIndex < report.Candidates.Count ? report.Candidates[SelectedIndex] : null;

    internal void SetLocators(LocatorReport report)
    {
        var kept = UserChoseLocator && Selected is { } chosen ? report.IndexOf(chosen.Key) : -1;
        Locators = report;
        SelectedIndex = kept >= 0 ? kept : report.SelectedIndex;
        UserChoseLocator = kept >= 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Choose(int index)
    {
        if (Locators is null || index < 0 || index >= Locators.Candidates.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        SelectedIndex = index;
        UserChoseLocator = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Rename(string name)
    {
        VariableName = name;
        UserRenamed = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a name back exactly as it was (undo), including whether the user had typed it.</summary>
    internal void RestoreName(string name, bool userRenamed)
    {
        VariableName = name;
        UserRenamed = userRenamed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a locator choice back exactly as it was (undo, or a recording opened from a file).</summary>
    internal void RestoreChoice(int index, bool userChose)
    {
        if (Locators is null || index < -1 || index >= Locators.Candidates.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        SelectedIndex = index;
        UserChoseLocator = userChose;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Recaptured(ElementSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class PageModel
{
    private readonly List<PageElement> _elements = [];

    public PageModel(PageKey key) => Key = key;

    public PageKey Key { get; private set; }

    /// <summary>The page class name the user typed (spec 10.2); null for the one derived from the form.</summary>
    public string? ClassName { get; private set; }

    public IReadOnlyList<PageElement> Elements => _elements;

    internal void Rename(string? className) => ClassName = className;

    /// <summary>Takes over another page's key and elements (a numbered page from a recording that found its window, D-70).</summary>
    internal void Absorb(PageModel live)
    {
        Key = live.Key;
        _elements.AddRange(live._elements);
        live._elements.Clear();
        foreach (var element in _elements)
        {
            element.Page = Key;
        }

        ClassName ??= live.ClassName;
    }

    internal void Add(PageElement element) => _elements.Add(element);

    internal void Remove(PageElement element) => _elements.Remove(element);
}
