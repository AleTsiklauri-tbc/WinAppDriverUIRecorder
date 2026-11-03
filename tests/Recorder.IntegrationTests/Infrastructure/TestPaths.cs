namespace Recorder.IntegrationTests.Infrastructure;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Debug or Release, taken from this test assembly's own output folder.</summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;

    public static string SampleAppExe =>
        Existing(Path.Combine(RepoRoot, "tests", "Recorder.SampleApp", "bin", Configuration, "net48", "Recorder.SampleApp.exe"));

    /// <summary>
    /// The recorder under test: the build output, or the exe named by WINFORMSRECORDER_RECORDER_EXE, which
    /// <c>./build.ps1 test -Published</c> sets to the published single-file exe.
    /// </summary>
    public static string RecorderExe =>
        Existing(Environment.GetEnvironmentVariable("WINFORMSRECORDER_RECORDER_EXE") is { Length: > 0 } published
            ? Path.GetFullPath(published)
            : Path.Combine(RepoRoot, "src", "Recorder.App", "bin", Configuration, "net10.0-windows", "WinFormsRecorder.exe"));

    /// <summary>
    /// Root of the .NET installation running these tests. The recorder is framework-dependent in Debug,
    /// so it is started with DOTNET_ROOT pointing here (the SDK may be a per-user install).
    /// </summary>
    public static string DotnetRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));

    private static string Existing(string path) =>
        File.Exists(path) ? path : throw new FileNotFoundException("Build the solution first.", path);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WinFormsRecorder.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("WinFormsRecorder.sln not found.");
    }
}
