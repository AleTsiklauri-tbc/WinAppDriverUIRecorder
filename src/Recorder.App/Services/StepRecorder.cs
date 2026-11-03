using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.App.Services;

/// <summary>
/// Turns the recording engine's step drafts into recorded steps: their elements go into the catalog (with their locators)
/// and the steps into the step list. Recorded steps are not part of the undo history; edits to them are (D-49). UI thread only.
/// </summary>
public sealed class StepRecorder
{
    // A locator report checked while hovering is used when the element is captured within this time.
    private static readonly TimeSpan PrefetchMaxAge = TimeSpan.FromSeconds(30);
    private readonly ElementCatalog _catalog;
    private readonly StepList _steps;
    private readonly ElementLocating _locating;

    public StepRecorder(ElementCatalog catalog, StepList steps, ElementLocating locating)
    {
        ArgumentNullException.ThrowIfNull(locating);
        (_catalog, _steps, _locating) = (catalog, steps, locating);

        // A capture that turns out to be an element already listed is merged into it: its steps follow.
        locating.Merged += (_, merge) => steps.Retarget(merge.Removed.Id, merge.Kept.Id);
    }

    /// <summary>Locators checked while hovering during the current recording; null when not recording.</summary>
    public LocatorPrefetcher? Prefetch { get; set; }

    public void Apply(IReadOnlyList<StepChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            var step = ToStep(change.Step);
            var index = change is StepReplaced replaced ? _steps.IndexOf(replaced.ReplacedId) : -1;
            if (index >= 0)
            {
                _steps.Replace(index, step);
            }
            else
            {
                _steps.Add(step);
            }
        }
    }

    private RecordedStep ToStep(StepDraft draft)
    {
        var element = draft.Target is { } snapshot ? Capture(snapshot) : null;
        return new RecordedStep
        {
            Id = draft.Id,
            At = draft.At,
            Kind = draft.Kind,
            Target = element?.Id,
            Text = draft.IsSecret ? null : draft.Text,
            TestDataKey = draft.IsSecret ? element?.VariableName ?? "secret" : null,
            State = draft.State,
            RowIndex = draft.RowIndex,
            ColumnIndex = draft.ColumnIndex,
            ColumnHeader = draft.ColumnHeader,
            MenuItems = draft.MenuItems,
            Scroll = draft.Scroll,
            Warnings = draft.Warnings,
        };
    }

    private PageElement Capture(ElementSnapshot snapshot)
    {
        var (element, isNew) = _catalog.Add(snapshot);
        if (!isNew)
        {
            return element;
        }

        if (Prefetch?.TryGet(snapshot.SessionKey, PrefetchMaxAge) is { } report)
        {
            return _locating.Attach(element, report);
        }

        _ = _locating.LocateAsync(element);
        return element;
    }
}
