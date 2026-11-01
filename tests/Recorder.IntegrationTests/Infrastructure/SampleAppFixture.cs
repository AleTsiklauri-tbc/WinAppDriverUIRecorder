using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.Model;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>One SampleApp instance shared by the tests of a class.</summary>
public sealed class SampleAppFixture : IDisposable
{
    public SampleAppFixture()
    {
        Session = AppSession.Launch(new ProcessStartInfo(TestPaths.SampleAppExe));
        Loan = Wait.ForDescendant(Session.MainWindow, "frmLoan");
    }

    internal AppSession Session { get; }

    /// <summary>The maximized "Loan" MDI child holding the spec 13.1 controls.</summary>
    internal AutomationElement Loan { get; }

    internal nint MainWindowHandle => Session.MainWindow.Properties.NativeWindowHandle.Value;

    internal AutomationElement ById(string automationId) => Wait.ForDescendant(Session.MainWindow, automationId);

    /// <summary>
    /// Puts the SampleApp in front of the recorder at <paramref name="point"/>. Driving the recorder's toolbar through UI
    /// Automation activates the recorder and lifts it over the app (as a real click on it would), so tests call this
    /// after entering Pick mode, not before.
    /// </summary>
    internal void BringToFrontAt(PixelPoint point) =>
        Wait.Until(
            () =>
            {
                Desktop.Raise(MainWindowHandle);
                Thread.Sleep(100);
                return Desktop.ProcessAtPoint(point) == Session.ProcessId;
            },
            TimeSpan.FromSeconds(5),
            $"the SampleApp in front at {point}");

    /// <summary>The text of the status strip, which the SampleApp's buttons and menus update.</summary>
    internal string StatusText =>
        Wait.For(() => ById("statusStrip").FindFirstChild(c => c.ByControlType(ControlType.Text)), TimeSpan.FromSeconds(5), "the status text")
            .Properties.Name.Value;

    /// <summary>Opens Window › Web form (Angular/Kendo-like content in WebView2) and returns its date input.</summary>
    internal AutomationElement OpenWebFormDateInput()
    {
        var main = Session.MainWindow;
        var windowMenu = Wait.For(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Window"))), TimeSpan.FromSeconds(5), "the Window menu");
        windowMenu.Patterns.ExpandCollapse.Pattern.Expand();
        var webFormItem = Wait.For(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Web form"))), TimeSpan.FromSeconds(5), "Window › Web form");
        webFormItem.Patterns.Invoke.Pattern.Invoke();
        return Wait.For(() => main.FindFirstDescendant(c => c.ByAutomationId("datepicker-3")), TimeSpan.FromSeconds(30), "the web form's date input");
    }

    public void Dispose() => Session.Dispose();
}
