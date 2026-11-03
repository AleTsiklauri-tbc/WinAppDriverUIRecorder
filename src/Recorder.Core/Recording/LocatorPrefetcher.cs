using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// Works out the locators of the element under the resting cursor while recording. A recorded step's element then has
/// checked locators even when the click makes it disappear, like the OK button of a message box (D-47).
/// </summary>
public sealed class LocatorPrefetcher : IDisposable
{
    private const int Capacity = 64;
    private readonly ILocatorService _locators;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (LocatorReport Report, long At)> _reports = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly CancellationTokenSource _stop = new();

    public LocatorPrefetcher(ILocatorService locators, TimeProvider time)
    {
        _locators = locators;
        _time = time;
    }

    /// <summary>Starts checking a hovered element; latest wins, like hover highlighting.</summary>
    public void Observe(HoverUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update is { Kind: HoverKind.Element, Element: { } element } && element.SessionKey.Length > 0 && !_stop.IsCancellationRequested)
        {
            _ = LocateAsync(element);
        }
    }

    /// <summary>A report checked for the element within <paramref name="maxAge"/>, or null.</summary>
    public LocatorReport? TryGet(string sessionKey, TimeSpan maxAge)
    {
        lock (_gate)
        {
            return _reports.TryGetValue(sessionKey, out var entry) && _time.GetElapsedTime(entry.At) <= maxAge ? entry.Report : null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task LocateAsync(ElementSnapshot element)
    {
        UiaResult<LocatorReport> result;
        try
        {
            result = await _locators.LocateAsync(element, LocateMode.Hover, _stop.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (result.Value is not { IsChecked: true } report)
        {
            return;
        }

        lock (_gate)
        {
            if (!_reports.ContainsKey(element.SessionKey))
            {
                _order.Enqueue(element.SessionKey);
                if (_order.Count > Capacity)
                {
                    _reports.Remove(_order.Dequeue());
                }
            }

            _reports[element.SessionKey] = (report, _time.GetTimestamp());
        }
    }
}
