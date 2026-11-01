using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Recorder.Core.Automation;

/// <summary>
/// Runs all UI Automation work on one dedicated MTA thread that owns the automation context.
/// <list type="bullet">
/// <item>Requests are queued through a channel and complete on the thread pool, never on the worker.</item>
/// <item>A "latest-wins" lane cancels the previous request in the same lane (hover must not queue up).</item>
/// <item>If one request runs far past the call timeout (a target that ignores UIA timeouts), the thread is
/// abandoned and a fresh one with a fresh context takes over, so later requests are not stuck behind it.</item>
/// </list>
/// </summary>
public sealed partial class UiaWorker<TContext> : IDisposable
    where TContext : class, IDisposable
{
    private readonly Func<TContext> _createContext;
    private readonly TimeSpan _stuckAfter;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _lanes = new(StringComparer.Ordinal);
    private WorkerThread _current;
    private int _generation;
    private bool _disposed;

    /// <param name="createContext">Called on the worker thread; the context is used only there.</param>
    public UiaWorker(Func<TContext> createContext, TimeSpan stuckAfter, ILogger logger)
    {
        _createContext = createContext;
        _stuckAfter = stuckAfter;
        _logger = logger;
        _current = StartThread();
    }

    /// <summary>Number of times a stuck thread was replaced (for tests and diagnostics).</summary>
    public int Restarts { get; private set; }

    public Task<T> InvokeAsync<T>(Func<TContext, CancellationToken, T> work, CancellationToken cancellationToken) =>
        Enqueue(work, cancellationToken);

    /// <summary>Queues work in a lane where only the newest request matters; older ones are cancelled.</summary>
    public Task<T> InvokeLatestAsync<T>(string lane, Func<TContext, CancellationToken, T> work, CancellationToken cancellationToken)
    {
        // The lane owns its token source; it is disposed when the next request in the lane replaces it.
        // Work still holding the old token can keep checking it: IsCancellationRequested works after Dispose.
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            if (_lanes.Remove(lane, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            _lanes[lane] = source;
        }

        return Enqueue(work, source.Token);
    }

    public void Dispose()
    {
        WorkerThread current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            foreach (var source in _lanes.Values)
            {
                source.Cancel();
                source.Dispose();
            }

            _lanes.Clear();
        }

        current.Writer.TryComplete();
        current.Thread.Join(TimeSpan.FromSeconds(3));
    }

    private Task<T> Enqueue<T>(Func<TContext, CancellationToken, T> work, CancellationToken token)
    {
        var item = new WorkItem<T>(work, token);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current.IsStuck(_stuckAfter))
            {
                ReplaceStuckThread();
            }

            _current.Writer.TryWrite(item);
        }

        return item.Task;
    }

    // Called under _gate.
    private void ReplaceStuckThread()
    {
        var stuck = _current;
        stuck.Abandon();
        _current = StartThread();
        Restarts++;
        LogReplaced(_logger, _stuckAfter.TotalSeconds);

        // Requests queued behind the stuck one move to the new thread.
        while (stuck.Reader.TryRead(out var pending))
        {
            _current.Writer.TryWrite(pending);
        }
    }

    private WorkerThread StartThread()
    {
        var worker = new WorkerThread();
        worker.Thread = new Thread(() => Run(worker))
        {
            IsBackground = true,
            Name = "UiaWorker-" + Interlocked.Increment(ref _generation),
        };
        worker.Thread.SetApartmentState(ApartmentState.MTA);
        worker.Thread.Start();
        return worker;
    }

    private void Run(WorkerThread worker)
    {
        TContext? context = null;
        try
        {
            context = _createContext();
            while (worker.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (!worker.IsAbandoned && worker.Reader.TryRead(out var item))
                {
                    worker.MarkBusy();
                    item.Run(context);
                    worker.MarkIdle();
                }

                if (worker.IsAbandoned)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogThreadFailed(_logger, exception);
            while (worker.Reader.TryRead(out var item))
            {
                item.Fail(exception);
            }
        }
        finally
        {
            context?.Dispose();
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "UI Automation worker was stuck for more than {Seconds} s; started a new one")]
    private static partial void LogReplaced(ILogger logger, double seconds);

    [LoggerMessage(EventId = 201, Level = LogLevel.Error, Message = "UI Automation worker thread failed")]
    private static partial void LogThreadFailed(ILogger logger, Exception exception);

    private sealed class WorkerThread
    {
        private readonly Channel<WorkItem> _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions { SingleReader = false });
        private long _busySince;
        private volatile bool _abandoned;

        public Thread Thread { get; set; } = null!;

        public ChannelReader<WorkItem> Reader => _channel.Reader;

        public ChannelWriter<WorkItem> Writer => _channel.Writer;

        public bool IsAbandoned => _abandoned;

        public void MarkBusy() => Volatile.Write(ref _busySince, Stopwatch.GetTimestamp());

        public void MarkIdle() => Volatile.Write(ref _busySince, 0);

        public bool IsStuck(TimeSpan threshold)
        {
            var since = Volatile.Read(ref _busySince);
            return since != 0 && Stopwatch.GetElapsedTime(since) > threshold;
        }

        public void Abandon()
        {
            _abandoned = true;
            _channel.Writer.TryComplete();
        }
    }

    private abstract class WorkItem
    {
        public abstract void Run(TContext context);

        public abstract void Fail(Exception exception);
    }

    private sealed class WorkItem<T>(Func<TContext, CancellationToken, T> work, CancellationToken token) : WorkItem
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public override void Run(TContext context)
        {
            try
            {
                if (token.IsCancellationRequested)
                {
                    _completion.TrySetCanceled(token);
                    return;
                }

                _completion.TrySetResult(work(context, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _completion.TrySetCanceled(token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _completion.TrySetException(exception);
            }
        }

        public override void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}
