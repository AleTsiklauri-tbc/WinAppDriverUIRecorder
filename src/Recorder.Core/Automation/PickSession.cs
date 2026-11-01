using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.Core.Automation;

/// <summary>
/// Pick mode, like a browser's element picker: the next left click on the target app is captured and does not
/// reach the app (only that one click). The click is never swallowed outside the target.
/// </summary>
public sealed class PickSession : IDisposable
{
    private readonly IMouseHook _hook;
    private readonly IUiaService _uia;
    private readonly ITargetFilter _filter;
    private readonly TaskCompletionSource<UiaResult<ElementSnapshot>?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _clickTaken;

    public PickSession(IMouseHook hook, IUiaService uia, ITargetFilter filter)
    {
        _hook = hook;
        _uia = uia;
        _filter = filter;
    }

    /// <summary>The captured element, a failure status, or null when the pick was cancelled.</summary>
    public Task<UiaResult<ElementSnapshot>?> Completion => _completion.Task;

    /// <summary>Starts listening for the click. Returns false if the mouse hook could not be installed.</summary>
    public bool Start()
    {
        _hook.MouseEvent += OnMouseEvent;
        var installed = _hook.Install(ShouldSwallow);
        if (!installed)
        {
            _hook.MouseEvent -= OnMouseEvent;
        }

        return installed;
    }

    public void Cancel() => _completion.TrySetResult(null);

    public void Dispose()
    {
        _hook.MouseEvent -= OnMouseEvent;
        _hook.Uninstall();
        _completion.TrySetResult(null);
    }

    // Runs inside the hook callback: two cheap Win32 calls, no UI Automation.
    private bool ShouldSwallow(MouseHookEvent hookEvent) =>
        hookEvent.Action == MouseAction.LeftDown
        && Volatile.Read(ref _clickTaken) == 0
        && _filter.IsOverTarget(hookEvent.Point)
        && Interlocked.Exchange(ref _clickTaken, 1) == 0;

    private void OnMouseEvent(object? sender, MouseHookEvent hookEvent)
    {
        if (hookEvent is { Action: MouseAction.LeftDown, Swallowed: true })
        {
            _ = CaptureAsync(hookEvent.Point);
        }
    }

    private async Task CaptureAsync(PixelPoint point)
    {
        var result = await _uia.CaptureAtAsync(point, CancellationToken.None).ConfigureAwait(false);
        if (result.IsOk && !_filter.Owns(result.Value!))
        {
            result = UiaResult.Fail<ElementSnapshot>(UiaStatus.NothingThere);
        }

        _completion.TrySetResult(result);
    }
}
