using Microsoft.Extensions.Logging;
using Recorder.Core.Model;
using Recorder.Core.Persistence;

namespace Recorder.Core.Automation;

public enum TargetStatus
{
    /// <summary>No app chosen yet.</summary>
    None,

    /// <summary>Attached to a running process.</summary>
    Attached,

    /// <summary>Waiting for the remembered process to start (again).</summary>
    Waiting,

    /// <summary>The target runs as administrator and the recorder does not, so UI Automation cannot see it.</summary>
    Elevated,
}

/// <param name="WindowTitle">The target's main window title. Can contain customer data: shown, never logged.</param>
/// <param name="WasClosed">True when waiting because the target closed (not because it has not started yet).</param>
public sealed record TargetState(
    TargetStatus Status, string? ProcessName, int ProcessId, nint WindowHandle, string? WindowTitle, string? ExecutablePath, bool WasClosed)
{
    public static TargetState None { get; } = new(TargetStatus.None, null, 0, 0, null, null, false);

    public bool IsAttached => Status == TargetStatus.Attached;
}

/// <summary>Decides which elements belong to the target app.</summary>
public interface ITargetFilter
{
    /// <summary>True when the top-level window under the point belongs to the target. Fast: safe in a mouse hook.</summary>
    bool IsOverTarget(PixelPoint point);

    /// <summary>
    /// True when the element belongs to the target. Embedded web content (WebView2) is served by another process,
    /// so ownership is decided by the top-level window that contains the element (Findings F-12).
    /// </summary>
    bool Owns(ElementSnapshot snapshot);
}

/// <summary>
/// Tracks the app being automated: remembers it by process name, notices when it closes and re-attaches when it
/// starts again. Only processes in the recorder's own Windows session count (terminal servers run many copies).
/// </summary>
public sealed partial class TargetTracker : ITargetFilter, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly SettingsStore _settings;
    private readonly ITargetEnvironment _environment;
    private readonly TimeProvider _time;
    private readonly ILogger<TargetTracker> _logger;
    private readonly Lock _gate = new();

    // Serializes Poll, Select and Clear (timer thread and UI thread), which replace the process watch.
    private readonly Lock _pollGate = new();
    private readonly CancellationTokenSource _stop = new();
    private IProcessWatch? _watch;
    private Task? _loop;
    private TargetState _state = TargetState.None;
    private int _disposed;

    public TargetTracker(SettingsStore settings, ITargetEnvironment environment, TimeProvider time, ILogger<TargetTracker> logger)
    {
        _settings = settings;
        _environment = environment;
        _time = time;
        _logger = logger;
    }

    /// <summary>Raised on a background thread whenever the state changes.</summary>
    public event EventHandler<TargetState>? Changed;

    public TargetState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Restores the remembered target and starts watching for it.</summary>
    public void Start()
    {
        var remembered = _settings.Current.LastTargetProcessName;
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            SetState(new TargetState(TargetStatus.Waiting, remembered, 0, 0, null, null, WasClosed: false));
            Poll();
        }

        _loop ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public void Select(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _settings.Update(settings => settings with { LastTargetProcessName = window.ProcessName });
        lock (_pollGate)
        {
            Attach(window);
        }
    }

    public void Clear()
    {
        _settings.Update(settings => settings with { LastTargetProcessName = null });
        lock (_pollGate)
        {
            _watch?.Dispose();
            _watch = null;
            SetState(TargetState.None);
        }
    }

    /// <summary>Checks the target now instead of waiting for the next poll (tests and the "Refresh" button).</summary>
    public void Poll()
    {
        lock (_pollGate)
        {
            PollCore();
        }
    }

    private void PollCore()
    {
        var state = State;
        switch (state.Status)
        {
            case TargetStatus.Attached or TargetStatus.Elevated when _watch?.HasExited == true:
                LogClosed(_logger, state.ProcessName);
                SetState(state with { Status = TargetStatus.Waiting, ProcessId = 0, WindowHandle = 0, WindowTitle = null, WasClosed = true });
                break;
            case TargetStatus.Attached:
                RefreshWindow(state);
                break;
            case TargetStatus.Waiting:
                var window = _environment.GetAppWindows()
                    .FirstOrDefault(w => string.Equals(w.ProcessName, state.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (window is not null)
                {
                    LogReattached(_logger, window.ProcessName, window.ProcessId);
                    Attach(window);
                }

                break;
        }
    }

    public bool IsOverTarget(PixelPoint point)
    {
        var state = State;
        return state.IsAttached && _environment.ProcessAtPoint(point) == state.ProcessId;
    }

    public bool Owns(ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var state = State;
        return state.IsAttached
            && (snapshot.ProcessId == state.ProcessId || _environment.ProcessOfWindow(snapshot.TopLevelWindow.Handle) == state.ProcessId);
    }

    // Idempotent: the DI container disposes this object twice, once as itself and once as ITargetFilter.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _stop.Dispose();
        _watch?.Dispose();
    }

    private void Attach(AppWindow window)
    {
        _watch?.Dispose();
        _watch = _environment.Watch(window.ProcessId);
        var elevated = _environment.ElevationOf(window.ProcessId) == Interop.ElevationState.Elevated && !_environment.IsRecorderElevated;
        if (elevated)
        {
            LogElevated(_logger, window.ProcessName);
        }

        SetState(new TargetState(
            elevated ? TargetStatus.Elevated : TargetStatus.Attached,
            window.ProcessName, window.ProcessId, window.Handle, window.Title, window.ExecutablePath, WasClosed: false));
    }

    // The process can swap its main window (a login form closes, the main form opens) and titles change.
    private void RefreshWindow(TargetState state)
    {
        var windows = _environment.GetAppWindows().Where(w => w.ProcessId == state.ProcessId).ToList();
        var current = windows.Find(w => w.Handle == state.WindowHandle) ?? windows.FirstOrDefault();
        if (current is not null && (current.Handle != state.WindowHandle || current.Title != state.WindowTitle))
        {
            SetState(state with { WindowHandle = current.Handle, WindowTitle = current.Title });
        }
    }

    private void SetState(TargetState state)
    {
        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        Changed?.Invoke(this, state);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Poll();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Target {ProcessName} closed; waiting for it to start again")]
    private static partial void LogClosed(ILogger logger, string? processName);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "Re-attached to {ProcessName} (process {ProcessId})")]
    private static partial void LogReattached(ILogger logger, string processName, int processId);

    [LoggerMessage(EventId = 402, Level = LogLevel.Warning, Message = "Target {ProcessName} runs as administrator and the recorder does not")]
    private static partial void LogElevated(ILogger logger, string processName);
}
