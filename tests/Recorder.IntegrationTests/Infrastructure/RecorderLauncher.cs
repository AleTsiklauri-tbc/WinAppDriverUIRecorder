using System.Diagnostics;
using Recorder.Core.Persistence;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>Starts the recorder with its data redirected to a throwaway folder.</summary>
internal static class RecorderLauncher
{
    public static AppSession Launch(string dataHome)
    {
        var startInfo = new ProcessStartInfo(TestPaths.RecorderExe);
        startInfo.Environment[AppPaths.HomeOverrideVariable] = dataHome;
        startInfo.Environment["DOTNET_ROOT"] = TestPaths.DotnetRoot;
        return AppSession.Launch(startInfo);
    }

    public static string NewDataHome()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a data folder, first copying the recorder's log to <c>TestResults/recorder-logs</c> next to the test assembly,
    /// so a failed end-to-end run can be diagnosed afterwards. The log holds timings and statuses only, no UI text.
    /// </summary>
    public static void DeleteDataHome(string dataHome, string logName)
    {
        try
        {
            var logs = Path.Combine(dataHome, "local", "logs");
            if (Directory.Exists(logs))
            {
                var target = Path.Combine(AppContext.BaseDirectory, "TestResults", "recorder-logs");
                Directory.CreateDirectory(target);
                foreach (var log in Directory.GetFiles(logs, "*.log"))
                {
                    File.Copy(log, Path.Combine(target, $"{DateTime.Now:yyyyMMdd-HHmmss}-{logName}.log"), overwrite: true);
                }
            }

            Directory.Delete(dataHome, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp folder is cleaned up by the OS eventually.
        }
    }
}
