using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Logging;
using Recorder.Core.Model;

namespace Recorder.Core.Automation;

/// <summary>Capture operations. Every call runs on the UIA worker thread and never blocks the caller.</summary>
public interface IUiaService
{
    /// <summary>Element under the cursor for hover highlighting. Latest wins: an older pending request is cancelled.</summary>
    Task<UiaResult<ElementSnapshot>> HoverAtAsync(PixelPoint point, CancellationToken cancellationToken);

    /// <summary>Element under a point, for capturing.</summary>
    Task<UiaResult<ElementSnapshot>> CaptureAtAsync(PixelPoint point, CancellationToken cancellationToken);

    /// <summary>Reads an element seen earlier in this session again (for example an ancestor).</summary>
    Task<UiaResult<ElementSnapshot>> RefreshAsync(string sessionKey, CancellationToken cancellationToken);

    /// <summary>The current state of an element seen earlier: enabled, visible, text, value (never for a password field), checked.</summary>
    Task<UiaResult<ElementState>> ReadStateAsync(string sessionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Reports where keyboard focus is now, then every change, in order, on the worker thread: the focused element when
    /// <paramref name="isTargetProcess"/> accepts its process, otherwise null (focus moved to another app or to the recorder).
    /// Disposing the result stops it.
    /// </summary>
    Task<UiaResult<IAsyncDisposable>> WatchFocusAsync(Func<int, bool> isTargetProcess, Action<ElementSnapshot?> onFocusChanged, CancellationToken cancellationToken);
}

public sealed partial class UiaService : IUiaService, IDisposable
{
    private const string HoverLane = "hover";
    private readonly UiaWorker<UiaContext> _worker;
    private readonly UiaOptions _options;
    private readonly ILogger<UiaService> _logger;

    public UiaService(UiaOptions options, IValueReader valueReader, TimeProvider time, ILogger<UiaService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _worker = new UiaWorker<UiaContext>(() => new UiaContext(options, valueReader, time), options.StuckAfter, logger);
    }

    public Task<UiaResult<ElementSnapshot>> HoverAtAsync(PixelPoint point, CancellationToken cancellationToken) =>
        RunAsync(nameof(HoverAtAsync), HoverLane, (context, token) => SnapshotAt(context, point, findLabel: false, token), cancellationToken);

    public Task<UiaResult<ElementSnapshot>> CaptureAtAsync(PixelPoint point, CancellationToken cancellationToken) =>
        RunAsync(nameof(CaptureAtAsync), lane: null, (context, token) => SnapshotAt(context, point, findLabel: true, token), cancellationToken);

    public Task<UiaResult<ElementSnapshot>> RefreshAsync(string sessionKey, CancellationToken cancellationToken) =>
        RunAsync(nameof(RefreshAsync), lane: null, (context, token) => Refresh(context, sessionKey, token), cancellationToken);

    public Task<UiaResult<ElementState>> ReadStateAsync(string sessionKey, CancellationToken cancellationToken) =>
        RunAsync(nameof(ReadStateAsync), lane: null, (context, token) => ReadState(context, sessionKey, token), cancellationToken);

    public Task<UiaResult<IAsyncDisposable>> WatchFocusAsync(
        Func<int, bool> isTargetProcess, Action<ElementSnapshot?> onFocusChanged, CancellationToken cancellationToken) =>
        RunAsync<IAsyncDisposable>(
            nameof(WatchFocusAsync), lane: null, (context, _) => UiaResult.Ok<IAsyncDisposable>(new FocusWatch(this, context, isTargetProcess, onFocusChanged)), cancellationToken);

    public void Dispose() => _worker.Dispose();

    private static UiaResult<ElementSnapshot> SnapshotAt(UiaContext context, PixelPoint point, bool findLabel, CancellationToken token)
    {
        var captured = context.Snapshots.AtPoint(point, Environment.ProcessId, findLabel, token);
        return captured is null ? UiaResult.Fail<ElementSnapshot>(OwnOrNothing(point)) : Remember(context, captured);
    }

    private static UiaResult<ElementSnapshot> Refresh(UiaContext context, string sessionKey, CancellationToken token)
    {
        var element = context.Elements.Find(sessionKey);
        var captured = element is null ? null : context.Snapshots.Refresh(element, token);
        return captured is null ? UiaResult.Fail<ElementSnapshot>(UiaStatus.Gone) : Remember(context, captured);
    }

    private static UiaResult<ElementState> ReadState(UiaContext context, string sessionKey, CancellationToken token)
    {
        var element = context.Elements.Find(sessionKey);
        var state = element is null ? null : context.States.Read(element, token);
        return state is null ? UiaResult.Fail<ElementState>(UiaStatus.Gone) : UiaResult.Ok(state);
    }

    private static UiaResult<ElementSnapshot> Remember(UiaContext context, CapturedElement captured)
    {
        context.Elements.Remember(captured.Snapshot.SessionKey, captured.Element);
        for (var i = 0; i < captured.AncestorElements.Count; i++)
        {
            context.Elements.Remember(RuntimeKey.Of(captured.Snapshot.Ancestors[i].RuntimeId), captured.AncestorElements[i]);
        }

        return UiaResult.Ok(captured.Snapshot);
    }

    private static UiaStatus OwnOrNothing(PixelPoint point) =>
        Interop.Win32.ProcessAtPoint(point) == Environment.ProcessId ? UiaStatus.OwnWindow : UiaStatus.NothingThere;

    private async Task<UiaResult<T>> RunAsync<T>(
        string operation, string? lane, Func<UiaContext, CancellationToken, UiaResult<T>> work, CancellationToken cancellationToken)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        UiaResult<T> result;
        var task = lane is null ? _worker.InvokeAsync(work, cancellationToken) : _worker.InvokeLatestAsync(lane, work, cancellationToken);
        try
        {
            result = await task.WaitAsync(_options.AnswerWithin, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (UiaErrors.Classify(exception) is { } status)
        {
            if (!task.IsCompleted)
            {
                // The worker is still waiting for the target. Its late answer is dropped and must not surface later
                // as an unobserved task exception.
                _ = task.ContinueWith(
                    static abandoned => _ = abandoned.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            if (status != UiaStatus.Cancelled)
            {
                LogFailed(_logger, operation, status, stopwatch.ElapsedMilliseconds, exception.HResult);
            }

            return UiaResult.Fail<T>(status);
        }

        LogCompleted(_logger, operation, result.Status, stopwatch.ElapsedMilliseconds);
        return result;
    }

    // Timings only: names and values of elements are customer data and are never logged.
    [LoggerMessage(EventId = 210, Level = LogLevel.Debug, Message = "{Operation} {Status} in {ElapsedMs} ms")]
    private static partial void LogCompleted(ILogger logger, string operation, UiaStatus status, long elapsedMs);

    [LoggerMessage(EventId = 211, Level = LogLevel.Information, Message = "{Operation} {Status} after {ElapsedMs} ms (HRESULT 0x{HResult:X8})")]
    private static partial void LogFailed(ILogger logger, string operation, UiaStatus status, long elapsedMs, int hresult);

    [LoggerMessage(EventId = 212, Level = LogLevel.Debug, Message = "A focus change could not be read (HRESULT 0x{HResult:X8})")]
    private static partial void LogFocusUnread(ILogger logger, int hresult);

    /// <summary>
    /// A UI Automation focus-changed handler. The handler runs on UI Automation's own thread and only queues the element;
    /// it is read on the worker thread, in order, and reported from there.
    /// </summary>
    private sealed class FocusWatch : IAsyncDisposable
    {
        private readonly UiaService _owner;
        private readonly object _automation;
        private readonly FlaUI.Core.EventHandlers.FocusChangedEventHandlerBase _handler;
        private readonly Func<int, bool> _isTargetProcess;
        private readonly Action<ElementSnapshot?> _report;
        private int _stopped;

        // Worker thread.
        public FocusWatch(UiaService owner, UiaContext context, Func<int, bool> isTargetProcess, Action<ElementSnapshot?> report)
        {
            _owner = owner;
            _isTargetProcess = isTargetProcess;
            _report = report;
            _automation = context.Automation;
            _handler = context.Automation.RegisterFocusChangedEvent(OnFocusChanged);

            // Where focus is now, read as the next work item so that registering never waits for a busy target.
            _ = owner._worker.InvokeAsync(
                (current, token) =>
                {
                    Report(current, () => current.Automation.FocusedElement(), token);
                    return true;
                },
                CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            try
            {
                await _owner._worker.InvokeAsync(
                    (context, _) =>
                    {
                        // A replaced worker thread disposed its automation, and its handlers with it.
                        if (ReferenceEquals(context.Automation, _automation))
                        {
                            context.Automation.UnregisterFocusChangedEvent(_handler);
                        }

                        return true;
                    },
                    CancellationToken.None).WaitAsync(_owner._options.AnswerWithin).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or ObjectDisposedException || UiaErrors.Classify(exception) is not null)
            {
                // The worker is gone or stuck; its automation and handlers end with it.
            }
        }

        // UI Automation's event thread: no cross-process calls here.
        private void OnFocusChanged(AutomationElement element)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }

            try
            {
                _ = _owner._worker.InvokeAsync(
                    (context, token) =>
                    {
                        if (Volatile.Read(ref _stopped) == 0)
                        {
                            Report(context, () => element, token);
                        }

                        return true;
                    },
                    CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        }

        // Worker thread.
        private void Report(UiaContext context, Func<AutomationElement?> focused, CancellationToken token)
        {
            try
            {
                var element = focused();
                var captured = element is null ? null : context.Snapshots.Focused(element, pid => pid != Environment.ProcessId && _isTargetProcess(pid), token);
                if (captured is not null)
                {
                    Remember(context, captured);
                }

                _report(captured?.Snapshot);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The element vanished or the target is busy: this one focus change is skipped.
                LogFocusUnread(_owner._logger, exception.HResult);
            }
        }
    }
}
