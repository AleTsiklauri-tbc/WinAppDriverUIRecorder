using Recorder.Core.Locators;
using Recorder.Core.Naming;

namespace Recorder.Core.Model;

/// <summary>
/// Captured elements grouped by page (spec 6). An element is kept once per page: captured twice in a session (same
/// runtime id), or captured again after a restart and found by the same locator. Not thread-safe: owned by the UI thread.
/// </summary>
public sealed class ElementCatalog
{
    private readonly List<PageModel> _pages = [];
    private readonly Dictionary<string, PageElement> _bySessionKey = new(StringComparer.Ordinal);
    private readonly NamingOptions _naming;

    public ElementCatalog()
        : this(NamingOptions.Default)
    {
    }

    public ElementCatalog(NamingOptions naming) => _naming = naming;

    public event EventHandler<PageElement>? ElementAdded;

    /// <summary>An element turned out to be one already listed and was merged into it.</summary>
    public event EventHandler<PageElement>? ElementRemoved;

    public IReadOnlyList<PageModel> Pages => _pages;

    public int Count => _pages.Sum(page => page.Elements.Count);

    public bool Contains(string sessionKey) => _bySessionKey.ContainsKey(sessionKey);

    public PageElement? Find(string sessionKey) => _bySessionKey.GetValueOrDefault(sessionKey);

    public (PageElement Element, bool IsNew) Add(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_bySessionKey.TryGetValue(snapshot.SessionKey, out var existing))
        {
            return (existing, false);
        }

        var key = PageKey.For(snapshot);
        var page = _pages.Find(p => p.Key == key);
        if (page is null)
        {
            page = new PageModel(key);
            _pages.Add(page);
        }

        var name = VariableNamer.Suggest(snapshot, page.Elements.Select(e => e.VariableName), _naming);
        var element = new PageElement(Guid.NewGuid(), key, snapshot, name);
        page.Add(element);
        _bySessionKey.Add(snapshot.SessionKey, element);
        ElementAdded?.Invoke(this, element);
        return (element, true);
    }

    /// <summary>
    /// Stores an element's checked locators. If another element on the page already uses the same unique locator, they
    /// are the same control (captured again, for example after the app restarted): the earlier entry is kept and
    /// returned, updated with the new capture, and this one is removed.
    /// </summary>
    public PageElement AttachLocators(PageElement element, LocatorReport report)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(report);
        element.SetLocators(report);
        var page = PageOf(element);
        if (page is null || element.Selected is not { IsUnique: true } selected)
        {
            return element;
        }

        var twin = page.Elements.FirstOrDefault(other => other != element && other.Selected is { IsUnique: true } s && s.Key == selected.Key);
        if (twin is null)
        {
            return element;
        }

        page.Remove(element);
        _bySessionKey[element.Snapshot.SessionKey] = twin;
        twin.Recaptured(element.Snapshot);
        twin.SetLocators(report);
        ElementRemoved?.Invoke(this, element);
        return twin;
    }

    public void ChooseLocator(PageElement element, int index)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (PageOf(element) is null)
        {
            throw new ArgumentException("The element is not in this catalog.", nameof(element));
        }

        element.Choose(index);
    }

    /// <summary>Renames an element. The typed name is reduced to a valid identifier and kept unique within the page; false if nothing usable was typed.</summary>
    public bool Rename(PageElement element, string typed)
    {
        ArgumentNullException.ThrowIfNull(element);
        var name = VariableNamer.Clean(typed);
        if (name is null)
        {
            return false;
        }

        var taken = PageOf(element)?.Elements.Where(e => e != element).Select(e => e.VariableName).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var unique = name;
        for (var n = 2; taken.Contains(unique); n++)
        {
            unique = name + n;
        }

        if (unique != element.VariableName || !element.UserRenamed)
        {
            element.Rename(unique);
        }

        return true;
    }

    private PageModel? PageOf(PageElement element) => _pages.Find(page => page.Elements.Contains(element));
}