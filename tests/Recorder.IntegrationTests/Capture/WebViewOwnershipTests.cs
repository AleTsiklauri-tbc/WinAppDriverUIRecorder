using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Persistence;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>
/// WebView2 content is served by msedgewebview2.exe (Findings F-12), yet it is part of the target app:
/// ownership is decided by the top-level window that contains the element.
/// </summary>
public sealed class WebViewOwnershipTests : IClassFixture<SampleAppFixture>, IDisposable
{
    private readonly SampleAppFixture _app;
    private readonly string _settingsRoot = RecorderLauncher.NewDataHome();

    public WebViewOwnershipTests(SampleAppFixture app) => _app = app;

    public void Dispose() => Directory.Delete(_settingsRoot, recursive: true);

    [Fact]
    public async Task Web_content_counts_as_part_of_the_target_although_another_process_serves_it()
    {
        Assert.SkipUnless(WebView2Runtime.IsInstalled(), "The WebView2 runtime is not installed.");
        var token = TestContext.Current.CancellationToken;
        var date = _app.OpenWebFormDateInput();
        var center = Desktop.CenterOf(date);
        using var uia = new UiaService(UiaOptions.Default, new ValuePatternReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
        var settings = new SettingsStore(new AppPaths(Path.Combine(_settingsRoot, "roaming"), Path.Combine(_settingsRoot, "local")), NullLogger<SettingsStore>.Instance);
        await using var tracker = new TargetTracker(settings, new SystemTargetEnvironment(), TimeProvider.System, NullLogger<TargetTracker>.Instance);
        tracker.Select(WindowEnumerator.GetAppWindows().First(window => window.ProcessId == _app.Session.ProcessId));
        Desktop.Raise(_app.MainWindowHandle);

        var result = await uia.CaptureAtAsync(center, token);

        result.Status.ShouldBe(UiaStatus.Ok);
        var snapshot = result.Value!;
        snapshot.AutomationId.ShouldBe("datepicker-3");
        snapshot.FrameworkId.ShouldBe("Chrome");
        snapshot.ProcessId.ShouldNotBe(_app.Session.ProcessId);
        snapshot.TopLevelWindow.Handle.ShouldBe(_app.MainWindowHandle);
        tracker.State.Status.ShouldBe(TargetStatus.Attached);
        tracker.Owns(snapshot).ShouldBeTrue();
        tracker.IsOverTarget(center).ShouldBeTrue();
    }
}
