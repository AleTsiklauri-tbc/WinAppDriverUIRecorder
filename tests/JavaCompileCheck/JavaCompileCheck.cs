using System.Diagnostics;
using System.Text;
using Recorder.Core.CodeGen;

namespace Recorder.Tests.Java;

/// <summary>Runs check-java.ps1 on generated code: javac against the stubs and the real java-client 7.6.0 jars (spec 14, Phase 4).</summary>
/// <remarks>Shared by Recorder.Core.Tests and Recorder.IntegrationTests (linked source).</remarks>
internal static class JavaCompileCheck
{
    public const int Compiled = 0;
    public const int NoJdk = 2;
    public const int NoJars = 3;

    /// <param name="repoRoot">The folder that holds WinFormsRecorder.sln.</param>
    public static (int ExitCode, string Output) Run(IEnumerable<GeneratedFile> files, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(files);
        var source = Path.Combine(Path.GetTempPath(), "generated-java-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            foreach (var file in files)
            {
                if (file.Error is not null)
                {
                    return (1, file.FileName + ": " + file.Error);
                }

                File.WriteAllText(Path.Combine(source, file.FileName), file.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            var script = Path.Combine(repoRoot, "tests", "JavaCompileCheck", "check-java.ps1");
            var start = new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", source },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
            {
                process.Kill(entireProcessTree: true);
                return (1, "check-java.ps1 did not finish within 3 minutes.\n" + output);
            }

            return (process.ExitCode, output + error.Result);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }
}
