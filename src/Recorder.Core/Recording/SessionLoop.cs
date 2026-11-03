using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>
/// The recording's ordered loop: every input is handled here, one at a time, so steps come out in the order things
/// happened and a slow lookup never lets a later input overtake an earlier one. Only reads the target (F-14).
/// </summary>
internal sealed partial class SessionLoop(IUiaService uia, StepBuilder builder, ValueTracker values, ISessionHost host, RecordingOptions options, TimeProvider time, ILogger logger)
{
    private static readonly TimeSpan ClickBaselineAge = TimeSpan.FromSeconds(2);
    private readonly Channel<SessionInput> _inputs = Channel.CreateUnbounded<SessionInput>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<PointerButton, PressInput> _presses = [];
    private (ElementSnapshot Element, DateTimeOffset At)? _lastClicked;

    public bool Post(SessionInput input) => _inputs.Writer.TryWrite(input);

    public void Complete() => _inputs.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var input in _inputs.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await HandleAsync(input, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    LogInputFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    private async Task HandleAsync(SessionInput input, CancellationToken token)
    {
        var recording = host.Status == RecordingStatus.Recording;
        switch (input)
        {
            case PressInput press:
                _presses[press.Button] = press;
                if (recording)
                {
                    await RecordValueAsync(press.At, token).ConfigureAwait(false);
                }

                break;
            case ReleaseInput release when _presses.Remove(release.Button, out var press) && recording:
                if (Math.Abs(release.Point.X - press.Point.X) <= options.ClickSlop && Math.Abs(release.Point.Y - press.Point.Y) <= options.ClickSlop)
                {
                    await ClickAsync(press, token).ConfigureAwait(false);
                }

                break;
            case FocusInput focus:
                await FocusAsync(focus, recording, token).ConfigureAwait(false);
                break;
            case KeyInput key when recording && StepBuilder.Records(key.Chord, values.Focused):
                await RecordValueAsync(key.At, token).ConfigureAwait(false);
                host.Publish(builder.Keys(key.Chord, values.Focused, await WindowIfSwitchedAsync(values.Focused, token).ConfigureAwait(false), key.At));
                break;
            case SecretTypedInput:
                values.OnSecretTyped();
                break;
            case WheelInput wheel when recording:
                if ((await wheel.Element.ConfigureAwait(false)).Element is { } scrolled)
                {
                    host.Publish(builder.Scroll(scrolled, await WindowIfSwitchedAsync(scrolled, token).ConfigureAwait(false), wheel.Notches, wheel.At));
                }

                break;
            case AssertClickInput assert:
                await AssertClickAsync(assert, token).ConfigureAwait(false);
                break;
            case AssertionInput assertion:
                var window = await WindowIfSwitchedAsync(assertion.Element, token).ConfigureAwait(false);
                host.Publish(builder.Assert(assertion.Kind, assertion.Element, window, assertion.Text, assertion.State, assertion.At));
                break;
            case ControlInput control:
                await ControlAsync(control, token).ConfigureAwait(false);
                break;
        }
    }

    private async Task ClickAsync(PressInput press, CancellationToken token)
    {
        var resolved = await press.Element.ConfigureAwait(false);
        if (resolved.Status is not (UiaStatus.Ok or UiaStatus.Busy))
        {
            return; // not the target's, nothing there, or gone
        }

        ElementSnapshot? owner = null, window = null;
        if (resolved.Element is { } element)
        {
            owner = StepTargets.OwnerIndex(element) is { } index ? await RefreshAsync(element.Ancestors[index].RuntimeId, token).ConfigureAwait(false) : null;
            window = await WindowIfSwitchedAsync(element, token).ConfigureAwait(false);
            _lastClicked = (element, press.At);
        }

        var changes = builder.Click(new ClickInput(press.Button, resolved.Element, owner, window, press.Point, press.Time, press.At, resolved.Changed));
        host.Publish(changes);
        if (changes.Count > 0 && changes[^1].Step is { Kind: StepKind.SetChecked, State: null, Target: { } box } pending)
        {
            await Task.Delay(options.StateDelay, time, token).ConfigureAwait(false);
            var state = await uia.ReadStateAsync(box.SessionKey, token).ConfigureAwait(false);
            host.Publish(builder.CompleteState(pending, state.IsOk ? state.Value!.IsChecked : null));
        }
    }

    private async Task FocusAsync(FocusInput focus, bool recording, CancellationToken token)
    {
        var element = focus.Element;
        if (element is { ControlType: "Edit" } && element.Ancestors.Count > 0 && element.Ancestors[0].ControlType == "ComboBox")
        {
            element = await RefreshAsync(element.Ancestors[0].RuntimeId, token).ConfigureAwait(false) ?? element; // the text part of a combo box
        }

        if (values.IsFocused(element))
        {
            return;
        }

        if (recording)
        {
            await RecordValueAsync(focus.At, token).ConfigureAwait(false);
        }

        // The click that focused the input, if there was one: pending, or recent.
        ElementSnapshot? clicked = null;
        if (_presses.TryGetValue(PointerButton.Left, out var press))
        {
            clicked = (await press.Element.ConfigureAwait(false)).Element;
        }
        else if (_lastClicked is { } last && focus.At - last.At < ClickBaselineAge)
        {
            clicked = last.Element;
        }

        values.Focus(element, clicked, recording);
    }

    // Records the focused input's value if it changed: before any click or key, on focus-out, and on pause or stop (spec 9.3).
    private async Task RecordValueAsync(DateTimeOffset at, CancellationToken token)
    {
        if (await values.FlushAsync(at, token).ConfigureAwait(false) is { } change)
        {
            host.Publish(builder.Value(change with { Window = await WindowIfSwitchedAsync(change.Element, token).ConfigureAwait(false) }));
        }
    }

    private async Task AssertClickAsync(AssertClickInput assert, CancellationToken token)
    {
        var resolved = await assert.Element.ConfigureAwait(false);
        if (resolved.Element is not { } element)
        {
            host.RaiseAssert(new AssertTarget(null, null, assert.Point, resolved.Status));
            return;
        }

        await RecordValueAsync(assert.At, token).ConfigureAwait(false);
        var state = await uia.ReadStateAsync(element.SessionKey, token).ConfigureAwait(false);
        host.RaiseAssert(new AssertTarget(element, state.Value, assert.Point, state.IsOk ? UiaStatus.Ok : state.Status));
    }

    private async Task ControlAsync(ControlInput control, CancellationToken token)
    {
        switch (control.Next)
        {
            case RecordingStatus.Paused when host.Status == RecordingStatus.Recording:
                await RecordValueAsync(control.At, token).ConfigureAwait(false);
                _presses.Clear();
                await host.SuspendAsync().ConfigureAwait(false);
                host.SetStatus(RecordingStatus.Paused);
                break;
            case RecordingStatus.Recording when host.Status == RecordingStatus.Paused:
                if (host.Resume())
                {
                    await values.RebaselineAsync(token).ConfigureAwait(false);
                    host.SetStatus(RecordingStatus.Recording);
                }

                break;
            case RecordingStatus.Idle:
                if (host.Status == RecordingStatus.Recording)
                {
                    await RecordValueAsync(control.At, token).ConfigureAwait(false);
                }

                Complete();
                break;
        }

        control.Done?.TrySetResult();
    }

    // The top-level window, read in full, when a step lands in another one than the steps before (SwitchWindow, D-43).
    private async Task<ElementSnapshot?> WindowIfSwitchedAsync(ElementSnapshot? element, CancellationToken token)
    {
        if (element is null || element.TopLevelWindow.Handle == 0 || element.TopLevelWindow.Handle == builder.CurrentWindow)
        {
            return null;
        }

        return element.Ancestors.Count == 0 ? element : await RefreshAsync(element.Ancestors[^1].RuntimeId, token).ConfigureAwait(false);
    }

    private async Task<ElementSnapshot?> RefreshAsync(IReadOnlyList<int> runtimeId, CancellationToken token)
    {
        var result = await uia.RefreshAsync(RuntimeKey.Of(runtimeId), token).ConfigureAwait(false);
        return result.IsOk ? result.Value : null;
    }

    [LoggerMessage(EventId = 712, Level = LogLevel.Error, Message = "A recorded input could not be handled")]
    private static partial void LogInputFailed(ILogger logger, Exception exception);
}
