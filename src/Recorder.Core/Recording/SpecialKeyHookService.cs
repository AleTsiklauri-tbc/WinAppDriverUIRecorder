using System.Runtime.InteropServices;
using System.Threading.Channels;
using Recorder.Core.Interop;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>What the keyboard hook needs to know, asked inside its callback: every answer must be fast.</summary>
public interface IKeyHookContext
{
    /// <summary>True when keyboard input goes to the target app.</summary>
    bool IsTargetInFront();

    /// <summary>True while a password field of the target has keyboard focus.</summary>
    bool IsPasswordFocused { get; }

    /// <summary>The recorder's own shortcuts (F9, Ctrl+Shift+X…), which are never recorded.</summary>
    bool IsReserved(HotkeyModifiers modifiers, int virtualKey);
}

public interface ISpecialKeyHook : IDisposable
{
    /// <summary>Raised on a thread-pool thread for each recordable key: Enter, Tab, Esc, F1–F12, Delete, arrows, Ctrl or Alt chords.</summary>
    event EventHandler<HotkeyGesture>? KeyPressed;

    /// <summary>Raised when a key that types (or Delete) was pressed in a password field. It carries nothing about the key.</summary>
    event EventHandler? SecretTyped;

    /// <summary>Returns false if Windows refused the hook (for example blocked by security software).</summary>
    bool Install(IKeyHookContext context);

    void Uninstall();
}

/// <summary>
/// WH_KEYBOARD_LL for special keys only (spec 9.4). Privacy guarantees, enforced in the callback itself:
/// <list type="bullet">
/// <item>Only the virtual-key code is looked at. Keys are never translated into characters.</item>
/// <item>Keys that type (letters, digits, punctuation, space, Backspace) are dropped inside the callback: nothing about
/// them is stored, queued, raised or logged. Ctrl and Alt chords such as Ctrl+S are not typing and are recorded.</item>
/// <item>While a password field has focus, only Enter and Tab are recorded: they say nothing about the password (D-59).
/// For keys that type, only the fact that something was typed is passed on, without the key, so that a test-data step can
/// be added for the field. Every other key is dropped.</item>
/// <item>Nothing is recorded unless the target app is in front, so typing into the recorder itself or any other app is never seen.</item>
/// </list>
/// The callback queues and returns at once: Windows removes low-level hooks that take too long.
/// </summary>
public sealed class SpecialKeyHookService : ISpecialKeyHook
{
    private static SpecialKeyHookService? s_active;

    private readonly Channel<HotkeyGesture?> _events = Channel.CreateUnbounded<HotkeyGesture?>(new UnboundedChannelOptions { SingleReader = true });
    private IKeyHookContext? _context;
    private Thread? _thread;
    private uint _threadId;
    private Task? _dispatcher;

    public event EventHandler<HotkeyGesture>? KeyPressed;

    public event EventHandler? SecretTyped;

    public bool Install(IKeyHookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (_thread is not null)
        {
            return true;
        }

        if (Interlocked.CompareExchange(ref s_active, this, null) is not null)
        {
            throw new InvalidOperationException("Another keyboard hook is already installed in this process.");
        }

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
            Name = "KeyboardHook",
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
        Interlocked.CompareExchange(ref s_active, null, this);
    }

    public void Dispose()
    {
        Uninstall();
        _events.Writer.TryComplete();
    }

    private static unsafe nint InstallHook() =>
        NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, &HookProc, NativeMethods.GetModuleHandle(null), 0);

    [UnmanagedCallersOnly]
    private static unsafe nint HookProc(int code, nint wParam, nint lParam)
    {
        var active = Volatile.Read(ref s_active);
        if (code >= 0 && active is not null && (uint)wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            try
            {
                active.Handle((int)((NativeMethods.KBDLLHOOKSTRUCT*)lParam)->VirtualKey);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Never let an exception cross into native code; the key simply passes through.
            }
        }

        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }

    // Never swallows: keys always reach the app.
    private void Handle(int virtualKey)
    {
        var context = _context;
        if (context is null || !context.IsTargetInFront())
        {
            return;
        }

        var modifiers = CurrentModifiers();
        var inPassword = context.IsPasswordFocused;
        var kind = inPassword ? KeyClassifier.InPasswordField(virtualKey, modifiers) : KeyClassifier.Classify(virtualKey, modifiers);
        if (kind == KeyKind.Typing)
        {
            // Dropped; in a password field only the fact that something was typed goes on.
            if (inPassword)
            {
                _events.Writer.TryWrite(null);
            }

            return;
        }

        if (kind == KeyKind.Recordable && !context.IsReserved(modifiers, virtualKey))
        {
            _events.Writer.TryWrite(KeyClassifier.Chord(virtualKey, modifiers));
        }
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(NativeMethods.VK_CONTROL))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(NativeMethods.VK_SHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(NativeMethods.VK_MENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;

        static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    // A null item is "something was typed into a password field".
    private async Task DispatchAsync()
    {
        await foreach (var chord in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (chord is null)
            {
                SecretTyped?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                KeyPressed?.Invoke(this, chord);
            }
        }
    }
}
