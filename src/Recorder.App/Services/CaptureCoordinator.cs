using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Recorder.App.Overlay;
using Recorder.App.Resources;
using Recorder.Core.Automation;
using Recorder.Core.Interop;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;

namespace Recorder.App.Services;

/// <summary>
/// Captures elements: the global capture shortcut (any mode), Pick mode (hover highlight, next click captured and
/// swallowed, Esc cancels) and the highlight overlay. Every result becomes a short plain message. UI thread.
/// </summary>
public sealed partial class CaptureCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(800);
    private readonly IUiaService _uia;
    private readonly HoverTracker _hover;
    private readonly IMouseHook _hook;
    private readonly TargetTracker _target;
    private readonly ElementCatalog _catalog;
    private readonly HotkeyService _hotkeys;
    private readonly ToastService _toasts;
    private readonly BannerService _banners;
    private readonly ElementLocating _locating;
    private readonly Overlays _overlays;
    private readonly HoverHighlighter _highlighter;
    private readonly ILogger<CaptureCoordinator> _logger;
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _flashTimer;
    private readonly HotkeyGesture _captureGesture;
    private PickSession? _pick;
    private bool _disposed;

    public CaptureCoordinator(
        IUiaService uia, ILocatorService locators, HoverTracker hover, IMouseHook hook, TargetTracker target, ElementCatalog catalog, HotkeyService hotkeys,
        ToastService toasts, BannerService banners, ElementLocating locating, Overlays overlays, SettingsStore settings, ILogger<CaptureCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (_uia, _hover, _hook, _target, _catalog, _hotkeys, _toasts, _banners, _logger) = (uia, hover, hook, target, catalog, hotkeys, toasts, banners, logger);
        (_locating, _overlays) = (locating, overlays);
        _highlighter = new HoverHighlighter(locators, catalog, () => overlays.Hover);
        _captureGesture = HotkeyGesture.TryParse(settings.Current.CaptureHotkey, out var gesture)
            ? gesture
            : HotkeyGesture.TryParse(new AppSettings().CaptureHotkey, out var fallback) ? fallback : throw new InvalidOperationException("Invalid default shortcut.");
        _flashTimer = new DispatcherTimer(FlashDuration, DispatcherPriority.Normal, (_, _) => EndFlash(), _dispatcher) { IsEnabled = false };
    }

    /// <summary>An element was captured (new or already listed). Raised on the UI thread.</summary>
    public event EventHandler<PageElement>? Captured;

    public event EventHandler? PickingChanged;

    /// <summary>True while the hovered target does not respond.</summary>
    public event EventHandler<bool>? TargetBusyChanged;

    public bool IsPicking => _pick is not null;

    public string CaptureGestureText => _captureGesture.ToString();

    private HighlightOverlay Overlay => _overlays.Hover;

    public void Initialize(Window mainWindow)
    {
        _hotkeys.Attach(mainWindow);
        _hotkeys.Pressed += OnHotkey;
        _hover.Updated += OnHoverUpdated;
        if (!_hotkeys.Register(HotkeyId.Capture, _captureGesture))
        {
            _banners.Show("hotkey", Text.Format(Strings.Banner_HotkeyTaken, _captureGesture), isWarning: true);
        }
    }

    public void TogglePick()
    {
        if (IsPicking)
        {
            _pick!.Cancel();
        }
        else
        {
            _ = PickAsync();
        }
    }

    /// <summary>Captures an element the user is already looking at (for example an ancestor in the Inspector).</summary>
    public void Capture(ElementSnapshot snapshot) => HandleCapture(UiaResult.Ok(snapshot), flash: true);

    // Called on the UI thread when the main window closes, and again by the DI container: idempotent.
    // The overlay windows and the shortcuts belong to the UI thread, so they are released here, not by the container.
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pick?.Cancel();
        _hotkeys.Pressed -= OnHotkey;
        _hover.Updated -= OnHoverUpdated;
        _hotkeys.Dispose();
        await _hover.StopAsync().ConfigureAwait(true);
        _hook.Uninstall();
        _flashTimer.Stop();
        _overlays.Dispose();
    }

    private async Task PickAsync()
    {
        if (!_target.State.IsAttached)
        {
            _toasts.Show(Strings.Toast_ChooseAppFirst);
            return;
        }

        var session = new PickSession(_hook, _uia, _target);
        if (!session.Start())
        {
            session.Dispose();
            _banners.Show("hook", Text.Format(Strings.Banner_HookBlocked, _captureGesture), isWarning: true);
            return;
        }

        _pick = session;
        EndFlash();
        _hotkeys.Register(HotkeyId.CancelMode, HotkeyGesture.Escape);
        _hover.Start();
        PickingChanged?.Invoke(this, EventArgs.Empty);

        var result = await session.Completion.ConfigureAwait(true);
        _pick = null;
        _hotkeys.Unregister(HotkeyId.CancelMode);
        await _hover.StopAsync().ConfigureAwait(true);
        _highlighter.Hide();
        session.Dispose();
        TargetBusyChanged?.Invoke(this, false);
        PickingChanged?.Invoke(this, EventArgs.Empty);
        if (result is { } capture)
        {
            HandleCapture(capture, flash: true);
        }
    }

    private void OnHotkey(object? sender, HotkeyId id)
    {
        if (id == HotkeyId.Capture)
        {
            _ = CaptureUnderCursorAsync();
        }
        else if (id == HotkeyId.CancelMode)
        {
            _pick?.Cancel();
        }
    }

    private async Task CaptureUnderCursorAsync()
    {
        var point = Win32.CursorPosition();
        if (!_target.State.IsAttached && !TryTargetAppAt(point))
        {
            return;
        }

        var result = await _uia.CaptureAtAsync(point, CancellationToken.None).ConfigureAwait(true);
        if (result.IsOk && !_target.Owns(result.Value!))
        {
            result = UiaResult.Fail<ElementSnapshot>(Win32.ProcessAtPoint(point) == Environment.ProcessId ? UiaStatus.OwnWindow : UiaStatus.NothingThere);
        }

        _pick?.Cancel();
        HandleCapture(result, flash: true);
    }

    // With no app chosen yet, the shortcut picks the app under the cursor: one step less for the user.
    private bool TryTargetAppAt(PixelPoint point)
    {
        var processId = Win32.ProcessAtPoint(point);
        var window = processId == 0 || processId == Environment.ProcessId
            ? null
            : WindowEnumerator.GetAppWindows().FirstOrDefault(w => w.ProcessId == processId);
        if (window is null)
        {
            _toasts.Show(Text.Format(Strings.Toast_NoAppUnderCursor, _captureGesture));
            return false;
        }

        _target.Select(window);
        _toasts.Show(Text.Format(Strings.Toast_NowTargeting, window.ProcessName));
        return _target.State.IsAttached;
    }

    private void HandleCapture(UiaResult<ElementSnapshot> result, bool flash)
    {
        var app = _target.State.ProcessName ?? string.Empty;
        switch (result.Status)
        {
            case UiaStatus.Ok when result.Value is { } snapshot:
                var (element, isNew) = _catalog.Add(snapshot);
                LogCaptured(_logger, snapshot.ControlType, isNew);
                _toasts.Show(Text.Format(isNew ? Strings.Toast_Captured : Strings.Toast_AlreadyCaptured, element.VariableName));
                if (flash)
                {
                    Flash(snapshot);
                }

                Captured?.Invoke(this, element);
                if (isNew)
                {
                    _ = _locating.LocateAsync(element);
                }

                break;
            case UiaStatus.Busy:
                _toasts.Show(Text.Format(Strings.Toast_TargetBusy, app));
                break;
            case UiaStatus.AccessDenied:
                _banners.Show("target", Strings.Banner_Elevated, isWarning: true, Strings.Banner_Elevated_Action, ElevationRestart.Restart);
                break;
            case UiaStatus.OwnWindow:
                _toasts.Show(Text.Format(Strings.Toast_OwnWindow, app));
                break;
            case UiaStatus.Gone:
                _toasts.Show(Strings.Toast_ElementGone);
                break;
            case UiaStatus.Cancelled:
                break;
            default:
                _toasts.Show(Text.Format(Strings.Toast_NothingThere, app));
                break;
        }
    }

    // A short highlight confirms what was captured outside Pick mode.
    private void Flash(ElementSnapshot snapshot)
    {
        if (IsPicking)
        {
            return;
        }

        Overlay.Show(snapshot.Bounds, ElementLabel.For(snapshot), HighlightStyle.Captured);
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void EndFlash()
    {
        _flashTimer.Stop();
        if (!IsPicking)
        {
            _overlays.HoverIfCreated?.Hide();
        }
    }

    private void OnHoverUpdated(object? sender, HoverUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (!IsPicking)
        {
            return;
        }

        TargetBusyChanged?.Invoke(this, update.Kind == HoverKind.Busy);
        if (update is { Kind: HoverKind.Element, Element: { } element })
        {
            _highlighter.Show(element);
        }
        else
        {
            _highlighter.Hide();
        }
    });

    [LoggerMessage(EventId = 610, Level = LogLevel.Information, Message = "Captured element of type {ControlType} (new: {IsNew})")]
    private static partial void LogCaptured(ILogger logger, string controlType, bool isNew);
}
