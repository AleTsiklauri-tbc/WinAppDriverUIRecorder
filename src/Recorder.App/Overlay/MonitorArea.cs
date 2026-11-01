using Recorder.Core.Model;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Overlay;

/// <summary>The monitor an element is on: bounds, work area (without the taskbar) and its DPI scale.</summary>
internal readonly record struct MonitorArea(PixelRect Bounds, PixelRect WorkArea, double Scale)
{
    public static unsafe MonitorArea For(PixelRect rect)
    {
        var native = new RECT { Left = rect.X, Top = rect.Y, Right = rect.Right, Bottom = rect.Bottom };
        var monitor = MonitorFromRect(&native, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = sizeof(MONITORINFO) };
        if (monitor == 0 || !GetMonitorInfo(monitor, &info))
        {
            return new MonitorArea(rect, rect, 1.0);
        }

        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        return new MonitorArea(ToPixels(info.Monitor), ToPixels(info.Work), scale);
    }

    private static PixelRect ToPixels(RECT rect) => PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
