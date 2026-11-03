using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace Recorder.IntegrationTests.Infrastructure;

internal static class Click
{
    private const uint BmClick = 0x00F5;
    private const uint WmClose = 0x0010;
    private const uint CbShowDropDown = 0x014F;
    private const uint WmContextMenu = 0x007B;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;

    /// <summary>
    /// Clicks a Win32/WinForms button asynchronously, without moving the user's mouse.
    /// Finding: InvokePattern.Invoke blocks while the click opens a modal window, and every
    /// further UIA call to that process then times out (0x80131505). PostMessage returns at once.
    /// </summary>
    public static void Post(AutomationElement button) => Send(button, BmClick);

    /// <summary>Asks a window to close, without blocking on it.</summary>
    public static void PostClose(AutomationElement window) => Send(window, WmClose);

    /// <summary>Opens or closes a ComboBox's drop-down list (CB_SHOWDROPDOWN), without clicking.</summary>
    public static void ShowDropDown(AutomationElement comboBox, bool show) => Send(comboBox, CbShowDropDown, show ? 1 : 0);

    /// <summary>Opens a control's context menu at a screen point (WM_CONTEXTMENU), without a right-click.</summary>
    public static void PostContextMenu(AutomationElement control, Recorder.Core.Model.PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(control);
        var handle = control.Properties.NativeWindowHandle.ValueOrDefault;
        Post(handle, WmContextMenu, handle, (point.Y << 16) | (point.X & 0xFFFF));
    }

    /// <summary>Sends a key press to a window (for example Esc to close a menu), without the keyboard.</summary>
    public static void PostKey(nint window, ushort virtualKey)
    {
        Post(window, WmKeyDown, virtualKey, 0);
        Post(window, WmKeyUp, virtualKey, 0);
    }

    private static void Send(AutomationElement element, uint message, nint wParam = 0)
    {
        var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Element has no window handle: " + element.Properties.Name.ValueOrDefault);
        }

        Post(handle, message, wParam, IntPtr.Zero);
    }

    private static void Post(nint handle, uint message, nint wParam, nint lParam)
    {
        if (!NativeMethods.PostMessage(handle, message, wParam, lParam))
        {
            throw new InvalidOperationException("PostMessage failed with error " + Marshal.GetLastPInvokeError());
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
