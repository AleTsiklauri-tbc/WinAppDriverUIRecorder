using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.CodeGen;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.IntegrationTests.Infrastructure;
using Recorder.Tests.Java;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Phase 3 acceptance, end to end: the real recorder records the SampleApp flow driven with the real mouse and keyboard,
/// saves it through the Save dialog, and the password typed into the app appears neither in the file nor in the logs.
/// These tests move the cursor and type (and put the cursor back): exclude them with --filter "Category!=Input".
/// </summary>
[Trait("Category", "Input")]
public sealed class RecordingEndToEndTests : IClassFixture<RecorderWithTargetFixture>
{
    // Unique enough to be found anywhere it might leak.
    private const string Secret = "S3cret-Pa55-Lms!";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // A person rests the cursor before clicking; the recorder uses that moment to know the control (spec 9.2).
    private static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(450);
    private readonly RecorderWithTargetFixture _fixture;

    public RecordingEndToEndTests(RecorderWithTargetFixture fixture) => _fixture = fixture;

    private RecorderWindow Recorder => _fixture.Recorder;

    private SampleAppFixture Sample => _fixture.Sample;

    [Fact]
    public void Recording_the_acceptance_flow_gives_exactly_its_eight_steps_and_the_password_is_written_nowhere()
    {
        var main = Sample.Session.MainWindow;
        var path = Path.Combine(_fixture.DataHome, "flow" + RecordingStore.Extension);
        using (Desktop.KeepCursor())
        {
            Recorder.Invoke("Toolbar.Record");
            Wait.Until(() => Recorder.NameOf("Status.State").StartsWith("Recording", StringComparison.Ordinal), Timeout, "recording to start");
            var miniBar = Wait.For(Recorder.FindMiniBar, Timeout, "the mini-bar");

            // Spec 11.4: at the top center of the monitor showing the app, whatever that monitor's scale.
            var monitor = Monitors.Of(Monitors.BoundsOf(Sample.MainWindowHandle));
            Wait.Until(() => IsTopCenter(Desktop.BoundsOf(miniBar), monitor.WorkArea), Timeout, () => $"the mini-bar at the top center of {monitor}; it is at {Desktop.BoundsOf(miniBar)}");

            // type amount → type password → select "GEL" → tick the check box
            ClickOn(Sample.ById("txtAmount"));
            Desktop.TypeText("5000");
            ClickOn(Sample.ById("txtPassword"));
            Desktop.TypeText(Secret);
            var combo = Sample.ById("cmbCurrency");
            ClickOn(combo);
            ClickOn(Wait.For(() => combo.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName("GEL"))), Timeout, "the GEL item"), raise: false);
            ClickOn(Sample.ById("chkInsured"));

            // File › Export › PDF
            ClickOn(MenuItem(main, "File"));
            ClickOn(MenuItem(main, "Export"), raise: false);
            ClickOn(MenuItem(main, "PDF"), raise: false);
            Wait.Until(() => Sample.StatusText == "Exported to PDF", Timeout, "the export to run");

            // Show message, then OK on the message box
            ClickOn(Sample.ById("btnShowMessage"));
            var box = Wait.For(() => main.FindFirstChild(c => c.ByClassName("#32770")), Timeout, "the message box");
            ClickOn(Wait.ForDescendant(box, "2"), raise: false);
            Wait.Until(() => main.FindFirstChild(c => c.ByClassName("#32770")) is null, Timeout, "the message box to close");

            // The Status cell of row 2 (rows count from 0)
            ClickOn(Wait.For(() => Sample.ById("dgvLoans").FindFirstDescendant(c => c.ByName("Status Row 2, Not sorted.")), Timeout, "the Status cell of row 2"));

            // F9 stops: a global shortcut of the recorder, never recorded as a key press.
            Desktop.PressChord(Desktop.VkF9);
            Recorder.WaitForName("Status.State", "Idle", TimeSpan.FromSeconds(20));
        }

        Wait.Until(() => Recorder.FindMiniBar() is null, Timeout, "the mini-bar to close");
        Wait.Until(() => Recorder.StepSentences().Count == 8, Timeout, "eight steps");
        Recorder.StepSentences().ShouldBe([
            "Type “5000” into Amount",
            "Type the test value passwordInput into Password",
            "Select “GEL” in Currency",
            "Tick Insured",
            "Click File › Export › PDF",
            "Click Show message",
            "Click OK",
            "Click row 2, “Status” in loansGrid",
        ]);

        SaveThroughTheDialog(path);
        var json = File.ReadAllText(path);
        json.ShouldNotContain(Secret);

        // The message box has no AutomationId: it is known by its caption while recording, and kept numbered in the file (D-70).
        json.ShouldNotContain("Information");
        json.ShouldNotContain("\"name:");
        json.ShouldContain("\"id\": \"window:1\"");
        var steps = RecordingStore.Load(path).Steps;
        steps.Select(step => step.Kind).ShouldBe([
            StepKind.SetText, StepKind.SetText, StepKind.SelectOption, StepKind.SetChecked,
            StepKind.MenuPath, StepKind.Click, StepKind.Click, StepKind.GridCellClick]);
        (steps[0].Text, steps[1].Text, steps[1].TestDataKey, steps[2].Text, steps[3].State).ShouldBe(("5000", null, "passwordInput", "GEL", true));
        steps[4].MenuItems.ShouldBe(["File", "Export", "PDF"]);
        (steps[7].RowIndex, steps[7].ColumnHeader).ShouldBe((2, "Status"));
        steps.ShouldAllBe(step => step.Warnings.Count == 0);

        foreach (var log in Directory.GetFiles(Path.Combine(_fixture.DataHome, "local", "logs"), "*.log"))
        {
            ReadShared(log).ShouldNotContain(Secret, customMessage: Path.GetFileName(log));
        }

        GeneratedJavaCompiles(path);
    }

    // Phase 4 acceptance (spec 14): the code pane shows the test for this recording, and the Java generated from it compiles
    // against the target framework's base classes and the real java-client 7.6.0 jars. Skipped, not passed, without a JDK.
    private void GeneratedJavaCompiles(string path)
    {
        var recording = RecordingStore.Load(path);
        var catalog = new ElementCatalog();
        catalog.Load(recording.Elements, recording.PageClassNames);
        var code = new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == CodeProfile.DefaultId)).Generate(catalog, recording.Steps, "flow");
        code.Error.ShouldBeNull();
        var test = code.Of(CodeFileKind.Test).Single();
        test.Text.ShouldContain(".enterPassword(ConfigReader.getProperty(\"passwordInput\"))");
        string.Concat(code.Files.Select(file => file.Text)).ShouldNotContain(Secret);

        Wait.ForDescendant(Recorder.Main, "Code.Tab.Test").Patterns.SelectionItem.Pattern.Select();
        Wait.Until(() => Recorder.ValueOf("Code.Editor").ReplaceLineEndings("\n") == test.Text, Timeout, "the code pane to show the test");

        var (exitCode, output) = JavaCompileCheck.Run(code.Files, TestPaths.RepoRoot);
        Assert.SkipWhen(exitCode is JavaCompileCheck.NoJdk or JavaCompileCheck.NoJars, output);
        exitCode.ShouldBe(JavaCompileCheck.Compiled, output);
    }

    private static bool IsTopCenter(PixelRect bar, PixelRect work) =>
        Math.Abs(bar.Center.X - work.Center.X) <= 2 && bar.Y >= work.Y && bar.Y <= work.Y + 24 && bar.Right <= work.Right && bar.Bottom <= work.Bottom;

    private static AutomationElement MenuItem(AutomationElement main, string name) =>
        Wait.For(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName(name))), Timeout, "the " + name + " menu item");

    // The log is still open in the recorder.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Moves the real cursor to the control, rests like a person, and clicks with the real mouse.
    private void ClickOn(AutomationElement element, bool raise = true)
    {
        var point = Desktop.CenterOf(element);
        if (raise)
        {
            Sample.BringToFrontAt(point);
        }
        else
        {
            Wait.Until(() => Desktop.ProcessAtPoint(point) == Sample.Session.ProcessId, Timeout, $"the SampleApp at {point}");
        }

        Desktop.MoveCursor(point);
        Thread.Sleep(Rest);
        Desktop.LeftClick();
        Thread.Sleep(TimeSpan.FromMilliseconds(350));
    }

    // Ctrl+S in the recorder opens the Save dialog; its file name box has the focus.
    private void SaveThroughTheDialog(string path)
    {
        Recorder.Main.Focus();
        Thread.Sleep(300);
        Desktop.PressChord(Desktop.VkControl, Desktop.VkS);
        var dialog = Wait.For(
            () => Recorder.Main.FindFirstChild(c => c.ByClassName("#32770"))
                ?? Recorder.Session.Automation.GetDesktop().FindFirstChild(c => c.ByClassName("#32770").And(c.ByProcessId(Recorder.Session.ProcessId))),
            Timeout,
            "the Save dialog");
        Thread.Sleep(500);
        var fileName = Wait.ForDescendant(dialog, "1001");
        fileName.Focus();
        Desktop.PressChord(Desktop.VkControl, 0x41);
        Desktop.TypeText(path);
        Desktop.PressChord(Desktop.VkReturn);
        Wait.Until(() => File.Exists(path), Timeout, "the recording file");
    }
}
