using System.Windows.Threading;
using Recorder.Core.Model;

namespace Recorder.App.Services;

/// <summary>
/// Says when the recording changed, a while after the last change to the elements (added, merged, renamed, another locator
/// chosen), a page class name, the steps, or the recording's file name: the code pane generates again 150 ms after it
/// (spec 10.4), autosave keeps the recording 2 s after it (spec 12). UI thread only.
/// </summary>
public sealed class RecordingChangeWatcher
{
    private readonly ElementCatalog _catalog;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<PageElement> _watched = [];

    public RecordingChangeWatcher(ElementCatalog catalog, StepList steps, RecordingFiles files, TimeSpan delay, Action changed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(files);
        _catalog = catalog;
        _timer = new DispatcherTimer(delay, DispatcherPriority.Background, (_, _) => Fire(changed), Dispatcher.CurrentDispatcher);
        _timer.Stop();
        catalog.ElementAdded += (_, element) => Watch(element);
        catalog.ElementRemoved += (_, element) => Unwatch(element);
        catalog.Reset += (_, _) => WatchAll();
        catalog.PageRenamed += (_, _) => Schedule();
        steps.Changed += (_, _) => Schedule();
        files.PathChanged += (_, _) => Schedule();
        WatchAll();
    }

    /// <summary>Drops a pending change: it was just handled anyway.</summary>
    public void Cancel() => _timer.Stop();

    private void Fire(Action changed)
    {
        _timer.Stop();
        changed();
    }

    private void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void Watch(PageElement element)
    {
        if (_watched.Add(element))
        {
            element.Changed += OnElementChanged;
        }

        Schedule();
    }

    private void Unwatch(PageElement element)
    {
        if (_watched.Remove(element))
        {
            element.Changed -= OnElementChanged;
        }

        Schedule();
    }

    // Opening a recording replaces every element without removing them one by one.
    private void WatchAll()
    {
        foreach (var element in _watched)
        {
            element.Changed -= OnElementChanged;
        }

        _watched.Clear();
        foreach (var element in _catalog.Elements)
        {
            Watch(element);
        }

        Schedule();
    }

    private void OnElementChanged(object? sender, EventArgs e) => Schedule();
}
