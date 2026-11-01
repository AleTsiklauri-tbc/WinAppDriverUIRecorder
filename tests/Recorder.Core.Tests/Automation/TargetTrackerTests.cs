using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Interop;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Automation;

public sealed class TargetTrackerTests : IAsyncDisposable
{
    private static readonly AppWindow LmsLogin = new(100, "Login", 4242, "LMS.exe", @"C:\LMS_Client\LMS.exe");
    private static readonly AppWindow LmsMain = new(200, "Loan Management System - [client]", 4242, "LMS.exe", @"C:\LMS_Client\LMS.exe");
    private static readonly AppWindow LmsRestarted = new(300, "Login", 5555, "LMS.exe", @"C:\LMS_Client\LMS.exe");
    private static readonly AppWindow Notepad = new(400, "Untitled - Notepad", 777, "notepad.exe", null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment _environment = new();
    private readonly SettingsStore _settings;
    private readonly TargetTracker _tracker;
    private readonly List<TargetState> _changes = [];

    public TargetTrackerTests()
    {
        _settings = new SettingsStore(new AppPaths(Path.Combine(_root, "r"), Path.Combine(_root, "l")), NullLogger<SettingsStore>.Instance);
        _tracker = NewTracker(_settings);
    }

    public async ValueTask DisposeAsync()
    {
        await _tracker.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TargetTracker NewTracker(SettingsStore settings)
    {
        var tracker = new TargetTracker(settings, _environment, TimeProvider.System, NullLogger<TargetTracker>.Instance);
        tracker.Changed += (_, state) => _changes.Add(state);
        return tracker;
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        // The DI container disposes the tracker once as itself and once as ITargetFilter.
        _environment.Windows.Add(LmsLogin);
        _tracker.Start();
        _tracker.Select(LmsLogin);

        await _tracker.DisposeAsync();
        await Should.NotThrowAsync(async () => await _tracker.DisposeAsync());
    }

    [Fact]
    public void Selecting_a_window_attaches_to_its_process_and_remembers_the_name()
    {
        _environment.Windows.AddRange([LmsLogin, Notepad]);

        _tracker.Select(LmsLogin);

        _tracker.State.ShouldBe(new TargetState(TargetStatus.Attached, "LMS.exe", 4242, 100, "Login", @"C:\LMS_Client\LMS.exe", false));
        _settings.Current.LastTargetProcessName.ShouldBe("LMS.exe");
    }

    [Fact]
    public void When_the_main_window_changes_the_target_follows_it()
    {
        _environment.Windows.Add(LmsLogin);
        _tracker.Select(LmsLogin);
        _environment.Windows.Clear();
        _environment.Windows.Add(LmsMain);

        _tracker.Poll();

        _tracker.State.WindowHandle.ShouldBe(LmsMain.Handle);
        _tracker.State.WindowTitle.ShouldBe(LmsMain.Title);
    }

    [Fact]
    public void A_closed_target_is_waited_for_and_re_attached_when_it_starts_again()
    {
        _environment.Windows.Add(LmsLogin);
        _tracker.Select(LmsLogin);

        _environment.Exit(4242);
        _environment.Windows.Clear();
        _tracker.Poll();
        _tracker.State.ShouldBe(TargetState.None with { Status = TargetStatus.Waiting, ProcessName = "LMS.exe", ExecutablePath = @"C:\LMS_Client\LMS.exe", WasClosed = true });

        _environment.Windows.AddRange([Notepad, LmsRestarted]);
        _tracker.Poll();
        _tracker.State.Status.ShouldBe(TargetStatus.Attached);
        _tracker.State.ProcessId.ShouldBe(5555);
        _changes.Select(c => c.Status).ShouldBe([TargetStatus.Attached, TargetStatus.Waiting, TargetStatus.Attached]);
    }

    [Fact]
    public async Task The_remembered_target_is_restored_on_start()
    {
        _settings.Update(s => s with { LastTargetProcessName = "LMS.exe" });
        await using var restarted = NewTracker(_settings);

        restarted.Start();
        restarted.State.ShouldBe(TargetState.None with { Status = TargetStatus.Waiting, ProcessName = "LMS.exe" });

        _environment.Windows.Add(LmsMain);
        restarted.Poll();
        restarted.State.Status.ShouldBe(TargetStatus.Attached);
    }

    [Fact]
    public void An_elevated_target_is_reported_when_the_recorder_is_not_elevated()
    {
        _environment.Elevated.Add(4242);
        _environment.Windows.Add(LmsLogin);

        _tracker.Select(LmsLogin);

        _tracker.State.Status.ShouldBe(TargetStatus.Elevated);
        _tracker.IsOverTarget(new PixelPoint(1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void An_elevated_target_is_fine_when_the_recorder_is_elevated_too()
    {
        _environment.Elevated.Add(4242);
        _environment.RecorderElevated = true;

        _tracker.Select(LmsLogin);

        _tracker.State.Status.ShouldBe(TargetStatus.Attached);
    }

    [Fact]
    public void Elements_belong_to_the_target_by_process_or_by_their_top_level_window()
    {
        _tracker.Select(LmsMain);
        _environment.WindowOwners[200] = 4242;

        _tracker.Owns(TestSnapshots.Edit(processId: 4242)).ShouldBeTrue();

        // WebView2 content: another process, but inside the target's main window.
        var web = TestSnapshots.Edit(processId: 9999, ancestors: [TestSnapshots.Ancestor("Window", "frmMain", "LMS", handle: 200)]);
        _tracker.Owns(web).ShouldBeTrue();

        _tracker.Owns(TestSnapshots.Edit(processId: 777, ancestors: [TestSnapshots.Ancestor("Window", null, "Notepad", handle: 400)])).ShouldBeFalse();
    }

    [Fact]
    public void Hit_testing_compares_the_process_under_the_point()
    {
        _tracker.Select(LmsMain);

        _environment.ProcessUnderPoint = 4242;
        _tracker.IsOverTarget(new PixelPoint(5, 5)).ShouldBeTrue();
        _environment.ProcessUnderPoint = 777;
        _tracker.IsOverTarget(new PixelPoint(5, 5)).ShouldBeFalse();
    }

    [Fact]
    public void Clearing_forgets_the_target()
    {
        _tracker.Select(LmsMain);

        _tracker.Clear();

        _tracker.State.ShouldBe(TargetState.None);
        _settings.Current.LastTargetProcessName.ShouldBeNull();
    }

    private sealed class FakeEnvironment : ITargetEnvironment
    {
        private readonly HashSet<int> _exited = [];

        public List<AppWindow> Windows { get; } = [];

        public HashSet<int> Elevated { get; } = [];

        public Dictionary<nint, int> WindowOwners { get; } = [];

        public bool RecorderElevated { get; set; }

        public int ProcessUnderPoint { get; set; }

        public bool IsRecorderElevated => RecorderElevated;

        public void Exit(int processId) => _exited.Add(processId);

        public IReadOnlyList<AppWindow> GetAppWindows() => [.. Windows];

        public ElevationState ElevationOf(int processId) => Elevated.Contains(processId) ? ElevationState.Elevated : ElevationState.NotElevated;

        public IProcessWatch Watch(int processId) => new Watcher(() => _exited.Contains(processId));

        public int ProcessAtPoint(PixelPoint point) => ProcessUnderPoint;

        public int ProcessOfWindow(nint windowHandle) => WindowOwners.GetValueOrDefault(windowHandle);

        private sealed class Watcher(Func<bool> exited) : IProcessWatch
        {
            public bool HasExited => exited();

            public void Dispose()
            {
            }
        }
    }
}
