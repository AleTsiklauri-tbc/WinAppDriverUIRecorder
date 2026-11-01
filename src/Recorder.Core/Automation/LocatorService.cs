using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Automation;

public enum LocateMode
{
    /// <summary>For the highlight colour while hovering: latest wins, and a tree read moments ago may be reused.</summary>
    Hover,

    /// <summary>For a captured element: always checked against the screen as it is now.</summary>
    Capture,
}

/// <param name="Budget">Spec 7.5: the whole uniqueness check for one element. After that the candidates are shown unchecked.</param>
/// <param name="HoverReuse">How old a tree read may be when hovering moves to another element of the same window.</param>
public sealed record LocatorOptions(TimeSpan Budget, TimeSpan HoverReuse)
{
    public static LocatorOptions Default { get; } = new(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(2));
}

/// <summary>An element a locator matched, for "Highlight matches".</summary>
public sealed record LocatorMatch(PixelRect Bounds, bool IsOffscreen, bool IsTarget);

/// <summary>Locator checks against the live UI. Every call runs on the locator worker thread and never blocks the caller.</summary>
public interface ILocatorService
{
    /// <summary>All candidates for an element, checked for uniqueness in the tree WinAppDriver would search.</summary>
    Task<UiaResult<LocatorReport>> LocateAsync(ElementSnapshot target, LocateMode mode, CancellationToken cancellationToken);

    /// <summary>The elements a locator matches now, in document order.</summary>
    Task<UiaResult<IReadOnlyList<LocatorMatch>>> FindMatchesAsync(ElementSnapshot target, LocatorSpec spec, CancellationToken cancellationToken);

    /// <summary>The absolute path from the window root (Inspector › Advanced).</summary>
    Task<UiaResult<LocatorSpec>> AbsolutePathAsync(ElementSnapshot target, CancellationToken cancellationToken);
}

/// <summary>
/// Runs on its own UI Automation worker, so a large tree never delays hovering or capturing. Logs timings and statuses
/// only: names and values are customer data.
/// </summary>
public sealed partial class LocatorService : ILocatorService, IDisposable
{
    private const string HoverLane = "locate-hover";
    private readonly UiaWorker<LocatorContext> _worker;
    private readonly UiaOptions _uiaOptions;
    private readonly LocatorOptions _options;
    private readonly ILogger<LocatorService> _logger;

    public LocatorService(UiaOptions uiaOptions, LocatorOptions options, TimeProvider time, ILogger<LocatorService> logger)
    {
        ArgumentNullException.ThrowIfNull(uiaOptions);
        _uiaOptions = uiaOptions;
        _options = options;
        _logger = logger;
        _worker = new UiaWorker<LocatorContext>(() => new LocatorContext(uiaOptions, time), uiaOptions.StuckAfter, logger);
    }

    public async Task<UiaResult<LocatorReport>> LocateAsync(ElementSnapshot target, LocateMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var stopwatch = Stopwatch.StartNew();
        var reuse = mode == LocateMode.Hover ? _options.HoverReuse : TimeSpan.Zero;
        var task = mode == LocateMode.Hover
            ? _worker.InvokeLatestAsync(HoverLane, Work, cancellationToken)
            : _worker.InvokeAsync(Work, cancellationToken);
        var result = await WaitAsync(nameof(LocateAsync), task, _options.Budget, stopwatch, cancellationToken).ConfigureAwait(false);
        if (result.Status == UiaStatus.Busy && !cancellationToken.IsCancellationRequested)
        {
            // Out of budget (a large tree) or the target is busy: the candidates themselves are still worth showing.
            LogUnchecked(_logger, stopwatch.ElapsedMilliseconds);
            return UiaResult.Ok(LocatorEngine.Unchecked(LocatorSubject.From(target, target.Scope), stopwatch.Elapsed));
        }

        if (result.Value is { } report)
        {
            LogLocated(_logger, mode, report.TreeSize, report.Candidates.Count, stopwatch.ElapsedMilliseconds);
        }

        return result;

        UiaResult<LocatorReport> Work(LocatorContext context, CancellationToken token)
        {
            var tree = context.TreeFor(target.SearchRoot, target.SessionKey, reuse, token);
            var node = tree.Find(target.SessionKey);
            if (node is null)
            {
                return UiaResult.Fail<LocatorReport>(UiaStatus.Gone);
            }

            var subject = LocatorSubject.From(node, target.ProcessId, target.Scope);
            return UiaResult.Ok(LocatorEngine.Analyze(subject, tree, node, stopwatch.Elapsed));
        }
    }

    public Task<UiaResult<IReadOnlyList<LocatorMatch>>> FindMatchesAsync(ElementSnapshot target, LocatorSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spec);
        var stopwatch = Stopwatch.StartNew();
        var task = _worker.InvokeAsync(
            (context, token) =>
            {
                var tree = context.TreeFor(target.SearchRoot, target.SessionKey, TimeSpan.Zero, token);
                IReadOnlyList<LocatorMatch> matches = [.. LocatorEvaluator.Evaluate(spec, tree)
                    .Select(node => new LocatorMatch(node.Bounds, node.IsOffscreen, node.RuntimeKey == target.SessionKey))];
                return UiaResult.Ok(matches);
            },
            cancellationToken);
        return WaitAsync(nameof(FindMatchesAsync), task, _uiaOptions.AnswerWithin, stopwatch, cancellationToken);
    }

    public Task<UiaResult<LocatorSpec>> AbsolutePathAsync(ElementSnapshot target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var stopwatch = Stopwatch.StartNew();
        var task = _worker.InvokeAsync(
            (context, token) =>
            {
                var tree = context.TreeFor(target.SearchRoot, target.SessionKey, _options.HoverReuse, token);
                return tree.Find(target.SessionKey) is { } node
                    ? UiaResult.Ok(AbsolutePath.Build(node, target.ProcessId))
                    : UiaResult.Fail<LocatorSpec>(UiaStatus.Gone);
            },
            cancellationToken);
        return WaitAsync(nameof(AbsolutePathAsync), task, _uiaOptions.AnswerWithin, stopwatch, cancellationToken);
    }

    public void Dispose() => _worker.Dispose();

    private async Task<UiaResult<T>> WaitAsync<T>(string operation, Task<UiaResult<T>> task, TimeSpan limit, Stopwatch stopwatch, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await task.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (UiaErrors.Classify(exception) is { } status)
        {
            if (!task.IsCompleted)
            {
                // The worker is still reading; its late result is dropped and must not surface as an unobserved exception.
                _ = task.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            if (status != UiaStatus.Cancelled)
            {
                LogFailed(_logger, operation, status, stopwatch.ElapsedMilliseconds, exception.HResult);
            }

            return UiaResult.Fail<T>(status);
        }
    }

    [LoggerMessage(EventId = 220, Level = LogLevel.Debug, Message = "Located ({Mode}): {TreeSize} elements, {Candidates} candidates in {ElapsedMs} ms")]
    private static partial void LogLocated(ILogger logger, LocateMode mode, int treeSize, int candidates, long elapsedMs);

    [LoggerMessage(EventId = 221, Level = LogLevel.Information, Message = "Uniqueness not checked: no answer within the budget ({ElapsedMs} ms)")]
    private static partial void LogUnchecked(ILogger logger, long elapsedMs);

    [LoggerMessage(EventId = 222, Level = LogLevel.Information, Message = "{Operation} {Status} after {ElapsedMs} ms (HRESULT 0x{HResult:X8})")]
    private static partial void LogFailed(ILogger logger, string operation, UiaStatus status, long elapsedMs, int hresult);
}
