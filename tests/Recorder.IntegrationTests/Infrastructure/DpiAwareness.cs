using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>
/// The recorder works in physical pixels (per-monitor DPI aware V2). The tests compare its coordinates with what
/// UI Automation reports and move the real cursor, so this process must use physical pixels too.
/// </summary>
internal static class DpiAwareness
{
    private static readonly nint PerMonitorAwareV2 = -4;

#pragma warning disable CA2255 // The test assembly is an executable: the initializer runs before any window or UIA call.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize() => SetProcessDpiAwarenessContext(PerMonitorAwareV2);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
