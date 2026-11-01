using System.Runtime.InteropServices;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>
/// The clipboard's text, for testing the recorder's copy commands. The clipboard is the user's, so a test reads
/// what was there first and puts that text back afterwards (other formats such as images are not kept).
/// </summary>
internal static class ClipboardText
{
    private const uint UnicodeText = 13;
    private const uint Moveable = 0x0002;

    public static string? Read()
    {
        Open();
        try
        {
            var data = GetClipboardData(UnicodeText);
            if (data == 0)
            {
                return null;
            }

            var text = GlobalLock(data);
            try
            {
                return Marshal.PtrToStringUni(text);
            }
            finally
            {
                GlobalUnlock(data);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Open();
        try
        {
            EmptyClipboard();
            var memory = GlobalAlloc(Moveable, (nuint)((text.Length + 1) * sizeof(char)));
            var target = GlobalLock(memory);
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            GlobalUnlock(memory);
            if (SetClipboardData(UnicodeText, memory) == 0)
            {
                GlobalFree(memory); // on success the clipboard owns it
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Another program may hold the clipboard for a moment.
    private static void Open()
    {
        for (var attempt = 1; !OpenClipboard(0); attempt++)
        {
            if (attempt == 40)
            {
                throw new InvalidOperationException("The clipboard stayed locked by another program.");
            }

            Thread.Sleep(50);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);
}
