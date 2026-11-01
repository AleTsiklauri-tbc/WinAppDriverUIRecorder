using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Persistence;

public class AppPathsTests
{
    private static string Folder(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.ApplicationData => @"C:\Users\qa\AppData\Roaming",
        Environment.SpecialFolder.LocalApplicationData => @"C:\Users\qa\AppData\Local",
        _ => throw new ArgumentOutOfRangeException(nameof(folder)),
    };

    [Fact]
    public void Settings_and_profiles_roam_while_logs_and_autosaves_stay_local()
    {
        var paths = AppPaths.Resolve(_ => null, Folder);

        paths.SettingsFile.ShouldBe(@"C:\Users\qa\AppData\Roaming\WinFormsRecorder\settings.json");
        paths.ProfilesDirectory.ShouldBe(@"C:\Users\qa\AppData\Roaming\WinFormsRecorder\profiles");
        paths.LogsDirectory.ShouldBe(@"C:\Users\qa\AppData\Local\WinFormsRecorder\logs");
        paths.AutosaveDirectory.ShouldBe(@"C:\Users\qa\AppData\Local\WinFormsRecorder\autosave");
    }

    [Fact]
    public void Home_override_puts_all_data_under_one_folder()
    {
        var paths = AppPaths.Resolve(
            name => name == AppPaths.HomeOverrideVariable ? @"D:\portable\recorder" : null,
            Folder);

        paths.SettingsFile.ShouldBe(@"D:\portable\recorder\roaming\settings.json");
        paths.LogsDirectory.ShouldBe(@"D:\portable\recorder\local\logs");
    }

    [Fact]
    public void Blank_home_override_is_ignored()
    {
        var paths = AppPaths.Resolve(_ => "   ", Folder);

        paths.RoamingRoot.ShouldBe(@"C:\Users\qa\AppData\Roaming\WinFormsRecorder");
    }

    [Theory]
    [InlineData("", @"C:\local")]
    [InlineData(@"C:\roaming", " ")]
    public void Roots_are_required(string roaming, string local) =>
        Should.Throw<ArgumentException>(() => new AppPaths(roaming, local));
}
