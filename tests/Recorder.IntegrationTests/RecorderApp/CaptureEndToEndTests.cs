using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Phase 1 acceptance, end to end: the real recorder, the real SampleApp, real mouse and keyboard input.
/// These tests move the cursor (and put it back): exclude them with --filter "Category!=Input" while working.
/// </summary>
[Trait("Category", "Input")]
public sealed class CaptureEndToEndTests : IClassFixture<RecorderWithTargetFixture>
{
    private const string PickingStatus = "Picking · click a control in Recorder.SampleApp.exe · Esc cancels";
    private readonly RecorderWithTargetFixture _fixture;

    public CaptureEndToEndTests(RecorderWithTargetFixture fixture) => _fixture = fixture;

    private RecorderWindow Recorder => _fixture.Recorder;

    private SampleAppFixture Sample => _fixture.Sample;

    [Fact]
    public void On_start_the_recorder_attaches_to_the_app_it_remembers()
    {
        Recorder.NameOf("Toolbar.TargetSelector").ShouldBe(RecorderWithTargetFixture.TargetName);
        Recorder.NameOf("Status.Target").ShouldBe(RecorderWithTargetFixture.TargetName);
        Recorder.NameOf("Status.State").ShouldBe("Idle");
    }

    [Fact]
    public void The_capture_shortcut_captures_the_control_under_the_cursor_and_inspects_it()
    {
        var amount = Sample.ById("txtAmount");
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(Desktop.CenterOf(amount));

            Desktop.PressChord(Desktop.VkControl, Desktop.VkShift, Desktop.VkX);

            Recorder.WaitForCapturedElement("amountInput");
        }

        Recorder.NameOf("Inspector.Title").ShouldBe("Edit · txtAmount");
        Recorder.NameOf("Inspector.Page").ShouldBe("Window: frmLoan");
    }

    [Fact]
    public void Pick_mode_outlines_the_hovered_control_and_captures_the_clicked_one_without_clicking_it()
    {
        var bounds = Desktop.BoundsOf(Sample.ById("btnSaveCustomer"));
        var thickness = Desktop.HighlightThicknessAt(bounds.Center);
        var statusBefore = Sample.StatusText;
        IReadOnlyList<DesktopWindow> highlight;
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(bounds.Center);
            Recorder.PickButton.Patterns.Toggle.Pattern.Toggle();
            Recorder.WaitForName("Status.State", PickingStatus);
            Sample.BringToFrontAt(bounds.Center);
            highlight = Wait.For(() => Recorder.VisibleHighlightWindows() is { Count: 5 } windows ? windows : null, TimeSpan.FromSeconds(5), "the highlight around the button");

            Desktop.LeftClick();

            Recorder.WaitForCapturedElement("saveCustomerButton");
        }

        // Four edges just outside the control, and a label chip that does not cover it.
        var rects = highlight.Select(window => window.Bounds).ToList();
        rects.ShouldContain(new PixelRect(bounds.X - thickness, bounds.Y - thickness, bounds.Width + (2 * thickness), thickness));
        rects.ShouldContain(new PixelRect(bounds.X - thickness, bounds.Bottom, bounds.Width + (2 * thickness), thickness));
        rects.ShouldContain(new PixelRect(bounds.X - thickness, bounds.Y, thickness, bounds.Height));
        rects.ShouldContain(new PixelRect(bounds.Right, bounds.Y, thickness, bounds.Height));
        var chip = rects.Single(rect => rect.Width > thickness && rect.Height > thickness);
        (chip.Bottom <= bounds.Y - thickness || chip.Y >= bounds.Bottom + thickness).ShouldBeTrue($"the label {chip} covers the control {bounds}");

        // The click was for the recorder: the app never saw it.
        Sample.StatusText.ShouldBe(statusBefore);
        Recorder.WaitForName("Status.State", "Idle");
        Recorder.PickButton.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.Off);
        Wait.Until(() => Recorder.VisibleHighlightWindows().Count == 0, TimeSpan.FromSeconds(5), "the highlight to disappear");
    }

    [Fact]
    public void Escape_leaves_pick_mode_without_capturing_anything()
    {
        var insured = Sample.ById("chkInsured");
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(Desktop.CenterOf(insured));
            Recorder.PickButton.Patterns.Toggle.Pattern.Toggle();
            Recorder.WaitForName("Status.State", PickingStatus);
            Sample.BringToFrontAt(Desktop.CenterOf(insured));
            Wait.Until(() => Recorder.VisibleHighlightWindows().Count == 5, TimeSpan.FromSeconds(5), "the highlight around the check box");

            Desktop.PressChord(Desktop.VkEscape);

            Recorder.WaitForName("Status.State", "Idle");
        }

        Wait.Until(() => Recorder.VisibleHighlightWindows().Count == 0, TimeSpan.FromSeconds(5), "the highlight to disappear");
        Recorder.FindCapturedElement("insuredCheckbox").ShouldBeNull();
    }

    /// <summary>Phase 1 acceptance: hovering every SampleApp control highlights the correct rectangle.</summary>
    [Fact]
    public void Hovering_any_control_in_pick_mode_outlines_exactly_the_element_under_the_cursor()
    {
        // What an independent UI Automation client sees under each control's center, read before any highlight is shown.
        Desktop.Raise(Sample.MainWindowHandle);
        var automation = Sample.Session.Automation;
        var monitors = Monitors.All();
        var offEveryMonitor = 0;
        var probes = new Dictionary<string, (PixelPoint Point, PixelRect Bounds, string What)>(StringComparer.Ordinal);
        foreach (var element in Sample.Session.MainWindow.FindAllDescendants())
        {
            var bounds = Desktop.BoundsOf(element);
            if (element.Properties.IsOffscreen.ValueOrDefault || bounds.IsEmpty)
            {
                continue;
            }

            // No cursor reaches a point off every monitor: at 150 % the SampleApp is taller than a 1920×1080 screen.
            if (!monitors.Any(monitor => monitor.Bounds.Contains(bounds.Center)))
            {
                offEveryMonitor++;
                continue;
            }

            // Over a menu or tool strip a hit test stops at the strip; the recorder outlines the item under the point (F-25).
            var under = ItemInStrip(automation.FromPoint(new System.Drawing.Point(bounds.Center.X, bounds.Center.Y)), bounds.Center);
            var underBounds = Desktop.BoundsOf(under);
            if (under.Properties.ProcessId.ValueOrDefault == Sample.Session.ProcessId && !underBounds.IsEmpty)
            {
                var key = string.Join('.', under.Properties.RuntimeId.ValueOrDefault ?? []);
                probes.TryAdd(key, (bounds.Center, underBounds, $"{under.Properties.ControlType.ValueOrDefault} '{under.Properties.AutomationId.ValueOrDefault}' at {bounds.Center}"));
            }
        }

        probes.Count.ShouldBeGreaterThan(30);
        var failures = new List<string>();
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(probes.Values.First().Point);
            Recorder.PickButton.Patterns.Toggle.Pattern.Toggle();
            Recorder.WaitForName("Status.State", PickingStatus);
            Sample.BringToFrontAt(probes.Values.First().Point);

            foreach (var (point, bounds, what) in probes.Values)
            {
                Desktop.MoveCursor(point);
                var thickness = Desktop.HighlightThicknessAt(point);
                try
                {
                    Wait.Until(() => Recorder.IsOutlined(bounds, thickness), TimeSpan.FromSeconds(3), what);
                }
                catch (TimeoutException)
                {
                    failures.Add($"{what}: expected an outline around {bounds}, saw [{string.Join("; ", Recorder.VisibleHighlightWindows().Select(w => w.Bounds))}]");
                }
            }

            Desktop.PressChord(Desktop.VkEscape);
            Recorder.WaitForName("Status.State", "Idle");
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Hovered {probes.Count} distinct elements, {failures.Count} without the right outline; skipped {offEveryMonitor} elements whose center is off every monitor.");
        failures.ShouldBeEmpty();
    }

    private static AutomationElement ItemInStrip(AutomationElement element, PixelPoint point)
    {
        for (var depth = 0; depth < 3 && element.Properties.ControlType.ValueOrDefault is ControlType.MenuBar or ControlType.Menu or ControlType.ToolBar; depth++)
        {
            var item = element.FindAllChildren().FirstOrDefault(child => !child.Properties.IsOffscreen.ValueOrDefault && Desktop.BoundsOf(child).Contains(point));
            if (item is null)
            {
                break;
            }

            element = item;
        }

        return element;
    }

    /// <summary>Phase 1 acceptance: the recorder stays responsive while the SampleApp is frozen by "Freeze 5 s".</summary>
    [Fact]
    public void While_the_app_is_frozen_the_recorder_stays_responsive_and_recovers_without_the_mouse_moving()
    {
        // Everything that needs the SampleApp is read before it freezes.
        var freeze = Sample.ById("btnFreeze");
        var amount = Desktop.CenterOf(Sample.ById("txtAmount"));
        var insured = Desktop.BoundsOf(Sample.ById("chkInsured"));
        var thickness = Desktop.HighlightThicknessAt(insured.Center);
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(amount);
            Recorder.PickButton.Patterns.Toggle.Pattern.Toggle();
            Recorder.WaitForName("Status.State", PickingStatus);
            Sample.BringToFrontAt(amount);

            Click.Post(freeze);
            Thread.Sleep(300);
            Desktop.MoveCursor(insured.Center); // the next lookup has to ask the frozen app

            // These reads go through the recorder's UI thread: they only succeed if it is not blocked.
            Recorder.WaitForName("Status.State", "Recorder.SampleApp.exe is not responding…", TimeSpan.FromSeconds(4));
            var read = System.Diagnostics.Stopwatch.StartNew();
            Recorder.NameOf("Status.Target").ShouldBe(RecorderWithTargetFixture.TargetName);
            read.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

            // Once the app answers again, the hovered control is outlined without moving the mouse.
            Recorder.WaitForName("Status.State", PickingStatus, TimeSpan.FromSeconds(10));
            Wait.Until(() => Recorder.IsOutlined(insured, thickness), TimeSpan.FromSeconds(5), "the outline around the check box");

            Desktop.PressChord(Desktop.VkEscape);
            Recorder.WaitForName("Status.State", "Idle");
        }
    }
}
