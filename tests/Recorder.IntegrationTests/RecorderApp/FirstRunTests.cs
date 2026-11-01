using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>Phase 0 acceptance: the app launches into the first-run state (spec 11.3) and is itself automatable (11.8).</summary>
public class FirstRunTests : IClassFixture<RecorderAppFixture>
{
    private readonly RecorderAppFixture _app;

    public FirstRunTests(RecorderAppFixture app) => _app = app;

    private Window Main => _app.Session.MainWindow;

    private string NameOf(string automationId) => Wait.ForDescendant(Main, automationId).Properties.Name.Value;

    [Fact]
    public void The_window_is_titled_WinForms_Recorder() =>
        Main.Title.ShouldBe("WinForms Recorder");

    [Fact]
    public void The_center_pane_shows_the_three_first_run_steps()
    {
        NameOf("FirstRun.Step1").ShouldBe("Choose the app you want to automate");
        NameOf("FirstRun.Step2").ShouldBe("Hover a control and press Ctrl+Shift+X to get its locator");
        NameOf("FirstRun.Step3").ShouldBe("Or press F9 to record a flow");
    }

    [Fact]
    public void Empty_panes_explain_what_will_appear_there()
    {
        NameOf("Steps.Empty").ShouldBe("No steps yet. Press F9 and use the app as you normally would.");
        NameOf("Code.Empty").ShouldBe("Code for captured controls and recorded steps appears here.");

        Wait.ForDescendant(Main, "Tab.Elements").Patterns.SelectionItem.Pattern.Select();
        NameOf("Elements.Empty").ShouldBe("Captured controls appear here, grouped by window.");
        Wait.ForDescendant(Main, "Tab.Steps").Patterns.SelectionItem.Pattern.Select();
    }

    [Fact]
    public void The_status_bar_shows_idle_and_no_target()
    {
        NameOf("Status.State").ShouldBe("Idle");
        NameOf("Status.Target").ShouldBe("No app selected");
    }

    [Fact]
    public void Every_button_has_an_AutomationId_and_an_accessible_name()
    {
        var buttons = Main.FindAllDescendants(c => c.ByControlType(ControlType.Button));

        buttons.Length.ShouldBeGreaterThanOrEqualTo(11);
        buttons.ShouldAllBe(b => !string.IsNullOrEmpty(b.Properties.AutomationId.ValueOrDefault)
            && !string.IsNullOrEmpty(b.Properties.Name.ValueOrDefault));
    }

    [Fact]
    public void The_toolbar_starts_with_the_target_selector() =>
        NameOf("Toolbar.TargetSelector").ShouldBe("Choose app");

    [Fact]
    public void Logs_are_written_only_to_the_local_data_folder()
    {
        var logs = Path.Combine(_app.DataHome, "local", "logs");

        var log = Wait.For(() => Directory.Exists(logs) ? Directory.GetFiles(logs, "recorder-*.log").FirstOrDefault() : null, TimeSpan.FromSeconds(10), "the log file");
        using var reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var text = reader.ReadToEnd();

        text.ShouldMatch(@"WinForms Recorder \d+\.\d+\.\d+ started");
        text.ShouldNotContain("Press Ctrl+C");
        Directory.GetFileSystemEntries(Path.Combine(_app.DataHome, "local")).ShouldBe([logs]);
    }
}
