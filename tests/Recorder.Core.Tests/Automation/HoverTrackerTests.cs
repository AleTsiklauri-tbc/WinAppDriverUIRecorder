using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Automation;

public sealed class HoverTrackerTests
{
    private static readonly HoverOptions Fast = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)) { Refresh = TimeSpan.FromMilliseconds(40) };
    private static readonly UiaResult<ElementSnapshot> Busy = UiaResult.Fail<ElementSnapshot>(UiaStatus.Busy);

    [Fact]
    public async Task A_busy_target_is_asked_again_while_the_cursor_rests_and_highlighted_once_it_answers()
    {
        var uia = new ScriptedUia(Busy, Busy, UiaResult.Ok(TestSnapshots.Edit()));
        var updates = new ConcurrentQueue<HoverUpdate>();
        var highlighted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var tracker = new HoverTracker(new RestingCursor(), uia, new WholeScreenIsTarget(), Fast, TimeProvider.System, NullLogger<HoverTracker>.Instance);
        tracker.Updated += (_, update) =>
        {
            updates.Enqueue(update);
            if (update.Kind == HoverKind.Element)
            {
                highlighted.TrySetResult();
            }
        };

        tracker.Start();
        await highlighted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        updates.Select(update => update.Kind).ShouldBe([HoverKind.Busy, HoverKind.Element]);
        uia.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task Refreshing_an_unchanged_element_publishes_nothing_new()
    {
        var uia = new ScriptedUia(UiaResult.Ok(TestSnapshots.Edit()));
        var updates = new ConcurrentQueue<HoverUpdate>();
        await using var tracker = new HoverTracker(new RestingCursor(), uia, new WholeScreenIsTarget(), Fast, TimeProvider.System, NullLogger<HoverTracker>.Instance);
        tracker.Updated += (_, update) => updates.Enqueue(update);

        tracker.Start();
        await WaitUntilAsync(() => uia.Calls >= 4);
        await tracker.StopAsync();

        updates.Count.ShouldBe(1);
        updates.Single().Kind.ShouldBe(HoverKind.Element);
    }

    [Fact]
    public async Task A_lookup_still_running_is_never_superseded_by_a_refresh()
    {
        // The first answer takes far longer than the refresh interval, like a frozen target.
        var uia = new ScriptedUia(Busy) { Delay = TimeSpan.FromMilliseconds(300) };
        var updates = new ConcurrentQueue<HoverUpdate>();
        await using var tracker = new HoverTracker(new RestingCursor(), uia, new WholeScreenIsTarget(), Fast, TimeProvider.System, NullLogger<HoverTracker>.Instance);
        tracker.Updated += (_, update) => updates.Enqueue(update);

        tracker.Start();
        await WaitUntilAsync(() => !updates.IsEmpty);

        updates.Single().Kind.ShouldBe(HoverKind.Busy);
        uia.MaxConcurrentCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_busy_answer_is_shown_even_if_the_cursor_moved_on_while_waiting_for_it()
    {
        // Each lookup takes as long as the UIA timeout; the user moves to another control during the first one.
        var cursor = new MovableCursor { Position = new PixelPoint(100, 100) };
        var uia = new ScriptedUia(Busy) { Delay = TimeSpan.FromMilliseconds(300) };
        var completedWhenShown = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var tracker = new HoverTracker(cursor, uia, new WholeScreenIsTarget(), Fast, TimeProvider.System, NullLogger<HoverTracker>.Instance);
        tracker.Updated += (_, update) =>
        {
            if (update.Kind == HoverKind.Busy)
            {
                completedWhenShown.TrySetResult(uia.Completed);
            }
        };

        tracker.Start();
        await WaitUntilAsync(() => uia.Calls == 1);
        cursor.Position = new PixelPoint(300, 300);

        // Shown when the first lookup answers, without waiting for the second one to time out as well.
        (await completedWhenShown.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "timed out");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class RestingCursor : ICursor
    {
        public PixelPoint Position => new(100, 100);
    }

    private sealed class MovableCursor : ICursor
    {
        private readonly Lock _gate = new();
        private PixelPoint _position;

        public PixelPoint Position
        {
            get
            {
                lock (_gate)
                {
                    return _position;
                }
            }

            set
            {
                lock (_gate)
                {
                    _position = value;
                }
            }
        }
    }

    private sealed class WholeScreenIsTarget : ITargetFilter
    {
        public bool IsOverTarget(PixelPoint point) => true;

        public bool Owns(ElementSnapshot snapshot) => true;
    }

    /// <summary>Answers hover lookups from a script, one call at a time like the UIA worker thread; the last answer repeats.</summary>
    private sealed class ScriptedUia(params UiaResult<ElementSnapshot>[] answers) : IUiaService
    {
        private readonly SemaphoreSlim _worker = new(1, 1);
        private int _calls;
        private int _completed;
        private int _running;
        private int _maxRunning;

        public TimeSpan Delay { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public int Completed => Volatile.Read(ref _completed);

        /// <summary>The most lookups the tracker had asked for and not yet received at any moment.</summary>
        public int MaxConcurrentCalls => Volatile.Read(ref _maxRunning);

        public async Task<UiaResult<ElementSnapshot>> HoverAtAsync(PixelPoint point, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls) - 1;
            InterlockedMax(ref _maxRunning, Interlocked.Increment(ref _running));
            try
            {
                await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                    return answers[Math.Min(call, answers.Length - 1)];
                }
                finally
                {
                    Interlocked.Increment(ref _completed);
                    _worker.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return UiaResult.Fail<ElementSnapshot>(UiaStatus.Cancelled);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public Task<UiaResult<ElementSnapshot>> CaptureAtAsync(PixelPoint point, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UiaResult<ElementSnapshot>> RefreshAsync(string sessionKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
