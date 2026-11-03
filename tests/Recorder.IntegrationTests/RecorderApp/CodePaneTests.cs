using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.CodeGen;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>The recorder with a profile of the user's own in its profiles folder before it starts (spec 10.1).</summary>
public sealed class RecorderWithProfilesFixture : IDisposable
{
    public RecorderWithProfilesFixture()
    {
        DataHome = RecorderLauncher.NewDataHome();
        ProfilesFolder = Path.Combine(DataHome, "roaming", "profiles");
        var java = ProfileLoader.BuiltIn().Single(p => p.Id == CodeProfile.DefaultId);
        ProfileLoader.Export(java, Path.Combine(ProfilesFolder, "team-java"));
        var json = Path.Combine(ProfilesFolder, "team-java", "profile.json");
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"name\": \"Java · TestNG · Appium 7\"", "\"name\": \"Team Java\"", StringComparison.Ordinal));
        Session = RecorderLauncher.Launch(DataHome);
    }

    internal string DataHome { get; }

    internal string ProfilesFolder { get; }

    internal AppSession Session { get; }

    public void Dispose()
    {
        Session.Dispose();
        RecorderLauncher.DeleteDataHome(DataHome, "code-pane");
    }
}

/// <summary>
/// Phase 4, the code pane without captured controls: the profile picker, switching profiles, and profiles read again
/// when their files change (hot reload). Only UI Automation patterns of the recorder itself; no mouse or keyboard.
/// </summary>
public sealed class CodePaneTests : IClassFixture<RecorderWithProfilesFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly RecorderWithProfilesFixture _fixture;

    public CodePaneTests(RecorderWithProfilesFixture fixture) => _fixture = fixture;

    private Window Main => _fixture.Session.MainWindow;

    [Fact]
    public void The_profile_picker_offers_the_built_in_profiles_and_the_users_own_with_Java_first()
    {
        var picker = Wait.ForDescendant(Main, "Code.Profile").AsComboBox();

        picker.Expand();
        var names = picker.Items.Select(item => item.Text).ToList();
        picker.Collapse();

        names.ShouldBe(["Java · TestNG · Appium 7", "C# · MSTest · Appium 4", "Team Java"]);
        picker.SelectedItem.ShouldNotBeNull().Text.ShouldBe("Java · TestNG · Appium 7");
        Wait.ForDescendant(Main, "Code.Empty").Properties.Name.Value.ShouldBe("Capture controls (Ctrl+Shift+X) or record a flow (F9) to get page classes.");
    }

    [Fact]
    public void Choosing_the_csharp_profile_drops_the_Steps_tab_and_is_remembered()
    {
        var picker = Wait.ForDescendant(Main, "Code.Profile").AsComboBox();
        try
        {
            picker.Select("C# · MSTest · Appium 4");

            Wait.Until(
                () => Main.FindFirstDescendant(c => c.ByAutomationId("Code.Tab.Steps")) is not { } tab || tab.Properties.IsOffscreen.ValueOrDefault,
                Timeout,
                "the Steps tab to go");
            Wait.Until(() => ActiveProfileSetting() == "csharp-appium", Timeout, "settings.json to name the C# profile");
        }
        finally
        {
            picker.Select("Java · TestNG · Appium 7");
            Wait.Until(() => ActiveProfileSetting() == "java-appium-pom", Timeout, "settings.json to name the Java profile again");
        }
    }

    [Fact]
    public void A_profile_folder_that_breaks_while_the_recorder_runs_is_reported_and_the_report_goes_when_it_is_fixed()
    {
        var broken = Path.Combine(_fixture.ProfilesFolder, "broken");
        Directory.CreateDirectory(broken);
        try
        {
            File.WriteAllText(Path.Combine(broken, "profile.json"), "{ \"name\": ");

            var banner = Wait.For(() => BannerText(text => text.Contains("“broken”", StringComparison.Ordinal)), Timeout, "the profile problem banner");
            banner.ShouldStartWith("The code profile “broken” couldn't be read and isn't offered. profile.json:");
        }
        finally
        {
            Directory.Delete(broken, recursive: true);
        }

        Wait.Until(() => BannerText(text => text.Contains("“broken”", StringComparison.Ordinal)) is null, Timeout, "the banner to go");
    }

    private string? BannerText(Func<string, bool> match) =>
        Main.FindAllDescendants(c => c.ByAutomationId("Banner.Message"))
            .Select(message => message.Properties.Name.ValueOrDefault ?? string.Empty)
            .FirstOrDefault(match);

    private string? ActiveProfileSetting()
    {
        var path = Path.Combine(_fixture.DataHome, "roaming", "settings.json");
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(path));
            return settings.RootElement.TryGetProperty("activeProfile", out var value) ? value.GetString() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null; // being written
        }
    }
}
