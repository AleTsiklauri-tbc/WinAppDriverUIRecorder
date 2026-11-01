using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.SampleApp;

/// <summary>
/// Spike tests for spec 7.1 "separate top-level windows". In Win32 terms, MessageBox, modal forms and popups
/// are separate owned top-level windows; in the UIA tree they appear as children of their owner window.
/// </summary>
public class WindowFindingsTests : IClassFixture<SampleAppFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly SampleAppFixture _app;

    public WindowFindingsTests(SampleAppFixture app) => _app = app;

    private Window Main => _app.Session.MainWindow;

    [Fact]
    public void A_MessageBox_is_a_Win32_dialog_nested_under_its_owner_not_under_the_desktop()
    {
        Click.Post(_app.ById("btnShowMessage"));
        var box = Wait.For(() => Main.FindFirstChild(c => c.ByControlType(ControlType.Window).And(c.ByName("Information"))), Timeout, "the MessageBox");
        try
        {
            box.Properties.ClassName.Value.ShouldBe("#32770");
            box.Properties.FrameworkId.Value.ShouldBe("Win32");
            DesktopWindowNamesOfSampleApp().ShouldNotContain("Information");

            // Win32 dialog control IDs are stable AutomationIds: IDOK = 2.
            var ok = Wait.For(() => box.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("OK"))), Timeout, "the OK button");
            ok.Properties.AutomationId.Value.ShouldBe("2");
            Click.Post(ok);
        }
        finally
        {
            WaitUntilClosed(() => Main.FindFirstChild(c => c.ByName("Information")));
        }
    }

    [Fact]
    public void A_modal_form_is_nested_under_its_owner_with_its_Form_Name_as_AutomationId()
    {
        Click.Post(_app.ById("btnOpenDialog"));
        var dialog = Wait.For(() => Main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")), Timeout, "the modal dialog");
        try
        {
            dialog.ControlType.ShouldBe(ControlType.Window);
            dialog.Properties.FrameworkId.Value.ShouldBe("WinForm");
            DesktopWindowNamesOfSampleApp().ShouldNotContain("Confirm");
            Click.Post(Wait.ForDescendant(dialog, "btnCancel"));
        }
        finally
        {
            WaitUntilClosed(() => Main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")));
        }
    }

    [Fact]
    public void A_popup_form_is_nested_under_its_owner()
    {
        Click.Post(_app.ById("btnPurpose"));
        var popup = Wait.For(() => Main.FindFirstChild(c => c.ByAutomationId("PopupTreeForm")), Timeout, "the popup");
        try
        {
            var tree = Wait.ForDescendant(popup, "PopupTreeView");
            tree.ControlType.ShouldBe(ControlType.Tree);
            tree.FindAllChildren().Select(n => n.Properties.Name.Value).ShouldBe(["სამომხმარებლო", "ბიზნესი"]);
            Click.PostClose(popup);
        }
        finally
        {
            WaitUntilClosed(() => Main.FindFirstChild(c => c.ByAutomationId("PopupTreeForm")));
        }
    }

    private List<string> DesktopWindowNamesOfSampleApp() =>
        _app.Session.Automation.GetDesktop()
            .FindAllChildren(c => c.ByProcessId(_app.Session.ProcessId))
            .Select(w => w.Properties.Name.ValueOrDefault ?? string.Empty)
            .ToList();

    private static void WaitUntilClosed(Func<AutomationElement?> find) =>
        Wait.Until(() => find() is null, Timeout, "the window to close");
}
