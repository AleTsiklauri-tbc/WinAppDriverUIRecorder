using System.Diagnostics;
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
}
