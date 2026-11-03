using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Persistence;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public SettingsStoreTests() => _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SettingsStore NewStore() => new(_paths, NullLogger<SettingsStore>.Instance);

    [Fact]
    public void Defaults_are_used_when_no_file_exists()
    {
        var settings = NewStore().Current;

        settings.SchemaVersion.ShouldBe(AppSettings.CurrentSchemaVersion);
        settings.CaptureHotkey.ShouldBe("Ctrl+Shift+X");
        settings.LastTargetProcessName.ShouldBeNull();
    }

    [Fact]
    public void Changes_are_saved_and_read_back()
    {
        NewStore().Update(s => s with { LastTargetProcessName = "LMS.exe", CaptureHotkey = "Ctrl+Alt+K" });

        var reloaded = NewStore().Current;
        reloaded.LastTargetProcessName.ShouldBe("LMS.exe");
        reloaded.CaptureHotkey.ShouldBe("Ctrl+Alt+K");
        File.ReadAllText(_paths.SettingsFile).ShouldContain("\"schemaVersion\": 1");
        File.Exists(_paths.SettingsFile + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void A_damaged_file_is_set_aside_and_defaults_are_used()
    {
        Directory.CreateDirectory(_paths.RoamingRoot);
        File.WriteAllText(_paths.SettingsFile, "{ not json");

        var store = NewStore();

        store.Current.ShouldBe(new AppSettings());
        File.Exists(_paths.SettingsFile).ShouldBeFalse();
        Directory.GetFiles(_paths.RoamingRoot, "settings.json.damaged-*").Length.ShouldBe(1);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_never_overwritten()
    {
        Directory.CreateDirectory(_paths.RoamingRoot);
        const string newer = """{ "schemaVersion": 99, "lastTargetProcessName": "LMS.exe", "somethingNew": true }""";
        File.WriteAllText(_paths.SettingsFile, newer);

        var store = NewStore();
        store.Update(s => s with { LastTargetProcessName = "Other.exe" });

        store.Current.LastTargetProcessName.ShouldBe("Other.exe");
        File.ReadAllText(_paths.SettingsFile).ShouldBe(newer);
    }

    [Fact]
    public void Phase_5_settings_are_saved_with_enums_as_names()
    {
        var layout = new WindowLayout(10, 20, 1300, 800, Maximized: true, LeftPaneWidth: 320, CodePaneWidth: 420, CodePaneHeight: 300);
        NewStore().Update(s => s with
        {
            Theme = ThemeChoice.Dark,
            CodePanePosition = CodePanePosition.Bottom,
            DetailedLogs = true,
            Window = layout,
            RecentRecordings = [@"C:\tests\a.recording.json"],
        });

        var reloaded = NewStore().Current;
        reloaded.Theme.ShouldBe(ThemeChoice.Dark);
        reloaded.CodePanePosition.ShouldBe(CodePanePosition.Bottom);
        reloaded.DetailedLogs.ShouldBeTrue();
        reloaded.Window.ShouldBe(layout);
        reloaded.RecentRecordings.ShouldBe(new[] { @"C:\tests\a.recording.json" });
        var json = File.ReadAllText(_paths.SettingsFile);
        json.ShouldContain("\"theme\": \"Dark\"");
        json.ShouldContain("\"codePanePosition\": \"Bottom\"");
    }

    [Fact]
    public void A_value_from_a_newer_version_falls_back_to_the_default_and_keeps_the_rest()
    {
        Directory.CreateDirectory(_paths.RoamingRoot);
        File.WriteAllText(_paths.SettingsFile, """{ "schemaVersion": 1, "theme": "Sepia", "codePanePosition": 3, "lastTargetProcessName": "LMS.exe" }""");

        var settings = NewStore().Current;

        settings.Theme.ShouldBe(ThemeChoice.System);
        settings.CodePanePosition.ShouldBe(CodePanePosition.Right);
        settings.LastTargetProcessName.ShouldBe("LMS.exe");
        File.Exists(_paths.SettingsFile).ShouldBeTrue();
    }

    [Fact]
    public void A_phase_4_file_reads_with_the_new_defaults()
    {
        Directory.CreateDirectory(_paths.RoamingRoot);
        File.WriteAllText(_paths.SettingsFile, """{ "schemaVersion": 1, "recordHotkey": "F8", "minimizeOnRecord": false }""");

        var settings = NewStore().Current;

        settings.RecordHotkey.ShouldBe("F8");
        settings.MinimizeOnRecord.ShouldBeFalse();
        settings.Theme.ShouldBe(ThemeChoice.System);
        settings.CodePanePosition.ShouldBe(CodePanePosition.Right);
        settings.Window.ShouldBeNull();
        settings.RecentRecordings.ShouldBeEmpty();
    }
}
