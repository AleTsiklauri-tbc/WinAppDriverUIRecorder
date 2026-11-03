namespace Recorder.Core.Model;

/// <summary>The global shortcuts a user can change (spec 11.9). Esc, which cancels Pick and Assert mode, is fixed.</summary>
public enum ShortcutId
{
    Capture,
    Record,
    Pause,
    Assert,
}

public enum ShortcutProblem
{
    None,

    /// <summary>A key that types (or Enter, Tab, arrows…) without Ctrl, Alt or Win: it would stop working in every app.</summary>
    NeedsModifier,

    /// <summary>A shortcut every app relies on (Ctrl+C…), or one of the recorder's own (spec 11.9, App scope).</summary>
    Reserved,

    /// <summary>Another of the recorder's global shortcuts already uses it.</summary>
    SameAs,
}

/// <summary>
/// Which keys can be a global shortcut. A global shortcut takes its keys from every app while the recorder runs, so it may
/// not be a typing key on its own, nor a shortcut people use everywhere.
/// </summary>
public static class ShortcutRules
{
    private static readonly HashSet<(HotkeyModifiers, int)> Reserved = [.. new[]
    {
        // Every app: copy, paste, cut, undo, redo, select all, save, open, find, print, new, close; Alt+F4, Alt+Tab.
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Ctrl+S", "Ctrl+O", "Ctrl+F", "Ctrl+P", "Ctrl+N", "Ctrl+W",
        "Alt+F4", "Alt+Tab",

        // The recorder's own window (spec 11.9, App scope), and Esc for Pick and Assert mode.
        "Ctrl+Shift+C", "Ctrl+Shift+S", "Ctrl+E", "Ctrl+D", "Alt+Up", "Alt+Down", "F2", "Esc",
    }.Select(Key)];

    public static IReadOnlyDictionary<ShortcutId, HotkeyGesture> Defaults { get; } = new Dictionary<ShortcutId, HotkeyGesture>
    {
        [ShortcutId.Capture] = Parse("Ctrl+Shift+X"),
        [ShortcutId.Record] = Parse("F9"),
        [ShortcutId.Pause] = Parse("F10"),
        [ShortcutId.Assert] = Parse("Ctrl+Shift+A"),
    };

    /// <summary>Whether <paramref name="gesture"/> can be <paramref name="id"/>'s shortcut, given the others' (<paramref name="current"/>).</summary>
    /// <returns>The problem, and for <see cref="ShortcutProblem.SameAs"/> which shortcut uses it.</returns>
    public static (ShortcutProblem Problem, ShortcutId? Other) Check(ShortcutId id, HotkeyGesture gesture, IReadOnlyDictionary<ShortcutId, HotkeyGesture> current)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(current);
        var modified = (gesture.Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0;
        if (!modified && gesture.VirtualKey is not (>= 0x70 and <= 0x87))
        {
            return (ShortcutProblem.NeedsModifier, null);
        }

        if (Reserved.Contains(Key(gesture)))
        {
            return (ShortcutProblem.Reserved, null);
        }

        foreach (var (other, used) in current)
        {
            if (other != id && Key(used) == Key(gesture))
            {
                return (ShortcutProblem.SameAs, other);
            }
        }

        return (ShortcutProblem.None, null);
    }

    /// <summary>Two gestures press the same keys (the key's spelling does not matter: "Esc", "Escape").</summary>
    public static bool Same(HotkeyGesture a, HotkeyGesture b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return Key(a) == Key(b);
    }

    private static (HotkeyModifiers, int) Key(HotkeyGesture gesture) => (gesture.Modifiers, gesture.VirtualKey);

    private static (HotkeyModifiers, int) Key(string text) => Key(Parse(text));

    private static HotkeyGesture Parse(string text) =>
        HotkeyGesture.TryParse(text, out var gesture) ? gesture : throw new ArgumentException("Not a shortcut: " + text, nameof(text));
}
