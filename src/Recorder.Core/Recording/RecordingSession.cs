using Microsoft.Extensions.Logging;
using Recorder.Core.Automation;
using Recorder.Core.Interop;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// Records a flow (spec 9): observes clicks, keyboard focus and values, and special keys, and turns them into steps through
/// <see cref="StepBuilder"/>. It never clicks or invokes anything in the target (F-14).
/// <list type="bullet">
/// <item>Hooks only copy events and return. The element of a press is looked up at once (<see cref="ClickResolver"/>) and
/// everything else is handled in order on one loop (<see cref="SessionLoop"/>), so nothing is reordered or lost.</item>
/// <item>Typed text is never captured: an input's value is compared at focus-in and focus-out (<see cref="ValueTracker"/>).
/// Password fields are never read; typing into one becomes a test-data step.</item>
/// </list>
/// </summary>
public sealed partial class RecordingSession : IKeyHookContext, ISessionHost, IAsyncDisposable
{
    private readonly IUiaService _uia;
    private readonly IMouseHook _mouse;
    private readonly ISpecialKeyHook _keys;
    private readonly IRecordingTarget _target;
    private readonly ICursor _cursor;
    private readonly RecordingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RecordingSession> _logger;
    private readonly HoverTracker _hover;
    private readonly ClickResolver _resolver;
    private readonly ValueTracker _values;
    private readonly CancellationTokenSource _stop = new();
    private SessionLoop? _loop;
    private IAsyncDisposable? _focusWatch;
    private Task? _loopTask;
    private Task? _watchdog;
    private volatile RecordingStatus _status;
    private volatile bool _keysActive;
    private int _assertArmed;
    private int _disposed;

    public RecordingSession(
        IUiaService uia, IMouseHook mouse, ISpecialKeyHook keys, IRecordingTarget target, ICursor cursor, RecordingOptions options, TimeProvider time,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);
        (_uia, _mouse, _keys, _target, _cursor, _options, _time) = (uia, mouse, keys, target, cursor, options, time);
        _logger = loggers.CreateLogger<RecordingSession>();
        var hoverOptions = HoverOptions.Default with { Refresh = options.HoverRefresh };
        _hover = new HoverTracker(cursor, uia, target, hoverOptions, time, loggers.CreateLogger<HoverTracker>());
        _hover.Updated += (_, update) => HoverUpdated?.Invoke(this, update);
        _resolver = new ClickResolver(uia, target, _hover, options, time, _stop.Token);
        _values = new ValueTracker(uia, () => _keysActive);
    }

    /// <summary>Steps were added or replaced. Raised on the session's own thread, in order.</summary>
    public event EventHandler<IReadOnlyList<StepChange>>? StepsChanged;

    public event EventHandler<RecordingStatus>? StatusChanged;

    /// <summary>The click of Assert mode landed: show the assertion picker for this element.</summary>
    public event EventHandler<AssertTarget>? AssertRequested;

    /// <summary>The element under the resting cursor, for Assert mode's highlight. Raised on a background thread.</summary>
    public event EventHandler<HoverUpdate>? HoverUpdated;

    public RecordingStatus Status => _status;

    /// <summary>False when special-key recording is off or Windows refused the keyboard hook.</summary>
    public bool SpecialKeysActive => _keysActive;

    public bool IsAssertArmed => Volatile.Read(ref _assertArmed) != 0;

    bool IKeyHookContext.IsPasswordFocused => _values.IsPasswordFocused;

    /// <summary>
    /// Starts recording. Returns false if Windows refused the mouse hook. A refused keyboard hook only turns off
    /// special-key recording (<see cref="SpecialKeysActive"/>).
    /// </summary>
    public async Task<bool> StartAsync()
    {
        var builder = new StepBuilder(_target.MainWindow, _options.DoubleClickTime ?? Win32.DoubleClickTime(), _options.ClickSlop, _options.MaskTypedValues);
        var loop = new SessionLoop(_uia, builder, _values, this, _options, _time, _logger);
        _loop = loop;
        _status = RecordingStatus.Recording;
        if (!InstallHooks())
        {
            _status = RecordingStatus.Idle;
            return false;
        }

        var watch = await _uia.WatchFocusAsync(pid => pid == _target.ProcessId, element => loop.Post(new FocusInput(element, _time.GetUtcNow())), _stop.Token).ConfigureAwait(false);
        _focusWatch = watch.Value;
        if (!watch.IsOk)
        {
            LogNoFocusTracking(_logger, watch.Status);
        }

        _hover.Start();
        _loopTask = Task.Run(() => loop.RunAsync(_stop.Token));
        var watchdog = new HookWatchdog(_mouse, _cursor, _target, _options, _time, () => _status == RecordingStatus.Recording, ReinstallHooks);
        _watchdog = Task.Run(() => watchdog.RunAsync(_stop.Token));
        LogStarted(_logger, _keysActive);
        StatusChanged?.Invoke(this, RecordingStatus.Recording);
        return true;
    }

    public void Pause() => _loop?.Post(new ControlInput(RecordingStatus.Paused, null, _time.GetUtcNow()));

    public void Resume() => _loop?.Post(new ControlInput(RecordingStatus.Recording, null, _time.GetUtcNow()));

    /// <summary>Records what is still pending (the focused input's value), then stops.</summary>
    public async Task StopAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_loop is null || _loopTask is null || !_loop.Post(new ControlInput(RecordingStatus.Idle, done, _time.GetUtcNow())))
        {
            return;
        }

        await done.Task.ConfigureAwait(false);
        await _loopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await ShutDownAsync().ConfigureAwait(false);
    }

    /// <summary>Assert mode: the next click on the target is kept from it and opens the assertion picker (spec 9.6).</summary>
    public void ArmAssert()
    {
        if (_status == RecordingStatus.Recording)
        {
            Volatile.Write(ref _assertArmed, 1);
        }
    }

    public void CancelAssert() => Volatile.Write(ref _assertArmed, 0);

    /// <summary>Adds the assertion the user chose in the picker, after any step still pending.</summary>
    public void AddAssertion(StepKind kind, ElementSnapshot element, string? text, bool? state) =>
        _loop?.Post(new AssertionInput(kind, element, text, state, _time.GetUtcNow()));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loop?.Complete();
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loopTask is not null)
        {
            await _loopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await ShutDownAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    bool IKeyHookContext.IsTargetInFront() => _status == RecordingStatus.Recording && Win32.ForegroundProcess() == _target.ProcessId;

    bool IKeyHookContext.IsReserved(HotkeyModifiers modifiers, int virtualKey) =>
        (virtualKey == 0x1B && IsAssertArmed) || _options.ReservedKeys.Any(key => key.Modifiers == modifiers && key.VirtualKey == virtualKey);

    void ISessionHost.SetStatus(RecordingStatus status)
    {
        _status = status;
        LogStatus(_logger, status);
        StatusChanged?.Invoke(this, status);
    }

    void ISessionHost.Publish(IReadOnlyList<StepChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            LogStep(_logger, change.Step.Kind, change is StepReplaced);
        }

        StepsChanged?.Invoke(this, changes);
    }

    void ISessionHost.RaiseAssert(AssertTarget target) => AssertRequested?.Invoke(this, target);

    async Task ISessionHost.SuspendAsync()
    {
        RemoveHooks();
        await _hover.StopAsync().ConfigureAwait(false);
    }

    bool ISessionHost.Resume()
    {
        if (!InstallHooks())
        {
            return false;
        }

        _hover.Start();
        return true;
    }

    // Hook dispatcher thread. The element of a press is looked up now, before the click changes the screen.
    private void OnMouse(object? sender, MouseHookEvent hookEvent)
    {
        var at = _time.GetUtcNow();
        switch (hookEvent.Action)
        {
            case MouseAction.LeftDown when hookEvent.Swallowed:
                _loop?.Post(new AssertClickInput(hookEvent.Point, _resolver.ResolveAsync(hookEvent.Point), at));
                break;
            case MouseAction.LeftDown or MouseAction.RightDown when !hookEvent.Swallowed && _target.IsOverTarget(hookEvent.Point):
                var button = hookEvent.Action == MouseAction.LeftDown ? PointerButton.Left : PointerButton.Right;
                _loop?.Post(new PressInput(button, hookEvent.Point, hookEvent.Time, _resolver.ResolveAsync(hookEvent.Point), at));
                break;
            case MouseAction.LeftUp or MouseAction.RightUp when !hookEvent.Swallowed:
                _loop?.Post(new ReleaseInput(hookEvent.Action == MouseAction.LeftUp ? PointerButton.Left : PointerButton.Right, hookEvent.Point, at));
                break;
            case MouseAction.Wheel when _options.RecordScrolls && hookEvent.WheelDelta != 0 && _target.IsOverTarget(hookEvent.Point):
                var notches = Math.Sign(hookEvent.WheelDelta) * Math.Max(1, Math.Abs(hookEvent.WheelDelta) / 120);
                _loop?.Post(new WheelInput(notches, _resolver.ResolveAsync(hookEvent.Point), at));
                break;
        }
    }

    private void OnKey(object? sender, HotkeyGesture chord) => _loop?.Post(new KeyInput(chord, _time.GetUtcNow()));

    private void OnSecretTyped(object? sender, EventArgs e) => _loop?.Post(new SecretTypedInput(_time.GetUtcNow()));

    private bool InstallHooks()
    {
        _mouse.MouseEvent += OnMouse;
        if (!_mouse.Install(ShouldSwallow))
        {
            _mouse.MouseEvent -= OnMouse;
            LogHookRefused(_logger, "mouse");
            return false;
        }

        if (_options.RecordSpecialKeys)
        {
            _keys.KeyPressed += OnKey;
            _keys.SecretTyped += OnSecretTyped;
            _keysActive = _keys.Install(this);
            if (!_keysActive)
            {
                _keys.KeyPressed -= OnKey;
                _keys.SecretTyped -= OnSecretTyped;
                LogHookRefused(_logger, "keyboard");
            }
        }

        return true;
    }

    private void RemoveHooks()
    {
        _mouse.MouseEvent -= OnMouse;
        _mouse.Uninstall();
        _keys.KeyPressed -= OnKey;
        _keys.SecretTyped -= OnSecretTyped;
        _keys.Uninstall();
        CancelAssert();
    }

    private void ReinstallHooks()
    {
        LogHookLost(_logger);
        _mouse.Reinstall();
        if (_keysActive)
        {
            _keys.Uninstall();
            _keys.Install(this);
        }
    }

    private async Task ShutDownAsync()
    {
        RemoveHooks();
        await _hover.StopAsync().ConfigureAwait(false);
        if (_watchdog is not null)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _watchdog.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _watchdog = null;
        }

        if (Interlocked.Exchange(ref _focusWatch, null) is { } watch)
        {
            await watch.DisposeAsync().ConfigureAwait(false);
        }

        if (_status != RecordingStatus.Idle)
        {
            _status = RecordingStatus.Idle;
            StatusChanged?.Invoke(this, RecordingStatus.Idle);
        }
    }

    // Inside the mouse hook callback: cheap checks only. Takes exactly one press while Assert mode is armed.
    private bool ShouldSwallow(MouseHookEvent hookEvent) =>
        hookEvent.Action == MouseAction.LeftDown
        && Volatile.Read(ref _assertArmed) != 0
        && _target.IsOverTarget(hookEvent.Point)
        && Interlocked.Exchange(ref _assertArmed, 0) != 0;

    [LoggerMessage(EventId = 700, Level = LogLevel.Information, Message = "Recording started (special keys: {SpecialKeys})")]
    private static partial void LogStarted(ILogger logger, bool specialKeys);

    [LoggerMessage(EventId = 701, Level = LogLevel.Warning, Message = "Windows refused the {Hook} hook")]
    private static partial void LogHookRefused(ILogger logger, string hook);

    [LoggerMessage(EventId = 702, Level = LogLevel.Warning, Message = "The mouse hook stopped receiving input; it was installed again")]
    private static partial void LogHookLost(ILogger logger);

    [LoggerMessage(EventId = 703, Level = LogLevel.Warning, Message = "Keyboard focus cannot be followed ({Status}); typed values will not be recorded")]
    private static partial void LogNoFocusTracking(ILogger logger, UiaStatus status);

    // Kinds only: values, names and titles are customer data and never logged.
    [LoggerMessage(EventId = 710, Level = LogLevel.Debug, Message = "Step {Kind} (replaces an earlier one: {Replaced})")]
    private static partial void LogStep(ILogger logger, StepKind kind, bool replaced);

    [LoggerMessage(EventId = 711, Level = LogLevel.Information, Message = "Recording {Status}")]
    private static partial void LogStatus(ILogger logger, RecordingStatus status);
}
