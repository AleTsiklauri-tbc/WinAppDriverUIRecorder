using System.Globalization;
using System.Windows.Input;
using Recorder.Core.Model;

namespace Recorder.App.Controls;

/// <summary>Keys pressed in a shortcut box as a <see cref="HotkeyGesture"/>.</summary>
public static class ShortcutKeys
{
    /// <summary>True for a key that only modifies the next one (Ctrl, Shift, Alt, Win): the gesture is not complete yet.</summary>
    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>The gesture, or null for a key a shortcut can't use (the numeric keypad, punctuation, media keys…).</summary>
    public static HotkeyGesture? Gesture(Key key, ModifierKeys modifiers)
    {
        var name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => (key - Key.D0).ToString(CultureInfo.InvariantCulture),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space or Key.Insert or Key.Delete or Key.Home or Key.End or Key.Left or Key.Up or Key.Right or Key.Down or Key.Tab => key.ToString(),
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            _ => null,
        };
        if (name is null)
        {
            return null;
        }

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(name);
        return HotkeyGesture.TryParse(string.Join('+', parts), out var gesture) ? gesture : null;
    }
}
