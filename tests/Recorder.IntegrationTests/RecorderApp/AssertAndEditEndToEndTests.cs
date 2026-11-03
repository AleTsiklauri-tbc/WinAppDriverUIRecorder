using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Phase 3, end to end: Assert mode (spec 9.6) through the global shortcut and the picker, then deleting a step and
/// undoing it (spec 11.6). Uses the real mouse and keyboard (and puts the cursor back).
/// </summary>
[Trait("Category", "Input")]
public sealed class AssertAndEditEndToEndTests : IClassFixture<RecorderWithTargetFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly RecorderWithTargetFixture _fixture;

    public AssertAndEditEndToEndTests(RecorderWithTargetFixture fixture) => _fixture = fixture;

    private RecorderWindow Recorder => _fixture.Recorder;

    private SampleAppFixture Sample => _fixture.Sample;

    [Fact]
    public void An_assertion_picked_in_assert_mode_follows_the_typed_value_and_a_deleted_step_comes_back_with_undo()
    {
        var amount = Sample.ById("txtAmount");
        using (Desktop.KeepCursor())
        {
            Recorder.Invoke("Toolbar.Record");
            Wait.Until(() => Recorder.NameOf("Status.State").StartsWith("Recording", StringComparison.Ordinal), Timeout, "recording to start");
            ClickOn(amount);
            Desktop.TypeText("750");

            // Ctrl+Shift+A: a global shortcut of the recorder, never recorded as a key press.
            Desktop.PressChord(Desktop.VkControl, Desktop.VkShift, 0x41);
            Recorder.WaitForName("Status.State", "Assert · click a control in Recorder.SampleApp.exe · Esc cancels");
            ClickOn(amount);

            var picker = Wait.For(
                () => Recorder.Session.Automation.GetDesktop().FindFirstChild(c => c.ByAutomationId("AssertPicker").And(c.ByProcessId(Recorder.Session.ProcessId))),
                Timeout,
                "the assertion picker");

            // Just below and right of the click, on the click's monitor, whatever that monitor's scale.
            var click = Desktop.CenterOf(amount);
            var monitor = Monitors.At(click);
            Wait.Until(() => IsNextTo(click, Desktop.BoundsOf(picker), monitor.WorkArea), Timeout, () => $"the picker next to {click} on {monitor}; it is at {Desktop.BoundsOf(picker)}");
            var options = Wait.ForDescendant(picker, "AssertPicker.Options").FindAllChildren(c => c.ByControlType(ControlType.ListItem)).Select(o => o.Properties.Name.Value).ToList();
            options.ShouldBe(["Value equals “750”", "Is enabled", "Is visible"]);
            Desktop.PressChord(Desktop.VkReturn);
            Wait.Until(() => Recorder.FindMiniBar()?.FindFirstDescendant(c => c.ByAutomationId("MiniBar.Steps"))?.Properties.Name.ValueOrDefault == "2 steps", Timeout, "the assertion step");

            Wait.ForDescendant(Recorder.FindMiniBar()!, "MiniBar.Stop").Patterns.Invoke.Pattern.Invoke();
            Recorder.WaitForName("Status.State", "Idle", TimeSpan.FromSeconds(20));
        }

        Wait.Until(() => Recorder.StepSentences().Count == 2, Timeout, "two steps");
        Recorder.StepSentences().ShouldBe(["Type “750” into Amount", "Check that Amount contains “750”"]);

        // Click the last step (which brings the recorder to the front), press Del, then Undo brings it back.
        var list = Wait.ForDescendant(Recorder.Main, "Steps.List");
        var point = Desktop.CenterOf(list.FindAllChildren(c => c.ByControlType(ControlType.ListItem))[^1]);
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Recorder.MainWindowHandle);
            Wait.Until(() => Desktop.ProcessAtPoint(point) == Recorder.Session.ProcessId, Timeout, "the recorder in front");
            Desktop.MoveCursor(point);
            Desktop.LeftClick();
            Thread.Sleep(300);
            Desktop.PressChord(0x2E);
        }

        Wait.Until(() => Recorder.StepSentences().Count == 1, Timeout, "the step to be deleted");
        Recorder.Invoke("Toolbar.Undo");
        Wait.Until(() => Recorder.StepSentences().Count == 2, Timeout, "the step to come back");
        Recorder.StepSentences()[^1].ShouldBe("Check that Amount contains “750”");
    }

    // 12 and 16 pixels below and right of the click, inside the work area.
    private static bool IsNextTo(PixelPoint click, PixelRect picker, PixelRect work) =>
        Math.Abs(picker.X - (click.X + 12)) <= 2 && Math.Abs(picker.Y - (click.Y + 16)) <= 2 && picker.Right <= work.Right && picker.Bottom <= work.Bottom;

    private void ClickOn(AutomationElement element)
    {
        var point = Desktop.CenterOf(element);
        Sample.BringToFrontAt(point);
        Desktop.MoveCursor(point);
        Thread.Sleep(450);
        Desktop.LeftClick();
        Thread.Sleep(350);
    }
}
