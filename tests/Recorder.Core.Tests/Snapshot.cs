using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Recorder.Core.Tests;

/// <summary>
/// Snapshot testing (spec 13.2) in the way of Verify, without its maintenance-fee binaries (D-62). Text is compared with
/// Snapshots/&lt;name&gt;.verified.&lt;ext&gt; next to the test file. On a difference, or when there is no snapshot yet, the test fails
/// and writes &lt;name&gt;.received.&lt;ext&gt; beside it: review it and rename it to .verified. to accept it.
/// </summary>
internal static class Snapshot
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Match(string text, string name, string extension = "txt", [CallerFilePath] string testFile = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        var folder = Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots");
        var verified = Path.Combine(folder, $"{name}.verified.{extension}");
        var received = Path.Combine(folder, $"{name}.received.{extension}");
        var actual = text.ReplaceLineEndings("\n");
        if (File.Exists(verified) && File.ReadAllText(verified, Utf8).ReplaceLineEndings("\n") == actual)
        {
            File.Delete(received);
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(received, actual, Utf8);
        Assert.Fail(File.Exists(verified)
            ? $"The snapshot changed: compare {Path.GetFileName(received)} with {Path.GetFileName(verified)} in {folder}, and rename it to .verified. if the change is right."
            : $"No snapshot yet: review {received} and rename it to .verified. to accept it.");
    }
}
