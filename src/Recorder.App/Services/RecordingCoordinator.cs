using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Recorder.App.Resources;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;

namespace Recorder.App.Services;

/// <summary>
/// Starts, pauses and stops recording (spec 9, 11.4): builds a <see cref="RecordingSession"/> from the settings, owns the
/// recording shortcuts (F9 always; F10 and Ctrl+Shift+A only while recording, D-48), feeds steps to the step list and
/// tracks the recording time. UI thread only.
/// </summary>
public sealed partial class RecordingCoordinator : IAsyncDisposable
{
    private readonly IUiaService _uia;
    private readonly IMouseHook _mouse;
    private readonly ISpecialKeyHook _keys;
    private readonly TargetTracker _target;
    private readonly ICursor _cursor;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly ILocatorService _locators;
    private readonly SettingsStore _settings;
    private readonly HotkeyService _hotkeys;
    private readonly ToastService _toasts;
    private readonly BannerService _banners;
    private readonly StepRecorder _recorder;
    private readonly AssertController _asserts;
    private readonly CaptureCoordinator _capture;
    private readonly Shortcuts _shortcuts;
    private readonly ILogger<RecordingCoordinator> _logger;
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private RecordingSession? _session;
    private LocatorPrefetcher? _prefetch;
    private long _activeSince;
    private TimeSpan _activeBefore;
    private bool _busy;
    private bool _disposed;

    public RecordingCoordinator(
        IUiaService uia, IMouseHook mouse, ISpecialKeyHook keys, TargetTracker target, ICursor cursor, TimeProvider time, ILoggerFactory loggers,
        ILocatorService locators, SettingsStore settings, HotkeyService hotkeys, ToastService toasts, BannerService banners, StepRecorder recorder,
        AssertController asserts, CaptureCoordinator capture, Shortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        (_uia, _mouse, _keys, _target, _cursor, _time, _loggers, _locators) = (uia, mouse, keys, target, cursor, time, loggers, locators);
        (_settings, _hotkeys, _toasts, _banners, _recorder, _asserts, _capture) = (settings, hotkeys, toasts, banners, recorder, asserts, capture);
        _shortcuts = shortcuts;
        _logger = loggers.CreateLogger<RecordingCoordinator>();
        asserts.ModeChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recording started, paused, resumed or stopped, or Assert mode changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Recording started: the main window may minimize and the mini-bar appears.</summary>
    public event EventHandler? Started;

    /// <summary>Recording stopped: the main window comes back with the Steps tab and the last step selected.</summary>
    public event EventHandler? Stopped;

    public RecordingStatus Status => _session?.Status ?? RecordingStatus.Idle;

    public bool IsActive => _session is not null;

    public bool IsAsserting => _asserts.IsArmed;

    /// <summary>Time spent recording, pauses excluded.</summary>
    public TimeSpan Elapsed => _activeBefore + (Status == RecordingStatus.Recording ? _time.GetElapsedTime(_activeSince) : TimeSpan.Zero);

    public void Initialize()
    {
        _hotkeys.Pressed += OnHotkey;
        _shortcuts.Register(ShortcutId.Record);

        // New keys from Settings take effect at once; Pause and Assert exist only while recording.
        _shortcuts.Changed += (_, id) =>
        {
            if (id == ShortcutId.Record || (IsActive && id is ShortcutId.Pause or ShortcutId.Assert))
            {
                _shortcuts.Register(id);
            }
        };
    }

    public async Task ToggleRecordAsync()
    {
        if (IsActive)
        {
            await StopAsync().ConfigureAwait(true);
        }
        else
        {
            await StartAsync().ConfigureAwait(true);
        }
    }

    public async Task StartAsync()
    {
        if (IsActive || _busy)
        {
            return;
        }

        if (!_target.State.IsAttached)
        {
            _toasts.Show(Strings.Toast_ChooseAppFirst);
            return;
        }

        if (_capture.IsPicking)
        {
            _capture.TogglePick();
        }

        _busy = true;
        try
        {
            var settings = _settings.Current;
            var options = RecordingOptions.Default with
            {
                RecordSpecialKeys = settings.RecordSpecialKeys,
                MaskTypedValues = settings.MaskTypedValues,
                RecordScrolls = settings.RecordScrolls,
                ReservedKeys = [.. _shortcuts.Current.Values],
            };
            var session = new RecordingSession(_uia, _mouse, _keys, _target, _cursor, options, _time, _loggers);
            var prefetch = new LocatorPrefetcher(_locators, _time);
            session.StepsChanged += (_, changes) => _dispatcher.BeginInvoke(() => _recorder.Apply(changes));
            session.StatusChanged += (_, _) => _dispatcher.BeginInvoke(OnStatusChanged);
            session.AssertRequested += (_, target) => _dispatcher.BeginInvoke(() => _asserts.OnAssertRequested(session, target));
            session.HoverUpdated += (_, update) => prefetch.Observe(update);
            if (!await session.StartAsync().ConfigureAwait(true))
            {
                prefetch.Dispose();
                await session.DisposeAsync().ConfigureAwait(true);
                _banners.Show("record-hook", Strings.Banner_RecordHookBlocked, isWarning: true);
                return;
            }

            _banners.Remove("record-hook");
            if (options.RecordSpecialKeys && !session.SpecialKeysActive)
            {
                _banners.Show("keys", Strings.Banner_KeysUnavailable, isWarning: true);
            }

            (_session, _prefetch) = (session, prefetch);
            _recorder.Prefetch = prefetch;
            _activeBefore = TimeSpan.Zero;
            _activeSince = _time.GetTimestamp();
            _shortcuts.Register(ShortcutId.Pause);
            _shortcuts.Register(ShortcutId.Assert);
            LogStarted(_logger);
            Started?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
    }

    public void TogglePause()
    {
        switch (Status)
        {
            case RecordingStatus.Recording:
                _asserts.Cancel();
                _session!.Pause();
                break;
            case RecordingStatus.Paused:
                _session!.Resume();
                break;
        }
    }

    public void ToggleAssert()
    {
        if (_asserts.IsArmed)
        {
            _asserts.Cancel();
        }
        else if (_session is { Status: RecordingStatus.Recording } session)
        {
            _asserts.Arm(session);
        }
    }

    public async Task StopAsync()
    {
        if (_session is not { } session || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            _asserts.Cancel();
            _activeBefore = Elapsed;
            await session.StopAsync().ConfigureAwait(true);
            await session.DisposeAsync().ConfigureAwait(true);
            _recorder.Prefetch = null;
            _prefetch?.Dispose();
            (_session, _prefetch) = (null, null);
            _shortcuts.Unregister(ShortcutId.Pause);
            _shortcuts.Unregister(ShortcutId.Assert);
            LogStopped(_logger, (int)_activeBefore.TotalSeconds);
            Stopped?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
    }

    // Called on the UI thread when the main window closes, and again by the DI container: idempotent.
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.Pressed -= OnHotkey;
        if (_session is { } session)
        {
            _asserts.Cancel();
            await session.DisposeAsync().ConfigureAwait(true);
            _session = null;
        }

        _prefetch?.Dispose();
    }

    private void OnStatusChanged()
    {
        if (_session is null)
        {
            return;
        }

        if (Status == RecordingStatus.Recording)
        {
            _activeSince = _time.GetTimestamp();
        }
        else if (Status == RecordingStatus.Paused)
        {
            _activeBefore += _time.GetElapsedTime(_activeSince);
            _activeSince = _time.GetTimestamp();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnHotkey(object? sender, HotkeyId id)
    {
        switch (id)
        {
            case HotkeyId.Record:
                _ = ToggleRecordAsync();
                break;
            case HotkeyId.Pause:
                TogglePause();
                break;
            case HotkeyId.Assert:
                ToggleAssert();
                break;
            case HotkeyId.CancelMode when _asserts.IsArmed:
                _asserts.Cancel();
                break;
        }
    }

    [LoggerMessage(EventId = 630, Level = LogLevel.Information, Message = "Recording started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 631, Level = LogLevel.Information, Message = "Recording stopped after {Seconds} s")]
    private static partial void LogStopped(ILogger logger, int seconds);
}
