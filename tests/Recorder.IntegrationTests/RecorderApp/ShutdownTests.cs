using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

public class ShutdownTests
{
    [Fact]
    public void Closing_the_main_window_stops_the_host_and_exits_with_code_zero()
    {
        var dataHome = RecorderLauncher.NewDataHome();
        using var session = RecorderLauncher.Launch(dataHome);

        var exitCode = session.CloseAndWaitForExit(TimeSpan.FromSeconds(15));

        exitCode.ShouldBe(0);
        var log = Directory.GetFiles(Path.Combine(dataHome, "local", "logs"), "recorder-*.log").Single();
        File.ReadAllText(log).ShouldContain("WinForms Recorder stopped");
        Directory.Delete(dataHome, recursive: true);
    }
}
