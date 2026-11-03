using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Recorder.Core.Interop;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

public enum MouseAction
{
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    Wheel,
}

/// <param name="Time">The system's millisecond tick count for the event (for double-click timing).</param>
/// <param name="Swallowed">True when the event was kept from the target app (the Pick click).</param>
/// <param name="WheelDelta">For a wheel event: positive away from the user, 120 per notch.</param>
public readonly record struct MouseHookEvent(MouseAction Action, PixelPoint Point, uint Time, bool Injected, bool Swallowed, int WheelDelta = 0);

public interface IMouseHook : IDisposable
{
    /// <summary>Raised on a thread-pool thread for button and wheel events (moves are not queued).</summary>
    event EventHandler<MouseHookEvent>? MouseEvent;

    /// <summary>
    /// How long since the hook last saw any mouse input, moves included. Windows removes a low-level hook it finds too
    /// slow without telling anyone: input that moves the cursor while this keeps growing means the hook is gone.
    /// </summary>
    TimeSpan IdleFor { get; }

    /// <summary>
    /// Installs the hook. <paramref name="shouldSwallow"/> runs inside the hook callback for each button-down and
    /// must be fast; when it returns true that press and its matching release never reach the app.
    /// Returns false if Windows refused the hook (for example blocked by security software).
    /// </summary>
    bool Install(Func<MouseHookEvent, bool>? shouldSwallow);

    /// <summary>Installs the hook again with the same predicate, after Windows removed it.</summary>
    bool Reinstall();

    void Uninstall();
}

/// <summary>
/// WH_MOUSE_LL on a dedicated thread with its own message loop. The callback copies the event into a channel
/// and returns at once: Windows silently removes low-level hooks that take too long. Installed only while needed.
/// </summary>
public sealed class MouseHookService : IMouseHook
{
    // The hook procedure is static (it is called from native code); one hook per process is enough.
    private static MouseHookService? s_active;

    private readonly Channel<MouseHookEvent> _events = Channel.CreateUnbounded<MouseHookEvent>(new UnboundedChannelOptions { SingleReader = true });
    private Func<MouseHookEvent, bool>? _shouldSwallow;
    private MouseAction? _swallowNextRelease;
    private Thread? _thread;
    private uint _threadId;
    private Task? _dispatcher;
    private long _lastActivity = Stopwatch.GetTimestamp();

    public event EventHandler<MouseHookEvent>? MouseEvent;

    public bool IsInstalled => _thread is not null;

    public TimeSpan IdleFor => Stopwatch.GetElapsedTime(Volatile.Read(ref _lastActivity));

    public bool Reinstall()
    {
        var shouldSwallow = _shouldSwallow;
        Uninstall();
        return Install(shouldSwallow);
    }

    public bool Install(Func<MouseHookEvent, bool>? shouldSwallow)
    {
        if (_thread is not null)
        {
            _shouldSwallow = shouldSwallow;
            return true;
        }

        if (Interlocked.CompareExchange(ref s_active, this, null) is not null)
        {
            throw new InvalidOperationException("Another mouse hook is already installed in this process.");
        }

        _shouldSwallow = shouldSwallow;
        _swallowNextRelease = null;
        Volatile.Write(ref _lastActivity, Stopwatch.GetTimestamp());
        using var started = new ManualResetEventSlim();
        var installed = false;
        _thread = new Thread(() =>
        {
            NativeMethods.PeekMessage(out _, 0, 0, 0, NativeMethods.PM_NOREMOVE); // creates the message queue
            _threadId = NativeMethods.GetCurrentThreadId();
            var hook = InstallHook();
            installed = hook != 0;
            started.Set();
            if (!installed)
            {
                return;
            }

            while (NativeMethods.GetMessage(out _, 0, 0, 0) > 0)
            {
            }

            NativeMethods.UnhookWindowsHookEx(hook);
        })
        {
            IsBackground = true,
            Name = "MouseHook",
        };
        _thread.Start();
        started.Wait();

        if (!installed)
        {
            _thread.Join();
            _thread = null;
            Volatile.Write(ref s_active, null);
            return false;
        }

        _dispatcher ??= Task.Run(DispatchAsync);
        return true;
    }

    public void Uninstall()
    {
        if (_thread is null)
        {
            return;
        }

        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _shouldSwallow = null;
        Interlocked.CompareExchange(ref s_active, null, this);
    }

    public void Dispose()
    {
        Uninstall();
        _events.Writer.TryComplete();
    }

    private static unsafe nint InstallHook() =>
        NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, &HookProc, NativeMethods.GetModuleHandle(null), 0);

    [UnmanagedCallersOnly]
    private static unsafe nint HookProc(int code, nint wParam, nint lParam)
    {
        var active = Volatile.Read(ref s_active);
        if (code >= 0 && active is not null)
        {
            try
            {
                if (active.Handle((uint)wParam, *(NativeMethods.MSLLHOOKSTRUCT*)lParam))
                {
                    return 1;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Never let an exception cross into native code; the event simply passes through.
            }
        }

        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }

    // Returns true to swallow the event.
    private bool Handle(uint message, NativeMethods.MSLLHOOKSTRUCT data)
    {
        Volatile.Write(ref _lastActivity, Stopwatch.GetTimestamp());
        MouseAction? action = message switch
        {
            NativeMethods.WM_LBUTTONDOWN => MouseAction.LeftDown,
            NativeMethods.WM_LBUTTONUP => MouseAction.LeftUp,
            NativeMethods.WM_RBUTTONDOWN => MouseAction.RightDown,
            NativeMethods.WM_RBUTTONUP => MouseAction.RightUp,
            NativeMethods.WM_MOUSEWHEEL => MouseAction.Wheel,
            _ => null,
        };
        if (action is null)
        {
            return false;
        }

        var hookEvent = new MouseHookEvent(
            action.Value, new PixelPoint(data.Point.X, data.Point.Y), data.Time, (data.Flags & NativeMethods.LLMHF_INJECTED) != 0, Swallowed: false,
            action == MouseAction.Wheel ? (short)(data.MouseData >> 16) : 0);
        var swallow = false;
        if (_swallowNextRelease == action)
        {
            _swallowNextRelease = null;
            swallow = true;
        }
        else if (action is MouseAction.LeftDown or MouseAction.RightDown && _shouldSwallow?.Invoke(hookEvent) == true)
        {
            _swallowNextRelease = action == MouseAction.LeftDown ? MouseAction.LeftUp : MouseAction.RightUp;
            swallow = true;
        }

        _events.Writer.TryWrite(hookEvent with { Swallowed = swallow });
        return swallow;
    }

    private async Task DispatchAsync()
    {
        await foreach (var hookEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            MouseEvent?.Invoke(this, hookEvent);
        }
    }
}
