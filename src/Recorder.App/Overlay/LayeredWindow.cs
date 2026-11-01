using System.Runtime.InteropServices;
using Recorder.Core.Model;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Overlay;

/// <summary>
/// A raw Win32 layered window that shows a premultiplied BGRA bitmap at an exact physical-pixel position.
/// Click-through, never activated, topmost, not in Alt+Tab. Used instead of a WPF Window so that WPF never
/// rescales or moves it when it crosses monitors with different DPI. UI thread only.
/// </summary>
internal sealed unsafe class LayeredWindow : IDisposable
{
    private const int ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
    private static ushort s_classAtom;

    public nint Handle { get; private set; }

    public bool IsVisible { get; private set; }

    public void Show(PixelRect bounds, ReadOnlySpan<byte> premultipliedBgra)
    {
        if (bounds.IsEmpty || premultipliedBgra.Length != bounds.Width * bounds.Height * 4)
        {
            Hide();
            return;
        }

        EnsureCreated();
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            Size = sizeof(BITMAPINFOHEADER),
            Width = bounds.Width,
            Height = -bounds.Height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var bitmap = CreateDIBSection(memory, &header, DIB_RGB_COLORS, out var bits, 0, 0);
        try
        {
            premultipliedBgra.CopyTo(new Span<byte>(bits, premultipliedBgra.Length));
            var previous = SelectObject(memory, bitmap);
            var destination = new POINT { X = bounds.X, Y = bounds.Y };
            var size = new SIZE { Width = bounds.Width, Height = bounds.Height };
            var source = default(POINT);
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screen, &destination, &size, memory, &source, 0, &blend, ULW_ALPHA);
            SelectObject(memory, previous);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        IsVisible = true;
    }

    public void Hide()
    {
        if (Handle != 0 && IsVisible)
        {
            ShowWindow(Handle, SW_HIDE);
        }

        IsVisible = false;
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }

        IsVisible = false;
    }

    private void EnsureCreated()
    {
        if (Handle != 0)
        {
            return;
        }

        var instance = GetModuleHandle(null);
        if (s_classAtom == 0)
        {
            s_classAtom = RegisterWindowClass(instance);
        }

        Handle = CreateWindowEx(ExStyle, s_classAtom, null, WS_POPUP, 0, 0, 1, 1, 0, 0, instance, 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException("Could not create the highlight window (error " + Marshal.GetLastPInvokeError() + ").");
        }
    }

    private static ushort RegisterWindowClass(nint instance)
    {
        // The windows never paint themselves (UpdateLayeredWindow supplies their pixels), so the default
        // window procedure is enough and no managed callback is needed.
        var user32 = NativeLibrary.Load("user32.dll");
        fixed (char* className = "WinFormsRecorder.Highlight")
        {
            var windowClass = new WNDCLASSEXW
            {
                Size = sizeof(WNDCLASSEXW),
                WndProc = NativeLibrary.GetExport(user32, "DefWindowProcW"),
                Instance = instance,
                ClassName = className,
            };
            var atom = RegisterClassEx(&windowClass);
            return atom != 0 ? atom : throw new InvalidOperationException("Could not register the highlight window class (error " + Marshal.GetLastPInvokeError() + ").");
        }
    }
}
