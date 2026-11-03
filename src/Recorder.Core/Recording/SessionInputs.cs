using Recorder.Core.Model;

namespace Recorder.Core.Recording;

/// <summary>Something that happened while recording, queued in order for the session loop.</summary>
internal abstract record SessionInput(DateTimeOffset At);

/// <param name="Element">Looked up at the moment of the press, before the click changes the screen.</param>
internal sealed record PressInput(PointerButton Button, PixelPoint Point, uint Time, Task<ResolvedElement> Element, DateTimeOffset At) : SessionInput(At);

internal sealed record ReleaseInput(PointerButton Button, PixelPoint Point, DateTimeOffset At) : SessionInput(At);

internal sealed record WheelInput(int Notches, Task<ResolvedElement> Element, DateTimeOffset At) : SessionInput(At);

/// <summary>The press Assert mode kept from the app.</summary>
internal sealed record AssertClickInput(PixelPoint Point, Task<ResolvedElement> Element, DateTimeOffset At) : SessionInput(At);

internal sealed record FocusInput(ElementSnapshot? Element, DateTimeOffset At) : SessionInput(At);

internal sealed record KeyInput(HotkeyGesture Chord, DateTimeOffset At) : SessionInput(At);

internal sealed record SecretTypedInput(DateTimeOffset At) : SessionInput(At);

internal sealed record AssertionInput(StepKind Kind, ElementSnapshot Element, string? Text, bool? State, DateTimeOffset At) : SessionInput(At);

/// <param name="Done">Completed once the change is made (Stop waits for it).</param>
internal sealed record ControlInput(RecordingStatus Next, TaskCompletionSource? Done, DateTimeOffset At) : SessionInput(At);

/// <summary>What the session loop needs from the session around it.</summary>
internal interface ISessionHost
{
    RecordingStatus Status { get; }

    void SetStatus(RecordingStatus status);

    void Publish(IReadOnlyList<StepChange> changes);

    void RaiseAssert(AssertTarget target);

    /// <summary>Pause: the hooks are removed (spec 9.1) and hovering stops.</summary>
    Task SuspendAsync();

    /// <summary>Resume: false if Windows refused the mouse hook.</summary>
    bool Resume();
}
