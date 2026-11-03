using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Recorder.Core.Model;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Services;

public enum HotkeyId
{
    Capture = 0xB001,

    /// <summary>Esc while picking or in Assert mode. Registered only while that mode is active.</summary>
    CancelMode = 0xB002,

    /// <summary>F9: start or stop recording. Registered while the recorder runs.</summary>
    Record = 0xB003,

    /// <summary>F10: pause or resume. Registered only while recording (D-48).</summary>
    Pause = 0xB004,

    /// <summary>Ctrl+Shift+A: Assert mode. Registered only while recording (D-48).</summary>
    Assert = 0xB005,
}

/// <summary>System-wide shortcuts through RegisterHotKey on the main window. UI thread only.</summary>
public sealed partial class HotkeyService : IDisposable
{
    // Not a HotkeyId: WM_HOTKEY for it is never handled, and it is held only for the length of IsFree.
    private const int ProbeId = 0xB0FF;

    private readonly ILogger<HotkeyService> _logger;
    private readonly HashSet<HotkeyId> _registered = [];
    private HwndSource? _source;

    public HotkeyService(ILogger<HotkeyService> logger) => _logger = logger;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler<HotkeyId>? Pressed;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        _source.AddHook(WndProc);
    }

    /// <summary>Returns false when another app already owns the shortcut.</summary>
    public bool Register(HotkeyId id, HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (_source is null)
        {
            throw new InvalidOperationException("Attach the hotkey service to a window first.");
        }

        Unregister(id);
        if (!RegisterHotKey(_source.Handle, (int)id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            LogRegisterFailed(_logger, gesture.ToString(), Marshal.GetLastPInvokeError());
            return false;
        }

        _registered.Add(id);
        return true;
    }

    /// <summary>
    /// Whether no other app holds the shortcut: registers it under a spare id and releases it at once. A shortcut the recorder
    /// itself holds is not free.
    /// </summary>
    public bool IsFree(HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (_source is null)
        {
            return true;
        }

        if (!RegisterHotKey(_source.Handle, ProbeId, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            return false;
        }

        UnregisterHotKey(_source.Handle, ProbeId);
        return true;
    }

    public void Unregister(HotkeyId id)
    {
        if (_source is not null && _registered.Remove(id))
        {
            UnregisterHotKey(_source.Handle, (int)id);
        }
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList())
        {
            Unregister(id);
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WM_HOTKEY && Enum.IsDefined((HotkeyId)(int)wParam))
        {
            handled = true;
            Pressed?.Invoke(this, (HotkeyId)(int)wParam);
        }

        return 0;
    }

    [LoggerMessage(EventId = 600, Level = LogLevel.Warning, Message = "Could not register the shortcut {Gesture} (error {Error}); another app probably owns it")]
    private static partial void LogRegisterFailed(ILogger logger, string gesture, int error);
}
