using FlaUI.Core.AutomationElements;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>Spec 12: a setting applies and is saved as soon as it changes. Spec 11.2: the code pane sits right of the Inspector or below it.</summary>
public class SettingsTests : IClassFixture<RecorderAppFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RecorderAppFixture _app;

    public SettingsTests(RecorderAppFixture app) => _app = app;

    private Window Main => _app.Session.MainWindow;

    private string SettingsJson => File.ReadAllText(Path.Combine(_app.DataHome, "roaming", "settings.json"));

    [Fact]
    public void The_code_pane_moves_below_the_Inspector_and_back()
    {
        var code = Wait.ForDescendant(Main, "Code.Empty");
        var center = Wait.ForDescendant(Main, "FirstRun.Step3");
        code.BoundingRectangle.Left.ShouldBeGreaterThan(center.BoundingRectangle.Right);

        Change("Settings.CodePane.Bottom");
        Wait.Until(() => code.BoundingRectangle.Top > center.BoundingRectangle.Bottom, Timeout, "the code pane to move below the Inspector");
        SettingsJson.ShouldContain("\"codePanePosition\": \"Bottom\"");

        Change("Settings.CodePane.Right");
        Wait.Until(() => code.BoundingRectangle.Left > center.BoundingRectangle.Right, Timeout, "the code pane to move back to the right");
        SettingsJson.ShouldContain("\"codePanePosition\": \"Right\"");
    }

    [Fact]
    public void The_theme_choice_is_saved_at_once()
    {
        Change("Settings.Theme.Dark");
        SettingsJson.ShouldContain("\"theme\": \"Dark\"");

        Change("Settings.Theme.System");
        SettingsJson.ShouldContain("\"theme\": \"System\"");
    }

    [Fact]
    public void The_cheat_sheet_opens_the_Shortcuts_section_of_Settings()
    {
        Wait.ForDescendant(Main, "Toolbar.Help").Patterns.Invoke.Pattern.Invoke();
        Wait.ForDescendant(Main, "CheatSheet.Change").Patterns.Invoke.Pattern.Invoke();

        var settings = SettingsWindow.WaitUntilOpen(_app.Session);
        try
        {
            Wait.ForDescendant(settings, "Settings.Section.Shortcuts").Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeTrue();
            Wait.ForDescendant(settings, "Settings.Shortcut.Record").Patterns.Value.Pattern.Value.Value.ShouldBe("F9");
            Main.FindFirstDescendant(c => c.ByAutomationId("CheatSheet.Close")).ShouldBeNull();
        }
        finally
        {
            SettingsWindow.Close(_app.Session, settings);
        }
    }

    private void Change(string option)
    {
        var settings = SettingsWindow.Open(_app.Session);
        try
        {
            SettingsWindow.Choose(settings, option);
        }
        finally
        {
            SettingsWindow.Close(_app.Session, settings);
        }
    }
}
