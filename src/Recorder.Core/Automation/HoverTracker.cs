using Microsoft.Extensions.Logging;
using Recorder.Core.Interop;
using Recorder.Core.Model;

namespace Recorder.Core.Automation;

public interface ICursor
{
    PixelPoint Position { get; }
}

public sealed class SystemCursor : ICursor
{
    public PixelPoint Position => Win32.CursorPosition();
}

public sealed record HoverOptions(TimeSpan PollInterval, TimeSpan Debounce)
{
    public static HoverOptions Default { get; } = new(TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(40));

    /// <summary>
    /// How long after a lookup the element under a resting cursor is looked up again. What is under the cursor
    /// can change without the cursor moving: a dialog opens, a busy target recovers.
    /// </summary>
    public TimeSpan Refresh { get; init; } = TimeSpan.FromMilliseconds(500);
}

public enum HoverKind
{
    /// <summary>The cursor rests on an element of the target.</summary>
    Element,

    /// <summary>The cursor is not over the target (or over the recorder itself): hide the highlight.</summary>
    Away,

    /// <summary>The target did not answer in time.</summary>
    Busy,
}

/// <param name="Latency">From the cursor arriving at the point to the element being known.</param>
public sealed record HoverUpdate(HoverKind Kind, ElementSnapshot? Element, TimeSpan Latency);

/// <summary>The element under the resting cursor and when it was last looked up (a <see cref="TimeProvider"/> timestamp).</summary>
public sealed record HoverSample(ElementSnapshot Element, long Timestamp);

/// <summary>
/// Polls the cursor (no hook needed for hovering), debounces, and looks up the element under it.
/// Lookups are latest-wins; a result is published only if no newer lookup has started since.
/// </summary>
public sealed partial class HoverTracker : IAsyncDisposable
{
    private readonly ICursor _cursor;
    private readonly IUiaService _uia;
    private readonly ITargetFilter _filter;
    private readonly HoverOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<HoverTracker> _logger;
    private readonly Lock _publishGate = new();
    private CancellationTokenSource? _run;
    private Task? _loop;
    private int _generation;
    private int _inFlight;
    private long _refreshDue;
    private HoverUpdate? _published;
    private HoverSample? _latest;

    public HoverTracker(ICursor cursor, IUiaService uia, ITargetFilter filter, HoverOptions options, TimeProvider time, ILogger<HoverTracker> logger)
    {
        _cursor = cursor;
        _uia = uia;
        _filter = filter;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>Raised on a background thread.</summary>
    public event EventHandler<HoverUpdate>? Updated;

    public bool IsRunning => _loop is not null;

    /// <summary>
    /// The hover cache: the element under the resting cursor, refreshed with every lookup even when nothing changed and
    /// so nothing was published. Null while the cursor is away from the target, the target is busy, or the tracker is stopped.
    /// </summary>
    public HoverSample? Latest => Volatile.Read(ref _latest);

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _published = null;
        Volatile.Write(ref _latest, null);
        _run = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_run.Token));
    }

    public async Task StopAsync()
    {
        if (_run is null || _loop is null)
        {
            return;
        }

        await _run.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _run.Dispose();
        _run = null;
        _loop = null;
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _latest, null);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var debouncer = new HoverDebouncer(_options.Debounce);
        var started = _time.GetTimestamp();
        using var timer = new PeriodicTimer(_options.PollInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // A resting cursor is looked up again now and then, but never while a lookup is still running:
                // a frozen target answers only after the timeout, and its "busy" answer must not be superseded.
                var refreshing = false;
                if (Volatile.Read(ref _inFlight) == 0 && _time.GetTimestamp() >= Volatile.Read(ref _refreshDue))
                {
                    refreshing = debouncer.HasResolved;
                    debouncer.Reset();
                }

                var point = debouncer.Sample(_cursor.Position, _time.GetElapsedTime(started));
                if (point is null)
                {
                    continue;
                }

                var generation = Interlocked.Increment(ref _generation);
                var settledAt = refreshing ? _time.GetTimestamp() : started + (long)(debouncer.SettledSince.TotalSeconds * _time.TimestampFrequency);
                if (!_filter.IsOverTarget(point.Value))
                {
                    ScheduleRefresh();
                    Volatile.Write(ref _latest, null);
                    Publish(new HoverUpdate(HoverKind.Away, null, TimeSpan.Zero));
                    continue;
                }

                Interlocked.Increment(ref _inFlight);
                _ = ResolveAsync(point.Value, generation, settledAt, refreshing, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    private async Task ResolveAsync(PixelPoint point, int generation, long settledAt, bool refreshing, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uia.HoverAtAsync(point, cancellationToken).ConfigureAwait(false);

            // A busy answer describes the target, not the point, so it is shown even if the cursor has moved on since:
            // "not responding" then appears after one timeout, not after a second lookup times out too.
            var stale = generation != Volatile.Read(ref _generation);
            if (result.Status == UiaStatus.Cancelled || (stale && result.Status != UiaStatus.Busy))
            {
                return;
            }

            var latency = _time.GetElapsedTime(settledAt);
            var update = result switch
            {
                { IsOk: true } when _filter.Owns(result.Value!) => new HoverUpdate(HoverKind.Element, result.Value, latency),
                { Status: UiaStatus.Busy } => new HoverUpdate(HoverKind.Busy, null, latency),
                _ => new HoverUpdate(HoverKind.Away, null, latency),
            };

            if (update.Kind == HoverKind.Element && !refreshing)
            {
                LogHover(_logger, latency.TotalMilliseconds);
            }

            Volatile.Write(ref _latest, update.Element is { } element ? new HoverSample(element, _time.GetTimestamp()) : null);
            Publish(update);
        }
        finally
        {
            ScheduleRefresh();
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void ScheduleRefresh() =>
        Volatile.Write(ref _refreshDue, _time.GetTimestamp() + (long)(_options.Refresh.TotalSeconds * _time.TimestampFrequency));

    // Repeats of the same state are not published: the overlay redraws only when something changed.
    private void Publish(HoverUpdate update)
    {
        lock (_publishGate)
        {
            var previous = _published;
            if (previous is not null && previous.Kind == update.Kind
                && previous.Element?.SessionKey == update.Element?.SessionKey && previous.Element?.Bounds == update.Element?.Bounds)
            {
                return;
            }

            _published = update;
        }

        Updated?.Invoke(this, update);
    }

    [LoggerMessage(EventId = 500, Level = LogLevel.Debug, Message = "Hover resolved {LatencyMs:0} ms after the cursor settled")]
    private static partial void LogHover(ILogger logger, double latencyMs);
}
