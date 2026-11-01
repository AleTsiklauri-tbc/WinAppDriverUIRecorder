using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Automation;

public sealed class UiaWorkerTests : IDisposable
{
    private readonly List<FakeContext> _contexts = [];
    private readonly UiaWorker<FakeContext> _worker;

    public UiaWorkerTests() =>
        _worker = new UiaWorker<FakeContext>(NewContext, stuckAfter: TimeSpan.FromMilliseconds(300), NullLogger.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _worker.Dispose();

    private FakeContext NewContext()
    {
        var context = new FakeContext();
        lock (_contexts)
        {
            _contexts.Add(context);
        }

        return context;
    }

    [Fact]
    public async Task Work_runs_on_one_dedicated_MTA_background_thread()
    {
        var first = await _worker.InvokeAsync((_, _) => Environment.CurrentManagedThreadId, Ct);
        var info = await _worker.InvokeAsync(
            (_, _) => (Id: Environment.CurrentManagedThreadId, Apartment: Thread.CurrentThread.GetApartmentState(), Background: Thread.CurrentThread.IsBackground),
            Ct);

        info.Id.ShouldBe(first);
        info.Id.ShouldNotBe(Environment.CurrentManagedThreadId);
        info.Apartment.ShouldBe(ApartmentState.MTA);
        info.Background.ShouldBeTrue();
    }

    [Fact]
    public async Task Latest_wins_cancels_a_pending_request_in_the_same_lane()
    {
        using var gate = new ManualResetEventSlim();
        var blocker = _worker.InvokeAsync((_, _) => gate.Wait(TimeSpan.FromSeconds(5)), Ct);
        var older = _worker.InvokeLatestAsync("hover", (_, _) => "older", Ct);
        var newer = _worker.InvokeLatestAsync("hover", (_, _) => "newer", Ct);

        gate.Set();
        await blocker;

        await Should.ThrowAsync<TaskCanceledException>(older);
        (await newer).ShouldBe("newer");
    }

    [Fact]
    public async Task Different_lanes_do_not_cancel_each_other()
    {
        var hover = _worker.InvokeLatestAsync("hover", (_, _) => 1, Ct);
        var other = _worker.InvokeLatestAsync("other", (_, _) => 2, Ct);

        (await hover).ShouldBe(1);
        (await other).ShouldBe(2);
    }

    [Fact]
    public async Task Exceptions_reach_the_caller_and_the_worker_keeps_going()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            _worker.InvokeAsync<int>((_, _) => throw new InvalidOperationException("boom"), Ct));

        (await _worker.InvokeAsync((_, _) => 7, Ct)).ShouldBe(7);
    }

    [Fact]
    public async Task A_stuck_thread_is_replaced_and_queued_work_moves_to_the_new_thread()
    {
        using var release = new ManualResetEventSlim();
        var stuck = _worker.InvokeAsync((_, _) => release.Wait(TimeSpan.FromSeconds(10)), Ct);
        var queued = _worker.InvokeAsync((_, _) => Environment.CurrentManagedThreadId, Ct);
        await Task.Delay(500, Ct);

        var afterRestart = await _worker.InvokeAsync((_, _) => Environment.CurrentManagedThreadId, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        _worker.Restarts.ShouldBe(1);
        (await queued.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(afterRestart);
        lock (_contexts)
        {
            _contexts.Count.ShouldBe(2);
        }

        release.Set();
        (await stuck).ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_disposes_the_context_and_rejects_new_work()
    {
        await _worker.InvokeAsync((_, _) => 0, Ct);

        _worker.Dispose();

        lock (_contexts)
        {
            _contexts.ShouldHaveSingleItem().Disposed.ShouldBeTrue();
        }

        Should.Throw<ObjectDisposedException>(() => _worker.InvokeAsync((_, _) => 0, CancellationToken.None));
    }

    public sealed class FakeContext : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
