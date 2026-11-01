using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Recorder.App.Services;

/// <summary>Restarts the recorder elevated, for targets that run as administrator.</summary>
public static class ElevationRestart
{
    private const int ErrorCancelled = 1223;

    public static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            // The user said no to the elevation prompt; keep running as is.
        }
    }
}
