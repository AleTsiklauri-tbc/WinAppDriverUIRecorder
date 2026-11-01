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

    public PageKey Page { get; }

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

    public PageKey Key { get; }

    public IReadOnlyList<PageElement> Elements => _elements;

    internal void Add(PageElement element) => _elements.Add(element);

    internal void Remove(PageElement element) => _elements.Remove(element);
}
