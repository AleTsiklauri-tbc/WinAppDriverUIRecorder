using System.Diagnostics;
using Recorder.Core.Interop;

namespace Recorder.Core.Automation;

/// <summary>A window shown in the target selector.</summary>
/// <param name="Title">Visible window title; may contain customer data, so it is displayed but never logged.</param>
public sealed record AppWindow(nint Handle, string Title, int ProcessId, string ProcessName, string? ExecutablePath);

/// <summary>Lists the app windows a user could choose as the target: visible, titled, in this Windows session, not ours.</summary>
public static class WindowEnumerator
{
    public static IReadOnlyList<AppWindow> GetAppWindows()
    {
        var ownProcess = Environment.ProcessId;
        var ownSession = Win32.SessionOf(ownProcess);
        var processNames = new Dictionary<int, string?>();
        var paths = new Dictionary<int, string?>();
        var windows = new List<AppWindow>();

        foreach (var hwnd in Win32.TopLevelWindowHandles())
        {
            if (!IsAppWindow(hwnd))
            {
                continue;
            }

            var processId = Win32.ProcessOfWindow(hwnd);
            if (processId == 0 || processId == ownProcess || Win32.SessionOf(processId) != ownSession)
            {
                continue;
            }

            if (!processNames.TryGetValue(processId, out var name))
            {
                name = ProcessName(processId);
                processNames[processId] = name;
                paths[processId] = Win32.ExecutablePath(processId);
            }

            var title = Win32.WindowText(hwnd);
            if (name is not null && title.Length > 0)
            {
                windows.Add(new AppWindow(hwnd, title, processId, name, paths[processId]));
            }
        }

        return windows;
    }

    /// <summary>"LMS.exe" for a running process id, or null if it has exited.</summary>
    public static string? ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // The same rules as the taskbar: visible, not cloaked, not a tool window, unowned unless it asks to be shown.
    private static bool IsAppWindow(nint hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd))
        {
            return false;
        }

        var exStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        return owner == 0 || (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
    }
}
