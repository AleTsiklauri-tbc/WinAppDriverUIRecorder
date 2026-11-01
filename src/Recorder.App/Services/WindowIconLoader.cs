using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Services;

/// <summary>Small window icons for the target selector. Safe on any thread; a hung window costs at most 50 ms.</summary>
public static class WindowIconLoader
{
    private const nint IconSmall = 0;
    private const nint IconSmall2 = 2;
    private const uint TimeoutMilliseconds = 50;

    public static ImageSource? Load(nint windowHandle)
    {
        var icon = Query(windowHandle, IconSmall2);
        if (icon == 0)
        {
            icon = Query(windowHandle, IconSmall);
        }

        if (icon == 0)
        {
            icon = GetClassLongPtr(windowHandle, GCLP_HICONSM);
        }

        if (icon == 0)
        {
            icon = GetClassLongPtr(windowHandle, GCLP_HICON);
        }

        if (icon == 0)
        {
            return null;
        }

        try
        {
            // The icon belongs to the window; it is copied, never destroyed here.
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static nint Query(nint windowHandle, nint which) =>
        SendMessageTimeout(windowHandle, WM_GETICON, which, 0, SMTO_ABORTIFHUNG, TimeoutMilliseconds, out var icon) != 0 ? icon : 0;
}
