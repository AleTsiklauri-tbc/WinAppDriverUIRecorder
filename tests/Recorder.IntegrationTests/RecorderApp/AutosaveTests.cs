using FlaUI.Core.AutomationElements;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Spec 12, never lose work: a recording an earlier session left unsaved is offered back at start, and what is restored is
/// kept while it stays unsaved, across a close. Spec 11.2: the window's size and splits are remembered.
/// </summary>
public sealed class AutosaveTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _home = RecorderLauncher.NewDataHome();
    private AppSession? _session;

    public void Dispose()
    {
        _session?.Dispose();
        RecorderLauncher.DeleteDataHome(_home, nameof(AutosaveTests));
    }

    private string Folder => Path.Combine(_home, "local", "autosave");

    private string[] Kept() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*" + RecordingStore.Extension) : [];

    [Fact]
    public void An_unsaved_recording_comes_back_and_stays_kept_until_it_is_dismissed()
    {
        // What a session that crashed leaves: its recording, without the lock Windows deleted with the process.
        Directory.CreateDirectory(Folder);
        var leftover = Path.Combine(Folder, "20261103-091500-0badf00d" + RecordingStore.Extension);
        var at = DateTimeOffset.Now;
        RecordingStore.Save(leftover, new RecordingDocument("Recorder.SampleApp.exe", at, at, [], [new RecordedStep { Kind = StepKind.Comment, Comment = "Check the totals", At = at }]));

        var main = Launch();
        Wait.ForDescendant(main, "Banner.Message").Properties.Name.Value.ShouldStartWith("Restore your unsaved recording from ");
        Wait.ForDescendant(main, "Banner.Action").Patterns.Invoke.Pattern.Invoke();

        Wait.Until(() => Steps(main) == 1, Timeout, "the restored step");
        main.FindFirstDescendant(c => c.ByAutomationId("Banner.Action")).ShouldBeNull();
        Wait.Until(() => !File.Exists(leftover), Timeout, "the earlier session's file to go");

        // Restored but not saved: autosave keeps it under this session's name, and a clean close leaves it for next time.
        Wait.Until(() => Kept().Length == 1, Timeout, "autosave to keep the restored recording");
        _session!.CloseAndWaitForExit(Timeout).ShouldBe(0);
        _session.Dispose();
        _session = null;
        Kept().Length.ShouldBe(1);
        Directory.GetFiles(Folder, "*.lock").ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_home, "roaming", "settings.json")).ShouldContain("\"leftPaneWidth\"");

        // Next start: offered again; dismissing it forgets it.
        main = Launch();
        Wait.ForDescendant(main, "Banner.Message").Properties.Name.Value.ShouldStartWith("Restore your unsaved recording from ");
        Wait.ForDescendant(main, "Banner.Dismiss").Patterns.Invoke.Pattern.Invoke();
        Wait.Until(() => Kept().Length == 0, Timeout, "the dismissed recording to be deleted");
        Steps(main).ShouldBe(0);
    }

    [Fact]
    public void Nothing_is_offered_after_a_session_with_nothing_unsaved()
    {
        var main = Launch();
        Wait.ForDescendant(main, "Status.State");

        _session!.CloseAndWaitForExit(Timeout).ShouldBe(0);
        _session.Dispose();
        _session = null;

        Kept().ShouldBeEmpty();
        main = Launch();
        Wait.ForDescendant(main, "Status.State");
        main.FindFirstDescendant(c => c.ByAutomationId("Banner.Message")).ShouldBeNull();
    }

    private Window Launch()
    {
        _session = RecorderLauncher.Launch(_home);
        return _session.MainWindow;
    }

    private static int Steps(Window main) =>
        main.FindFirstDescendant(c => c.ByAutomationId("Steps.List"))?.FindAllChildren(c => c.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length ?? 0;
}
