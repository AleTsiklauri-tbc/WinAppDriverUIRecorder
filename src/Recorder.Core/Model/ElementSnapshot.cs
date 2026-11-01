using Recorder.Core.Locators;

namespace Recorder.Core.Model;

/// <summary>The window just below the desktop that contains a captured element.</summary>
/// <remarks>Title can hold customer data (LMS puts the open client in it): display it, never log it or derive names from it.</remarks>
public sealed record WindowInfo(nint Handle, string? Title, string? AutomationId, string? ClassName);

/// <summary>One ancestor of a captured element, as seen through the raw view.</summary>
public sealed record AncestorInfo(
    string ControlType,
    string? AutomationId,
    string? Name,
    string? ClassName,
    string? FrameworkId,
    PixelRect Bounds,
    nint NativeWindowHandle,
    IReadOnlyList<int> RuntimeId);

/// <summary>
/// Everything known about one UI element at capture time, read in a few cross-process calls.
/// </summary>
/// <param name="ControlType">UIA ControlType name as WinAppDriver uses it in XPath: "Edit", "Button", "ComboBox"… ("Custom" when the element reports none, F-6).</param>
/// <param name="Ancestors">Nearest first, excluding the desktop.</param>
/// <param name="Value">ValuePattern value. Always null when <paramref name="IsPassword"/> is true: it is never read.</param>
/// <param name="RuntimeIdForSessionOnly">Re-finds the element during this session. Never emitted in code.</param>
/// <param name="HostWindow">
/// The Win32 top-level window that contains the element (GA_ROOT). An owned dialog, message box or menu has its own,
/// although UI Automation nests it under <paramref name="TopLevelWindow"/> (F-7). Null means the same window.
/// </param>
/// <param name="IsDetachedPopup">
/// The host window is neither the top-level window nor owned by it, like a ComboBox's open drop-down list. A WinAppDriver
/// session on the app's window cannot see such elements (F-23).
/// </param>
public sealed record ElementSnapshot(
    string ControlType,
    string? AutomationId,
    string? Name,
    string? ClassName,
    string? FrameworkId,
    bool IsPassword,
    bool IsEnabled,
    bool IsOffscreen,
    PixelRect Bounds,
    int ProcessId,
    nint NativeWindowHandle,
    WindowInfo TopLevelWindow,
    IReadOnlyList<AncestorInfo> Ancestors,
    IReadOnlyList<string> SupportedPatterns,
    string? Value,
    string? LabelText,
    IReadOnlyList<int> RuntimeIdForSessionOnly,
    DateTimeOffset CapturedAt,
    WindowInfo? HostWindow = null,
    bool IsDetachedPopup = false)
{
    /// <summary>Stable key for the element during this session (the runtime id as text).</summary>
    public string SessionKey => RuntimeKey.Of(RuntimeIdForSessionOnly);

    public WindowInfo Host => HostWindow ?? TopLevelWindow;

    /// <summary>Whether WinAppDriver finds the element from the app's window session or only from a desktop session.</summary>
    public LocatorScope Scope => IsDetachedPopup ? LocatorScope.Desktop : LocatorScope.Window;

    /// <summary>The window whose tree a locator is searched in: the app's window, or the pop-up itself.</summary>
    public nint SearchRoot => IsDetachedPopup ? Host.Handle : TopLevelWindow.Handle;
}

public static class RuntimeKey
{
    public static string Of(IReadOnlyList<int> runtimeId)
    {
        ArgumentNullException.ThrowIfNull(runtimeId);
        return string.Join('.', runtimeId);
    }
}
