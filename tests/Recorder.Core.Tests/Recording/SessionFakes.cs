using System.Collections.Concurrent;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.Core.Tests.Recording;

/// <summary>Answers like the UIA worker from a list of elements: a point finds the element whose bounds contain it.</summary>
internal sealed class FakeUia : IUiaService
{
    private int _captures;
    private int _busyAnswers;

    public List<ElementSnapshot> Elements { get; } = [];

    public ConcurrentDictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, bool?> Checked { get; } = new(StringComparer.Ordinal);

    /// <summary>Every element whose state was read: a password field must never be among them.</summary>
    public ConcurrentQueue<string> StateReads { get; } = new();

    /// <summary>What hovering finds; null: nothing, so every click is looked up at its point.</summary>
    public ElementSnapshot? Hovered { get; set; }

    public int Captures => Volatile.Read(ref _captures);

    public Action<ElementSnapshot?>? Focus { get; private set; }

    /// <summary>How many lookups at a point answer Busy before the target "recovers".</summary>
    public void BusyFor(int answers) => Volatile.Write(ref _busyAnswers, answers);

    public Task<UiaResult<ElementSnapshot>> HoverAtAsync(PixelPoint point, CancellationToken cancellationToken) =>
        Task.FromResult(Hovered is { } hovered && hovered.Bounds.Contains(point) ? UiaResult.Ok(hovered) : UiaResult.Fail<ElementSnapshot>(UiaStatus.NothingThere));

    public Task<UiaResult<ElementSnapshot>> CaptureAtAsync(PixelPoint point, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _captures);
        if (Interlocked.Decrement(ref _busyAnswers) >= 0)
        {
            return Task.FromResult(UiaResult.Fail<ElementSnapshot>(UiaStatus.Busy));
        }

        var element = Elements.LastOrDefault(e => e.Bounds.Contains(point));
        return Task.FromResult(element is null ? UiaResult.Fail<ElementSnapshot>(UiaStatus.NothingThere) : UiaResult.Ok(element));
    }

    public Task<UiaResult<ElementSnapshot>> RefreshAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var element = Elements.FirstOrDefault(e => e.SessionKey == sessionKey);
        return Task.FromResult(element is null ? UiaResult.Fail<ElementSnapshot>(UiaStatus.Gone) : UiaResult.Ok(element));
    }

    public Task<UiaResult<ElementState>> ReadStateAsync(string sessionKey, CancellationToken cancellationToken)
    {
        StateReads.Enqueue(sessionKey);
        var element = Elements.FirstOrDefault(e => e.SessionKey == sessionKey);
        if (element is null)
        {
            return Task.FromResult(UiaResult.Fail<ElementState>(UiaStatus.Gone));
        }

        var state = new ElementState(true, false, element.IsPassword, element.Name, element.IsPassword ? null : Values.GetValueOrDefault(sessionKey), Checked.GetValueOrDefault(sessionKey));
        return Task.FromResult(UiaResult.Ok(state));
    }

    public Task<UiaResult<IAsyncDisposable>> WatchFocusAsync(Func<int, bool> isTargetProcess, Action<ElementSnapshot?> onFocusChanged, CancellationToken cancellationToken)
    {
        Focus = onFocusChanged;
        return Task.FromResult(UiaResult.Ok<IAsyncDisposable>(new Stopper(() => Focus = null)));
    }

    private sealed class Stopper(Action stop) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stop();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A mouse hook driven by the test; it applies the swallow predicate like the real one.</summary>
internal sealed class FakeMouseHook : IMouseHook
{
    private Func<MouseHookEvent, bool>? _shouldSwallow;
    private MouseAction? _swallowNextRelease;
    private int _reinstalls;

    public event EventHandler<MouseHookEvent>? MouseEvent;

    public bool IsInstalled { get; private set; }

    public TimeSpan IdleFor { get; set; }

    public int Reinstalls => Volatile.Read(ref _reinstalls);

    public bool Install(Func<MouseHookEvent, bool>? shouldSwallow)
    {
        _shouldSwallow = shouldSwallow;
        IsInstalled = true;
        return true;
    }

    public bool Reinstall()
    {
        Interlocked.Increment(ref _reinstalls);
        return true;
    }

    public void Uninstall() => IsInstalled = false;

    public void Dispose() => Uninstall();

    public void Raise(MouseAction action, PixelPoint point, uint time = 1000, int wheelDelta = 0)
    {
        if (!IsInstalled)
        {
            return;
        }

        var hookEvent = new MouseHookEvent(action, point, time, Injected: false, Swallowed: false, wheelDelta);
        var swallow = false;
        if (_swallowNextRelease == action)
        {
            _swallowNextRelease = null;
            swallow = true;
        }
        else if (action is MouseAction.LeftDown && _shouldSwallow?.Invoke(hookEvent) == true)
        {
            _swallowNextRelease = MouseAction.LeftUp;
            swallow = true;
        }

        MouseEvent?.Invoke(this, hookEvent with { Swallowed = swallow });
    }

    public void Click(ElementSnapshot element, uint time = 1000)
    {
        Raise(MouseAction.LeftDown, element.Bounds.Center, time);
        Raise(MouseAction.LeftUp, element.Bounds.Center, time + 60);
    }
}

internal sealed class FakeKeyHook : ISpecialKeyHook
{
    public event EventHandler<HotkeyGesture>? KeyPressed;

    public event EventHandler? SecretTyped;

    public bool InstallResult { get; set; } = true;

    public IKeyHookContext? Context { get; private set; }

    public bool Install(IKeyHookContext context)
    {
        Context = InstallResult ? context : null;
        return InstallResult;
    }

    public void Uninstall() => Context = null;

    public void Dispose() => Uninstall();

    public void Press(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None) => KeyPressed?.Invoke(this, KeyClassifier.Chord(virtualKey, modifiers));

    public void Typed() => SecretTyped?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeTarget : IRecordingTarget
{
    public bool Over { get; set; } = true;

    public int ProcessId => 4242;

    public nint MainWindow => Elements.MainWindow;

    public bool IsOverTarget(PixelPoint point) => Over;

    public bool Owns(ElementSnapshot snapshot) => true;
}

internal sealed class FakeCursor : ICursor
{
    private int _reads;

    /// <summary>When set, every read returns a new position, like a hand moving the mouse.</summary>
    public bool Moving { get; set; }

    public PixelPoint Resting { get; set; } = new(1, 1);

    public PixelPoint Position => Moving ? new PixelPoint(Interlocked.Increment(ref _reads), 7) : Resting;
}
