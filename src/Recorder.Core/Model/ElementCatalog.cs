using Recorder.Core.Locators;
using Recorder.Core.Naming;

namespace Recorder.Core.Model;

/// <summary>An element as stored in a recording file.</summary>
public sealed record SavedElement(
    Guid Id, PageKey Page, ElementSnapshot Snapshot, string VariableName, bool UserRenamed, LocatorReport? Locators, int SelectedIndex, bool UserChoseLocator);

/// <summary>
/// Captured elements grouped by page (spec 6). An element is kept once per page: captured twice in a session (same
/// runtime id), or captured again after a restart and found by the same locator. Not thread-safe: owned by the UI thread.
/// </summary>
public sealed class ElementCatalog
{
    private readonly List<PageModel> _pages = [];
    private readonly Dictionary<string, PageElement> _bySessionKey = new(StringComparer.Ordinal);

    public ElementCatalog()
        : this(NamingOptions.Default)
    {
    }

    public ElementCatalog(NamingOptions naming) => Naming = naming;

    /// <summary>How elements added from now on are named: the active code profile's conventions. Existing names stay.</summary>
    public NamingOptions Naming { get; set; }

    public event EventHandler<PageElement>? ElementAdded;

    /// <summary>An element turned out to be one already listed and was merged into it.</summary>
    public event EventHandler<PageElement>? ElementRemoved;

    /// <summary>Everything was replaced (a recording was opened), or regrouped (a numbered page found its window, D-70).</summary>
    public event EventHandler? Reset;

    public IReadOnlyList<PageModel> Pages => _pages;

    public int Count => _pages.Sum(page => page.Elements.Count);

    public IEnumerable<PageElement> Elements => _pages.SelectMany(page => page.Elements);

    public bool Contains(string sessionKey) => _bySessionKey.ContainsKey(sessionKey);

    public PageElement? Find(string sessionKey) => _bySessionKey.GetValueOrDefault(sessionKey);

    public PageElement? FindById(Guid id) => Elements.FirstOrDefault(element => element.Id == id);

    /// <summary>
    /// Replaces every element with ones read from a recording. They have no live element until captured again: a new
    /// capture with the same unique locator is merged into them (<see cref="AttachLocators"/>).
    /// </summary>
    public void Load(IEnumerable<SavedElement> elements, IReadOnlyDictionary<PageKey, string>? pageClassNames = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _pages.Clear();
        _bySessionKey.Clear();
        foreach (var saved in elements)
        {
            var page = _pages.Find(p => p.Key == saved.Page);
            if (page is null)
            {
                page = new PageModel(saved.Page);
                page.Rename(pageClassNames?.GetValueOrDefault(saved.Page));
                _pages.Add(page);
            }

            var element = new PageElement(saved.Id, saved.Page, saved.Snapshot, saved.VariableName);
            if (saved.UserRenamed)
            {
                element.RestoreName(saved.VariableName, userRenamed: true);
            }

            if (saved.Locators is { } report)
            {
                element.SetLocators(report);
                element.RestoreChoice(saved.SelectedIndex, saved.UserChoseLocator);
            }

            page.Add(element);
        }

        Reset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A page's class name changed (spec 10.2).</summary>
    public event EventHandler<PageModel>? PageRenamed;

    /// <summary>
    /// Sets a page's class name; null goes back to the one derived from its form. The typed name is made a valid class name
    /// (<see cref="VariableNamer.ClassName"/>); false if nothing usable was typed.
    /// </summary>
    public bool RenamePage(PageModel page, string? className)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!_pages.Contains(page))
        {
            throw new ArgumentException("The page is not in this catalog.", nameof(page));
        }

        var name = className is null ? null : VariableNamer.ClassName(className);
        if (className is not null && name is null)
        {
            return false;
        }

        page.Rename(name);
        PageRenamed?.Invoke(this, page);
        return true;
    }

    /// <summary>Puts an element's name back exactly as it was (undo).</summary>
    public void RestoreName(PageElement element, string name, bool userRenamed)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Member(element).RestoreName(name, userRenamed);
    }

    /// <summary>Puts an element's locator choice back exactly as it was (undo).</summary>
    public void RestoreChoice(PageElement element, int index, bool userChose) => Member(element).RestoreChoice(index, userChose);

    private PageElement Member(PageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return PageOf(element) is null ? throw new ArgumentException("The element is not in this catalog.", nameof(element)) : element;
    }

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

        var name = VariableNamer.Suggest(snapshot, page.Elements.Select(e => e.VariableName), Naming);
        var element = new PageElement(Guid.NewGuid(), key, snapshot, name);
        page.Add(element);
        _bySessionKey.Add(snapshot.SessionKey, element);
        ElementAdded?.Invoke(this, element);
        return (element, true);
    }

    /// <summary>
    /// Stores an element's checked locators. If another element on the page uses a locator that uniquely finds this one,
    /// they are the same control (captured again after the app restarted, or opened from a recording): the earlier entry is
    /// kept and returned, updated with the new capture, and this one is removed.
    /// </summary>
    /// <remarks>
    /// A recording keeps a window known only by its title as a numbered page (D-70). The first capture in such a window that
    /// matches an element of a numbered page this way tells which window that page is: the numbered page takes over the live
    /// page's key and elements (keeping its own elements, names and class name) and <see cref="Reset"/> is raised.
    /// </remarks>
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

        var unique = report.Candidates.Where(c => c.IsUnique).Select(c => c.Key).Append(selected.Key).ToHashSet(StringComparer.Ordinal);
        bool IsTwin(PageElement other) => other != element && other.Selected is { IsUnique: true } s && unique.Contains(s.Key);
        var twin = page.Elements.FirstOrDefault(IsTwin);
        var regrouped = false;
        if (twin is null && page.Key.IsTitled && _pages.FirstOrDefault(p => p.Key.IsNumbered && p.Elements.Any(IsTwin)) is { } saved)
        {
            saved.Absorb(page);
            _pages.Remove(page);
            (page, twin, regrouped) = (saved, saved.Elements.First(IsTwin), true);
        }

        if (twin is null)
        {
            return element;
        }

        page.Remove(element);
        _bySessionKey[element.Snapshot.SessionKey] = twin;
        twin.Recaptured(element.Snapshot);
        twin.SetLocators(report);
        ElementRemoved?.Invoke(this, element);
        if (regrouped)
        {
            Reset?.Invoke(this, EventArgs.Empty);
        }

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