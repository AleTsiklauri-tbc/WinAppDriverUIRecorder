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
}
