using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Persistence;
using Recorder.IntegrationTests.Infrastructure;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>The SampleApp plus a recorder that remembers it as its target, so it attaches on start (spec 7.7).</summary>
public sealed class RecorderWithTargetFixture : IDisposable
{
    public const string TargetName = "Recorder.SampleApp.exe";

    public RecorderWithTargetFixture()
    {
        Sample = new SampleAppFixture();
        DataHome = RecorderLauncher.NewDataHome();
        var paths = new AppPaths(Path.Combine(DataHome, "roaming"), Path.Combine(DataHome, "local"));
        new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Update(settings => settings with { LastTargetProcessName = TargetName });
        Recorder = new RecorderWindow(RecorderLauncher.Launch(DataHome));
        Recorder.WaitForName("Status.Target", TargetName);
    }

    public SampleAppFixture Sample { get; }

    internal RecorderWindow Recorder { get; }

    internal string DataHome { get; }

    public void Dispose()
    {
        Recorder.Dispose();
        Sample.Dispose();
        RecorderLauncher.DeleteDataHome(DataHome, nameof(CaptureEndToEndTests));
    }
}
