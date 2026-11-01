using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>
/// A target that stops responding (LMS does this during long server calls) must never hang the recorder.
/// The SampleApp's "Freeze 5 s" button blocks its UI thread.
/// </summary>
public sealed class BusyTargetTests
{
    [Fact]
    public async Task A_frozen_target_is_reported_busy_within_the_timeout_and_answers_again_afterwards()
    {
        var token = TestContext.Current.CancellationToken;
        using var app = AppSession.Launch(new ProcessStartInfo(TestPaths.SampleAppExe));
        var handle = app.MainWindow.Properties.NativeWindowHandle.Value;
        var amount = Desktop.CenterOf(Wait.ForDescendant(app.MainWindow, "txtAmount"));

        // Never looked up before the freeze: UIA needs two cross-process steps for it, each with its own timeout.
        var insured = Desktop.CenterOf(Wait.ForDescendant(app.MainWindow, "chkInsured"));
        var freeze = Wait.ForDescendant(app.MainWindow, "btnFreeze");
        using var uia = new UiaService(UiaOptions.Default, new ValuePatternReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
        Desktop.Raise(handle);
        (await uia.CaptureAtAsync(amount, token)).Status.ShouldBe(UiaStatus.Ok);

        Click.Post(freeze);
        var frozenFor = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromMilliseconds(300), token); // the click reaches the UI thread, which then sleeps

        var stopwatch = Stopwatch.StartNew();
        var whileFrozen = await uia.CaptureAtAsync(insured, token);
        var elapsed = stopwatch.Elapsed;
        TestContext.Current.TestOutputHelper?.WriteLine($"Frozen target answered {whileFrozen.Status} after {elapsed.TotalMilliseconds:0} ms");

        whileFrozen.Status.ShouldBe(UiaStatus.Busy);
        elapsed.ShouldBeLessThan(UiaOptions.Default.AnswerWithin + TimeSpan.FromMilliseconds(500));

        // Once the target wakes up, the same service works again: nothing stays stuck.
        var remaining = TimeSpan.FromSeconds(5.5) - frozenFor.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, token);
        }

        var afterwards = await uia.CaptureAtAsync(insured, token);
        afterwards.Status.ShouldBe(UiaStatus.Ok);
        afterwards.Value!.AutomationId.ShouldBe("chkInsured");
    }
}
