using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

public enum RecordingStatus
{
    Idle,
    Recording,
    Paused,
}

/// <summary>The app being recorded: its process and main window, and what belongs to it.</summary>
public interface IRecordingTarget : ITargetFilter
{
    /// <summary>0 while no app is attached.</summary>
    int ProcessId { get; }

    /// <summary>The window a test session starts in.</summary>
    nint MainWindow { get; }
}

public sealed record RecordingOptions
{
    public static RecordingOptions Default { get; } = new();

    /// <summary>Spec 9.4: record Enter, Tab, Esc, F1–F12, Delete, arrows and Ctrl or Alt chords.</summary>
    public bool RecordSpecialKeys { get; init; } = true;

    /// <summary>Spec 9.3: every typed value becomes a test-data key.</summary>
    public bool MaskTypedValues { get; init; }

    /// <summary>Spec 9.5: wheel scrolls are recorded as comments.</summary>
    public bool RecordScrolls { get; init; }

    /// <summary>The recorder's own global shortcuts, never recorded as key presses.</summary>
    public IReadOnlyList<HotkeyGesture> ReservedKeys { get; init; } = [];

    /// <summary>Spec 9.2: a hover snapshot younger than this is used for a click inside its bounds.</summary>
    public TimeSpan HoverFreshness { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How often the element under a resting cursor is looked up again, so its snapshot stays fresher than <see cref="HoverFreshness"/>.</summary>
    public TimeSpan HoverRefresh { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Spec 9.5: a check box's state is read this long after the click.</summary>
    public TimeSpan StateDelay { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>How long a click on a busy target is retried before it is kept unidentified (no click is lost, F-16).</summary>
    public TimeSpan BusyRetryFor { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BusyRetryInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Null: the system setting.</summary>
    public TimeSpan? DoubleClickTime { get; init; }

    /// <summary>Spec 9.5: a press and release closer than this many pixels are a click; farther is a drag (ignored).</summary>
    public int ClickSlop { get; init; } = 8;

    public TimeSpan HookCheckInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The mouse hook counts as removed after this long without input while the cursor keeps moving over the target.</summary>
    public TimeSpan HookLostAfter { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>The element clicked in Assert mode, with its current state; Element is null when it could not be read.</summary>
public sealed record AssertTarget(ElementSnapshot? Element, ElementState? State, PixelPoint Point, UiaStatus Status);
