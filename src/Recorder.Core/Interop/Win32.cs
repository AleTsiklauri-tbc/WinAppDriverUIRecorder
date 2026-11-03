using System.Runtime.InteropServices;
using Recorder.Core.Model;

namespace Recorder.Core.Interop;

/// <summary>Small, fast Win32 queries about windows and processes. Safe to call from any thread.</summary>
public static unsafe class Win32
{
    public static PixelPoint CursorPosition() =>
        NativeMethods.GetPhysicalCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;

    /// <summary>
    /// Process owning the top-level window under a screen point, or 0. Click-through windows such as the
    /// highlight overlay are skipped by Windows hit-testing.
    /// </summary>
    public static int ProcessAtPoint(PixelPoint point)
    {
        var hwnd = NativeMethods.WindowFromPhysicalPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });
        return hwnd == 0 ? 0 : ProcessOfWindow(NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT));
    }

    /// <summary>The process that owns the foreground window (the one receiving keyboard input), or 0.</summary>
    public static int ForegroundProcess() => ProcessOfWindow(NativeMethods.GetForegroundWindow());

    /// <summary>The user's double-click time (Control Panel › Mouse).</summary>
    public static TimeSpan DoubleClickTime() => TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());

    public static int ProcessOfWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return (int)processId;
    }

    /// <summary>The top-level window that contains <paramref name="hwnd"/> (itself when it is top-level), or 0.</summary>
    public static nint RootOf(nint hwnd) => hwnd == 0 ? 0 : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);

    public static bool IsTopLevel(nint hwnd) => hwnd != 0 && RootOf(hwnd) == hwnd;

    /// <summary>True when <paramref name="window"/> is <paramref name="owner"/> or is owned by it, directly or through other owned windows.</summary>
    public static bool IsOwnedBy(nint window, nint owner)
    {
        var current = window;
        for (var depth = 0; current != 0 && depth < 32; depth++)
        {
            if (current == owner)
            {
                return true;
            }

            current = NativeMethods.GetWindow(current, NativeMethods.GW_OWNER);
        }

        return false;
    }

    public static string ClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        var buffer = stackalloc char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>The display scale of the monitor showing most of a rectangle (1.0 = 100 %).</summary>
    public static double ScaleAt(PixelRect rect)
    {
        var native = new NativeMethods.RECT { Left = rect.X, Top = rect.Y, Right = rect.Right, Bottom = rect.Bottom };
        var monitor = NativeMethods.MonitorFromRect(&native, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return monitor != 0 && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : 1.0;
    }

    public static string WindowText(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* pointer = buffer)
        {
            var copied = NativeMethods.GetWindowText(hwnd, pointer, buffer.Length);
            return new string(buffer, 0, Math.Max(0, copied));
        }
    }

    public static int SessionOf(int processId) =>
        NativeMethods.ProcessIdToSessionId((uint)processId, out var session) ? (int)session : -1;

    public static string? ExecutablePath(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            fixed (char* pointer = buffer)
            {
                return NativeMethods.QueryFullProcessImageName(handle, 0, pointer, ref size) ? new string(buffer, 0, (int)size) : null;
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Whether a process runs elevated. A non-elevated caller cannot open an elevated process's token,
    /// so "access denied" counts as elevated.
    /// </summary>
    public static ElevationState ElevationOf(int processId)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process == 0)
        {
            return Marshal.GetLastPInvokeError() == NativeMethods.ERROR_ACCESS_DENIED ? ElevationState.Elevated : ElevationState.Unknown;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token))
            {
                return Marshal.GetLastPInvokeError() == NativeMethods.ERROR_ACCESS_DENIED ? ElevationState.Elevated : ElevationState.Unknown;
            }

            try
            {
                uint elevated = 0;
                return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, &elevated, sizeof(uint), out _)
                    ? (elevated != 0 ? ElevationState.Elevated : ElevationState.NotElevated)
                    : ElevationState.Unknown;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    internal static List<nint> TopLevelWindowHandles()
    {
        var handles = new List<nint>();
        var gcHandle = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumWindows(&CollectWindow, GCHandle.ToIntPtr(gcHandle));
        }
        finally
        {
            gcHandle.Free();
        }

        return handles;
    }

    internal static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint lParam)
    {
        ((List<nint>)GCHandle.FromIntPtr(lParam).Target!).Add(hwnd);
        return 1;
    }
}

public enum ElevationState
{
    Unknown,
    NotElevated,
    Elevated,
}
