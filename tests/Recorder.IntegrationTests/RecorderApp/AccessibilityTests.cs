using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>Spec 11.8: every interactive control has an AutomationId and a name, in the main window, Settings and the cheat sheet.</summary>
public class AccessibilityTests : IClassFixture<RecorderAppFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly ControlType[] Interactive =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.RadioButton, ControlType.ComboBox, ControlType.Edit, ControlType.TabItem,
        ControlType.Thumb, ControlType.Hyperlink, ControlType.Slider, ControlType.SplitButton, ControlType.List, ControlType.DataGrid, ControlType.Tree,
    ];

    // The first control of each Settings section, in spec 12's order.
    private static readonly (string Section, string FirstControl)[] SettingsSections =
    [
        ("Settings.Section.General", "Settings.MinimizeOnRecord"),
        ("Settings.Section.Shortcuts", "Settings.Shortcut.Capture"),
        ("Settings.Section.Recording", "Settings.RecordSpecialKeys"),
        ("Settings.Section.Code", "Settings.Profile"),
        ("Settings.Section.WinAppDriver", "Settings.WinAppDriverAddress"),
        ("Settings.Section.Advanced", "Settings.DetailedLogs"),
    ];

    private readonly RecorderAppFixture _app;

    public AccessibilityTests(RecorderAppFixture app) => _app = app;

    private Window Main => _app.Session.MainWindow;

    [Fact]
    public void Every_control_in_the_main_window_has_an_AutomationId_and_a_name()
    {
        var problems = Unlabelled(Main).ToList();
        Wait.ForDescendant(Main, "Tab.Elements").Patterns.SelectionItem.Pattern.Select();
        Wait.ForDescendant(Main, "Elements.Empty");
        problems.AddRange(Unlabelled(Main));
        Wait.ForDescendant(Main, "Tab.Steps").Patterns.SelectionItem.Pattern.Select();

        problems.Distinct().ShouldBeEmpty();
    }

    [Fact]
    public void Every_control_in_every_section_of_Settings_has_an_AutomationId_and_a_name()
    {
        var settings = SettingsWindow.Open(_app.Session);
        try
        {
            var problems = new List<string>();
            foreach (var (section, first) in SettingsSections)
            {
                Wait.ForDescendant(settings, section).Patterns.SelectionItem.Pattern.Select();
                Wait.ForDescendant(settings, first);
                problems.AddRange(Unlabelled(settings).Select(problem => section + ": " + problem));
            }

            problems.Distinct().ShouldBeEmpty();
        }
        finally
        {
            SettingsWindow.Close(_app.Session, settings);
        }
    }

    [Fact]
    public void The_cheat_sheet_lists_the_global_shortcuts_and_closes_again()
    {
        Wait.ForDescendant(Main, "Toolbar.Help").Patterns.Invoke.Pattern.Invoke();
        var close = Wait.ForDescendant(Main, "CheatSheet.Close");

        foreach (var keys in new[] { "F9", "F10", "Ctrl+Shift+X", "Ctrl+Shift+A", "Esc", "Ctrl+Shift+C", "F1" })
        {
            Main.FindFirstDescendant(c => c.ByControlType(ControlType.Text).And(c.ByName(keys))).ShouldNotBeNull(keys);
        }

        Unlabelled(Main).ShouldBeEmpty();

        close.Patterns.Invoke.Pattern.Invoke();
        Wait.Until(() => Main.FindFirstDescendant(c => c.ByAutomationId("CheatSheet.Close")) is null, Timeout, "the cheat sheet to close");
    }

    /// <summary>Each interactive control under <paramref name="root"/> that lacks an AutomationId or a name, described.</summary>
    internal static IEnumerable<string> Unlabelled(AutomationElement root) =>
        root.FindAllDescendants()
            .Where(element => Interactive.Contains(element.Properties.ControlType.ValueOrDefault) && !IsWindowPart(element))
            .Select(element => (Element: element, Id: element.Properties.AutomationId.ValueOrDefault, Name: element.Properties.Name.ValueOrDefault))
            .Where(control => string.IsNullOrEmpty(control.Id) || string.IsNullOrEmpty(control.Name))
            .Select(control => $"{control.Element.Properties.ControlType.ValueOrDefault} with AutomationId '{control.Id}' and name '{control.Name}'"
                + $" ({control.Element.Properties.ClassName.ValueOrDefault}) in {control.Element.Parent?.Properties.ControlType.ValueOrDefault}"
                + $" '{control.Element.Parent?.Properties.AutomationId.ValueOrDefault}' ({control.Element.Parent?.Properties.ClassName.ValueOrDefault})"
                + $" in '{control.Element.Parent?.Parent?.Properties.AutomationId.ValueOrDefault}'");

    // Scroll bar arrows and thumbs, and the title bar's buttons, come from WPF and Windows, not from the recorder.
    private static bool IsWindowPart(AutomationElement element) =>
        element.Properties.ControlType.ValueOrDefault is ControlType.Button or ControlType.Thumb
        && element.Parent?.Properties.ControlType.ValueOrDefault is ControlType.ScrollBar or ControlType.TitleBar;
}

/// <summary>Opens and closes the recorder's Settings window (spec 12) through UI Automation.</summary>
internal static class SettingsWindow
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static AutomationElement Open(AppSession session)
    {
        var settingsButton = Wait.ForDescendant(session.MainWindow, "Toolbar.Settings");
        Wait.Until(() => settingsButton.Properties.IsEnabled.ValueOrDefault, Timeout, "Settings to be available");
        settingsButton.Patterns.Invoke.Pattern.Invoke();
        return WaitUntilOpen(session);
    }

    /// <summary>Waits for the Settings window, however it was opened.</summary>
    public static AutomationElement WaitUntilOpen(AppSession session) => Wait.For(() => FindOrNull(session), Timeout, "the Settings window");

    public static void Close(AppSession session, AutomationElement settings)
    {
        Wait.ForDescendant(settings, "Settings.Close").Patterns.Invoke.Pattern.Invoke();
        Wait.Until(() => IsClosed(session), Timeout, "the Settings window to close");
    }

    /// <summary>Selects a radio button in Settings and waits until it is selected.</summary>
    public static void Choose(AutomationElement settings, string automationId)
    {
        var option = Wait.ForDescendant(settings, automationId);
        option.Patterns.SelectionItem.Pattern.Select();
        Wait.Until(() => option.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, Timeout, $"'{automationId}' to be selected");
    }

    // An owned window is the owner's child in UI Automation; the desktop is searched too in case it is not.
    private static AutomationElement? Find(AppSession session) =>
        session.MainWindow.FindFirstChild(c => c.ByAutomationId("Settings"))
        ?? session.Automation.GetDesktop().FindFirstChild(c => c.ByAutomationId("Settings").And(c.ByProcessId(session.ProcessId)));

    // UI Automation can time out on a window that is opening or closing: then it is asked again.
    private static AutomationElement? FindOrNull(AppSession session)
    {
        try
        {
            return Find(session);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static bool IsClosed(AppSession session)
    {
        try
        {
            return Find(session) is null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
