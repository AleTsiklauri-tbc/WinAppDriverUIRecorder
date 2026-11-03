using System.Diagnostics;
using System.IO;

namespace Recorder.App.Services;

internal static class Explorer
{
    /// <summary>Shows a folder in File Explorer, creating it first if it does not exist yet.</summary>
    public static void Open(string folder)
    {
        Directory.CreateDirectory(folder);
        using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
    }
}
