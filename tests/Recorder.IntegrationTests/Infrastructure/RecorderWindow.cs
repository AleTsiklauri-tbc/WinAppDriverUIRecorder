using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.Model;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>Reads and drives the recorder's own UI, which is automatable by design (spec 11.8).</summary>
internal sealed class RecorderWindow(AppSession session) : IDisposable
{
    /// <summary>Window class of the highlight overlay's edge and label windows.</summary>
    public const string HighlightClass = "WinFormsRecorder.Highlight";

    public AppSession Session => session;

    public Window Main => session.MainWindow;

    public nint MainWindowHandle => Main.Properties.NativeWindowHandle.Value;

    public AutomationElement PickButton => Wait.ForDescendant(Main, "Toolbar.Pick");

    public string NameOf(string automationId) => Wait.ForDescendant(Main, automationId).Properties.Name.Value;

    /// <summary>The text of a text box (variable name, locator value, Java line).</summary>
    public string ValueOf(string automationId) => Wait.ForDescendant(Main, automationId).Patterns.Value.Pattern.Value.Value;

    public void WaitForName(string automationId, string expected, TimeSpan? timeout = null) =>
        Wait.Until(
            () => Main.FindFirstDescendant(c => c.ByAutomationId(automationId))?.Properties.Name.ValueOrDefault == expected,
            timeout ?? TimeSpan.FromSeconds(10),
            $"'{automationId}' to read '{expected}'");

    public void WaitForValue(string automationId, string expected, TimeSpan? timeout = null) =>
        Wait.Until(
            () => Main.FindFirstDescendant(c => c.ByAutomationId(automationId))?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault == expected,
            timeout ?? TimeSpan.FromSeconds(10),
            $"'{automationId}' to hold '{expected}'");

    public void Invoke(string automationId)
    {
        var button = Wait.ForDescendant(Main, automationId);
        Wait.Until(() => button.Properties.IsEnabled.ValueOrDefault, TimeSpan.FromSeconds(60), $"'{automationId}' to be enabled");
        button.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>The Elements list entry with this variable name, or null.</summary>
    public AutomationElement? FindCapturedElement(string variableName) =>
        Main.FindFirstDescendant(c => c.ByAutomationId("Elements.List"))
            ?.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName(variableName)));

    public AutomationElement WaitForCapturedElement(string variableName) =>
        Wait.For(() => FindCapturedElement(variableName), TimeSpan.FromSeconds(10), $"'{variableName}' in the Elements list");

    public IReadOnlyList<DesktopWindow> VisibleHighlightWindows() =>
        [.. Desktop.WindowsOf(session.ProcessId, HighlightClass).Where(window => window.IsVisible)];

    /// <summary>The visible label chips: highlight windows thicker than an edge in both directions.</summary>
    public IReadOnlyList<PixelRect> VisibleChips(int thickness) =>
        [.. VisibleHighlightWindows().Select(window => window.Bounds).Where(rect => rect.Width > thickness && rect.Height > thickness)];

    /// <summary>A chip's background colour, read just inside its left edge (left of the icon, below the rounded corner).</summary>
    public static ScreenColor ColorOf(PixelRect chip) => Desktop.PixelAt(new PixelPoint(chip.X + 2, chip.Y + (chip.Height / 2)));

    /// <summary>
    /// True when four edge windows sit just outside <paramref name="bounds"/>: top and bottom span the corners,
    /// left and right do not.
    /// </summary>
    public bool IsOutlined(PixelRect bounds, int thickness)
    {
        var visible = VisibleHighlightWindows().Select(window => window.Bounds).ToList();
        return visible.Contains(new PixelRect(bounds.X - thickness, bounds.Y - thickness, bounds.Width + (2 * thickness), thickness))
            && visible.Contains(new PixelRect(bounds.X - thickness, bounds.Bottom, bounds.Width + (2 * thickness), thickness))
            && visible.Contains(new PixelRect(bounds.X - thickness, bounds.Y, thickness, bounds.Height))
            && visible.Contains(new PixelRect(bounds.Right, bounds.Y, thickness, bounds.Height));
    }

    public void Dispose() => session.Dispose();
}