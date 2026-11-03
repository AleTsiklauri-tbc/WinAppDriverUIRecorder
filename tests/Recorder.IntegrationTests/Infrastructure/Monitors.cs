using System.Runtime.InteropServices;
using Recorder.Core.Model;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>A monitor as the tests number it: 1 is the main display, the others follow from left to right.</summary>
internal sealed record DisplayMonitor(int Number, PixelRect Bounds, PixelRect WorkArea, double Scale)
{
    public override string ToString() => $"monitor {Number} ({Bounds.Width}×{Bounds.Height} at {Scale * 100:0} %)";
}

/// <summary>
/// The monitors, and top-level windows moved between them. Setting WINFORMSRECORDER_TEST_MONITOR (or
/// <c>./build.ps1 test -Monitor 2</c>) runs the tests with the SampleApp on that monitor: spec 14's acceptance on two monitors
/// at 100 % and 150 % is one run per monitor.
/// </summary>
internal static class Monitors
{
    public const string Variable = "WINFORMSRECORDER_TEST_MONITOR";

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<DisplayMonitor> All()
    {
        var found = new List<(PixelRect Bounds, PixelRect Work, double Scale, bool Primary)>();
        EnumDisplayMonitors(
            0,
            0,
            (monitor, _, _, _) =>
            {
                found.Add(Read(monitor));
                return true;
            },
            0);
        return [.. found.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
            .Select((m, index) => new DisplayMonitor(index + 1, m.Bounds, m.Work, m.Scale))];
    }

    /// <summary>The monitor showing most of a rectangle, or the nearest one.</summary>
    public static DisplayMonitor Of(PixelRect rect)
    {
        var native = new NativeRect { Left = rect.X, Top = rect.Y, Right = rect.Right, Bottom = rect.Bottom };
        var bounds = Read(MonitorFromRect(ref native, MonitorDefaultToNearest)).Bounds;
        return All().Single(monitor => monitor.Bounds == bounds);
    }

    public static DisplayMonitor At(PixelPoint point) => Of(new PixelRect(point.X, point.Y, 1, 1));

    /// <summary>The monitor chosen for the SampleApp, or null to leave it where Windows opens it.</summary>
    public static DisplayMonitor? Chosen()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var all = All();
        return int.TryParse(value, out var number) && number >= 1 && number <= all.Count
            ? all[number - 1]
            : throw new InvalidOperationException($"{Variable} is '{value}', but the monitors are numbered 1 to {all.Count}.");
    }

    public static PixelRect BoundsOf(nint window)
    {
        GetWindowRect(window, out var rect);
        return PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static bool IsMaximized(nint window) => IsZoomed(window);

    /// <summary>
    /// Centers a top-level window on a monitor's work area. It moves there first, and is centered once Windows (or the app,
    /// if it is per-monitor DPI aware) has rescaled it for that monitor.
    /// </summary>
    public static void MoveOnto(nint window, DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var work = monitor.WorkArea;
        SetWindowPos(window, 0, work.X + 8, work.Y + 8, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        var size = Settled(window, monitor);
        SetWindowPos(window, 0, work.X + Math.Max(0, (work.Width - size.Width) / 2), work.Y + Math.Max(0, (work.Height - size.Height) / 2), 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        Settled(window, monitor);
    }

    /// <summary>Gives a window a new size in physical pixels; its top left corner stays.</summary>
    public static void Resize(nint window, int width, int height)
    {
        SetWindowPos(window, 0, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate);
        Settled(window, Of(BoundsOf(window)));
    }

    // Waits until the window is on the monitor and has kept its bounds for a moment.
    private static PixelRect Settled(nint window, DisplayMonitor monitor)
    {
        var last = BoundsOf(window);
        Wait.Until(
            () =>
            {
                Thread.Sleep(300);
                var now = BoundsOf(window);
                var stable = now == last;
                last = now;
                return stable && Of(now) == monitor;
            },
            TimeSpan.FromSeconds(5),
            () => $"the window to settle on {monitor}; it is at {last}");
        return last;
    }

    private static (PixelRect Bounds, PixelRect Work, double Scale, bool Primary) Read(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        return (ToPixels(info.Monitor), ToPixels(info.Work), scale, (info.Flags & MonitorInfoPrimary) != 0);
    }

    private static PixelRect ToPixels(NativeRect rect) => PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
