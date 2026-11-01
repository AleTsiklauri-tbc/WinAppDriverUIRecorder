using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace Recorder.IntegrationTests.Infrastructure;

internal static class Click
{
    private const uint BmClick = 0x00F5;
    private const uint WmClose = 0x0010;
    private const uint CbShowDropDown = 0x014F;

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

    private static void Send(AutomationElement element, uint message, nint wParam = 0)
    {
        var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Element has no window handle: " + element.Properties.Name.ValueOrDefault);
        }

        if (!NativeMethods.PostMessage(handle, message, wParam, IntPtr.Zero))
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
