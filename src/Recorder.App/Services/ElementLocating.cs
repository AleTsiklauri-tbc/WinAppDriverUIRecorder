using Microsoft.Extensions.Logging;
using Recorder.App.Resources;
using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.App.Services;

/// <summary>
/// Works out the locators of captured elements and keeps the catalog up to date. A capture that turns out to be a
/// control already listed (same unique locator) is merged into it. UI thread only.
/// </summary>
public sealed partial class ElementLocating(ILocatorService locators, ElementCatalog catalog, ToastService toasts, ILogger<ElementLocating> logger)
{
    private readonly HashSet<Guid> _checking = [];
    private readonly Dictionary<Guid, UiaStatus> _failures = [];

    /// <summary>Checking started or ended for an element.</summary>
    public event EventHandler<PageElement>? CheckingChanged;

    /// <summary>An element was merged into one already listed: (removed, kept).</summary>
    public event EventHandler<(PageElement Removed, PageElement Kept)>? Merged;

    public bool IsChecking(PageElement element) => element is not null && _checking.Contains(element.Id);

    /// <summary>Why the last check gave no result (Gone, Busy), or null.</summary>
    public UiaStatus? LastFailure(PageElement element) => element is not null && _failures.TryGetValue(element.Id, out var status) ? status : null;

    /// <summary>Checks the element's locators against the app as it is now. Returns the element that holds them.</summary>
    public async Task<PageElement> LocateAsync(PageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_checking.Add(element.Id))
        {
            return element;
        }

        _failures.Remove(element.Id);
        CheckingChanged?.Invoke(this, element);
        try
        {
            var result = await locators.LocateAsync(element.Snapshot, LocateMode.Capture, CancellationToken.None).ConfigureAwait(true);
            if (result.Value is not { } report)
            {
                _failures[element.Id] = result.Status;
                LogNoResult(logger, result.Status);
                return element;
            }

            return Attach(element, report);
        }
        finally
        {
            _checking.Remove(element.Id);
            CheckingChanged?.Invoke(this, element);
        }
    }

    /// <summary>Stores locators checked earlier (while recording, before a click made the element disappear, D-47).</summary>
    public PageElement Attach(PageElement element, Core.Locators.LocatorReport report)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(report);
        _failures.Remove(element.Id);
        var kept = catalog.AttachLocators(element, report);
        if (!ReferenceEquals(kept, element))
        {
            toasts.Show(Text.Format(Strings.Toast_Merged, kept.VariableName));
            Merged?.Invoke(this, (element, kept));
        }

        CheckingChanged?.Invoke(this, kept);
        return kept;
    }

    [LoggerMessage(EventId = 620, Level = LogLevel.Information, Message = "Locator check gave no result: {Status}")]
    private static partial void LogNoResult(ILogger logger, UiaStatus status);
}
