using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Persistence;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>Spec 7.7: testers restart LMS all day; the recorder must follow without being told.</summary>
public sealed class TargetReattachTests : IDisposable
{
    private readonly string _settingsRoot = RecorderLauncher.NewDataHome();

    public void Dispose() => Directory.Delete(_settingsRoot, recursive: true);

    [Fact]
    public async Task A_closed_target_is_waited_for_and_the_restarted_process_is_attached()
    {
        var settings = new SettingsStore(new AppPaths(Path.Combine(_settingsRoot, "roaming"), Path.Combine(_settingsRoot, "local")), NullLogger<SettingsStore>.Instance);
        await using var tracker = new TargetTracker(settings, new SystemTargetEnvironment(), TimeProvider.System, NullLogger<TargetTracker>.Instance);

        using (var first = AppSession.Launch(new ProcessStartInfo(TestPaths.SampleAppExe)))
        {
            tracker.Select(WindowEnumerator.GetAppWindows().First(window => window.ProcessId == first.ProcessId));
            tracker.State.Status.ShouldBe(TargetStatus.Attached);
            tracker.State.ProcessId.ShouldBe(first.ProcessId);

            first.CloseAndWaitForExit(TimeSpan.FromSeconds(10)).ShouldBe(0);
        }

        tracker.Poll();
        tracker.State.Status.ShouldBe(TargetStatus.Waiting);
        tracker.State.WasClosed.ShouldBeTrue();
        tracker.State.ProcessName.ShouldBe("Recorder.SampleApp.exe");

        using var second = AppSession.Launch(new ProcessStartInfo(TestPaths.SampleAppExe));
        Wait.Until(
            () =>
            {
                tracker.Poll();
                return tracker.State.IsAttached;
            },
            TimeSpan.FromSeconds(10),
            "the tracker to attach to the restarted app");

        tracker.State.ProcessId.ShouldBe(second.ProcessId);
        settings.Current.LastTargetProcessName.ShouldBe("Recorder.SampleApp.exe");
    }
}
