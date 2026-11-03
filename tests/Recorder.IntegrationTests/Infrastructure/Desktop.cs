using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using Recorder.Core.Model;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>A top-level window as Win32 sees it.</summary>
internal sealed record DesktopWindow(nint Handle, string ClassName, bool IsVisible, PixelRect Bounds);

/// <summary>An on-screen colour.</summary>
internal readonly record struct ScreenColor(byte R, byte G, byte B)
{
    /// <summary>Equal within a small tolerance, for colours read back from the screen.</summary>
    public bool IsNear(ScreenColor other) => Math.Abs(R - other.R) <= 12 && Math.Abs(G - other.G) <= 12 && Math.Abs(B - other.B) <= 12;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Real input and window arrangement for end-to-end tests. These move the user's cursor and send keys, so every
/// test that uses them restores the cursor and is tagged <c>Category=Input</c> (filter it out while working).
/// </summary>
internal static class Desktop
{
    public const ushort VkReturn = 0x0D;
    public const ushort VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const ushort VkEscape = 0x1B;
    public const ushort VkC = 0x43;
    public const ushort VkX = 0x58;
    public const ushort VkF2 = 0x71;
    public const ushort VkF9 = 0x78;
    public const ushort VkS = 0x53;
    private const uint KeyUnicode = 0x0004;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint KeyUp = 0x0002;
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;
    private static readonly nint HwndTopmost = -1;
    private static readonly nint HwndNoTopmost = -2;

    public static PixelRect BoundsOf(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var rect = element.BoundingRectangle;
        return new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static PixelPoint CenterOf(AutomationElement element) => BoundsOf(element).Center;

    /// <summary>Puts a window above all others without activating it. Works across processes.</summary>
    public static void Raise(nint window)
    {
        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        SetWindowPos(window, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Remembers where the cursor is; disposing puts it back.</summary>
    public static IDisposable KeepCursor()
    {
        GetCursorPos(out var saved);
        return new CursorRestorer(saved);
    }

    public static void MoveCursor(PixelPoint point) => SetCursorPos(point.X, point.Y);

    public static PixelPoint CursorPosition()
    {
        GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }

    /// <summary>The DPI scale of the monitor showing a point (1.0 = 100 %).</summary>
    public static double ScaleAt(PixelPoint point)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }

    /// <summary>The highlight outline's width in physical pixels: 2 DIPs on the monitor showing the point.</summary>
    public static int HighlightThicknessAt(PixelPoint point) => Math.Max(1, (int)Math.Round(2 * ScaleAt(point)));

    /// <summary>The colour the user sees at a point, overlays included.</summary>
    public static ScreenColor PixelAt(PixelPoint point)
    {
        var screen = GetDC(0);
        try
        {
            var bgr = GetPixel(screen, point.X, point.Y);
            return new ScreenColor((byte)bgr, (byte)(bgr >> 8), (byte)(bgr >> 16));
        }
        finally
        {
            _ = ReleaseDC(0, screen);
        }
    }

    /// <summary>A left click where the cursor is.</summary>
    public static void LeftClick() => Send(
    [
        new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = MouseLeftDown } } },
        new Input { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = MouseLeftUp } } },
    ]);

    /// <summary>Presses the keys in order and releases them in reverse order, like a person pressing a shortcut.</summary>
    public static void PressChord(params ushort[] virtualKeys)
    {
        ArgumentNullException.ThrowIfNull(virtualKeys);
        var inputs = virtualKeys.Select(key => Key(key, up: false)).Concat(virtualKeys.Reverse().Select(key => Key(key, up: true)));
        Send([.. inputs]);
    }

    /// <summary>
    /// Types text into the focused window as Unicode characters (VK_PACKET), whatever the keyboard layout. The recorder's
    /// keyboard hook sees these as typing, like keys pressed on a keyboard.
    /// </summary>
    public static void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var character in text)
        {
            Send(
            [
                new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = character, Flags = KeyUnicode } } },
                new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = character, Flags = KeyUnicode | KeyUp } } },
            ]);
            Thread.Sleep(15);
        }
    }

    /// <summary>The process owning the window the user sees at a point (what a click there would reach).</summary>
    public static int ProcessAtPoint(PixelPoint point)
    {
        var window = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    /// <summary>The top-level windows of one process with the given class name.</summary>
    public static IReadOnlyList<DesktopWindow> WindowsOf(int processId, string className)
    {
        var found = new List<DesktopWindow>();
        EnumWindows(
            (handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var owner);
                if (owner == processId)
                {
                    var name = new StringBuilder(256);
                    _ = GetClassName(handle, name, name.Capacity);
                    if (name.ToString() == className)
                    {
                        GetWindowRect(handle, out var rect);
                        found.Add(new DesktopWindow(handle, className, IsWindowVisible(handle), PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom)));
                    }
                }

                return true;
            },
            0);
        return found;
    }

    private static Input Key(ushort virtualKey, bool up) =>
        new() { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = up ? KeyUp : 0 } } };

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("SendInput was blocked (error " + Marshal.GetLastPInvokeError() + "). Is the session locked?");
        }
    }

    private sealed class CursorRestorer(NativePoint saved) : IDisposable
    {
        public void Dispose() => SetCursorPos(saved.X, saved.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint dc, int x, int y);
}
