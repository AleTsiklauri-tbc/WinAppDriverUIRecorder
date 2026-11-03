using System.Globalization;
using Recorder.Core.Model;

namespace Recorder.Core.Recording;

public enum KeyKind
{
    /// <summary>Shift, Ctrl, Alt or Windows on its own.</summary>
    Modifier,

    /// <summary>A key that types or deletes text (letters, digits, punctuation, space, Backspace). Dropped, never stored.</summary>
    Typing,

    /// <summary>Enter, Tab, Esc, F1–F12, Delete, the arrows, and Ctrl or Alt chords such as Ctrl+S (spec 9.4).</summary>
    Recordable,

    /// <summary>Anything else: Home, Page Up, Caps Lock, Windows shortcuts. Ignored.</summary>
    Other,
}

/// <summary>
/// Sorts key presses by virtual-key code alone, without ever turning one into a character. Pure, so every rule is tested.
/// </summary>
public static class KeyClassifier
{
    private static readonly Dictionary<int, string> Named = new()
    {
        [0x0D] = "Enter", [0x09] = "Tab", [0x1B] = "Esc", [0x2E] = "Delete",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
    };

    public static KeyKind Classify(int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C)
        {
            return KeyKind.Modifier;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            return KeyKind.Other;
        }

        if (Named.ContainsKey(virtualKey) || IsFunctionKey(virtualKey))
        {
            return KeyKind.Recordable;
        }

        if (virtualKey is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A))
        {
            // Ctrl+Alt together is AltGr on many keyboard layouts, which types characters: treated as typing.
            var ctrl = modifiers.HasFlag(HotkeyModifiers.Control);
            var alt = modifiers.HasFlag(HotkeyModifiers.Alt);
            return ctrl ^ alt ? KeyKind.Recordable : KeyKind.Typing;
        }

        // 0xE5 (IME processing) and 0xE7 (VK_PACKET: Unicode text from input methods, the touch keyboard or SendInput) type too.
        return virtualKey is 0x08 or 0x20 or (>= 0x60 and <= 0x6F) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2 or 0xE5 or 0xE7
            ? KeyKind.Typing
            : KeyKind.Other;
    }

    /// <summary>
    /// A key pressed while a password field has focus (D-59). Keys that type, and Delete, are <see cref="KeyKind.Typing"/>:
    /// only the fact is passed on. Enter and Tab say nothing about the password and are <see cref="KeyKind.Recordable"/>, so a
    /// login form submitted with Enter is recorded. Everything else is <see cref="KeyKind.Other"/>: dropped.
    /// </summary>
    public static KeyKind InPasswordField(int virtualKey, HotkeyModifiers modifiers)
    {
        var kind = Classify(virtualKey, modifiers);
        if (kind == KeyKind.Typing || virtualKey == 0x2E)
        {
            return KeyKind.Typing;
        }

        return kind == KeyKind.Recordable && virtualKey is 0x0D or 0x09 ? KeyKind.Recordable : KeyKind.Other;
    }

    /// <summary>The chord as the user would write it: "Enter", "Shift+Tab", "Ctrl+S". Only for <see cref="KeyKind.Recordable"/> keys.</summary>
    public static HotkeyGesture Chord(int virtualKey, HotkeyModifiers modifiers)
    {
        string name;
        if (Named.TryGetValue(virtualKey, out var named))
        {
            name = named;
        }
        else if (IsFunctionKey(virtualKey))
        {
            name = "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
        }
        else if (virtualKey is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A))
        {
            name = ((char)virtualKey).ToString();
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), "Not a recordable key.");
        }

        return new HotkeyGesture(modifiers, virtualKey, name);
    }

    /// <summary>True for keys that edit text inside an input; their effect is recorded as the input's new value instead (D-46).</summary>
    public static bool EditsText(HotkeyGesture chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.VirtualKey is 0x2E or (>= 0x25 and <= 0x28))
        {
            return true;
        }

        return chord.Modifiers == HotkeyModifiers.Control && chord.VirtualKey is 0x41 or 0x43 or 0x56 or 0x58 or 0x59 or 0x5A;
    }

    private static bool IsFunctionKey(int virtualKey) => virtualKey is >= 0x70 and <= 0x7B;
}
