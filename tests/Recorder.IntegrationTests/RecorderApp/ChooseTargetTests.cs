using FlaUI.Core.AutomationElements;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>Choosing the app to automate, starting from a recorder that has never been used (spec 7.7, 11.3).</summary>
public sealed class ChooseTargetTests : IClassFixture<SampleAppFixture>, IDisposable
{
    private readonly SampleAppFixture _sample;
    private readonly string _dataHome = RecorderLauncher.NewDataHome();
    private readonly RecorderWindow _recorder;

    public ChooseTargetTests(SampleAppFixture sample)
    {
        _sample = sample;
        _recorder = new RecorderWindow(RecorderLauncher.Launch(_dataHome));
    }

    public void Dispose()
    {
        _recorder.Dispose();
        RecorderLauncher.DeleteDataHome(_dataHome, nameof(ChooseTargetTests));
    }

    [Fact]
    public void The_target_selector_lists_app_windows_and_choosing_one_attaches_and_remembers_it()
    {
        _recorder.NameOf("Status.Target").ShouldBe("No app selected");

        Wait.ForDescendant(_recorder.Main, "Toolbar.TargetSelector").Patterns.Toggle.Pattern.Toggle();
        var desktop = _recorder.Session.Automation.GetDesktop();
        var item = Wait.For(
            () => FindSelectorWindow(desktop, "Recorder Sample - [Loan]"),
            TimeSpan.FromSeconds(10),
            "the SampleApp window in the target selector");
        item.Properties.HelpText.Value.ShouldBe("Recorder.SampleApp.exe");

        item.Patterns.Invoke.Pattern.Invoke();

        _recorder.WaitForName("Status.Target", "Recorder.SampleApp.exe");
        _recorder.NameOf("Toolbar.TargetSelector").ShouldBe("Recorder.SampleApp.exe");
        File.ReadAllText(Path.Combine(_dataHome, "roaming", "settings.json")).ShouldContain("Recorder.SampleApp.exe");
    }

    [Fact]
    [Trait("Category", "Input")]
    public void With_no_app_chosen_the_capture_shortcut_targets_the_app_under_the_cursor()
    {
        _recorder.NameOf("Status.Target").ShouldBe("No app selected");
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(_sample.MainWindowHandle);
            Desktop.MoveCursor(Desktop.CenterOf(_sample.ById("chkInsured")));

            Desktop.PressChord(Desktop.VkControl, Desktop.VkShift, Desktop.VkX);

            _recorder.WaitForCapturedElement("insuredCheckbox");
        }

        _recorder.NameOf("Status.Target").ShouldBe("Recorder.SampleApp.exe");
        File.ReadAllText(Path.Combine(_dataHome, "roaming", "settings.json")).ShouldContain("Recorder.SampleApp.exe");
    }

    // The selector is a WPF popup: its own top-level window, owned by the recorder.
    private AutomationElement? FindSelectorWindow(AutomationElement desktop, string title) =>
        desktop.FindAllChildren(c => c.ByProcessId(_recorder.Session.ProcessId))
            .Select(window => window.FindFirstDescendant(c => c.ByAutomationId("TargetSelector.Window").And(c.ByName(title))))
            .FirstOrDefault(found => found is not null);
}
