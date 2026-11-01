using System.Diagnostics.CodeAnalysis;

namespace Recorder.Core.Model;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>A keyboard shortcut such as "Ctrl+Shift+X", with the Win32 virtual-key code RegisterHotKey needs.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
    };

    public static HotkeyGesture Escape { get; } = new(HotkeyModifiers.None, 0x1B, "Esc");

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "SHIFT" => HotkeyModifiers.Shift,
                "ALT" => HotkeyModifiers.Alt,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => (HotkeyModifiers?)null,
            };
            if (modifier is null || modifiers.HasFlag(modifier.Value))
            {
                return false;
            }

            modifiers |= modifier.Value;
        }

        if (!TryParseKey(parts[^1], out var virtualKey, out var keyName))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, keyName);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string key, out int virtualKey, out string keyName)
    {
        keyName = key.Length == 1 ? key.ToUpperInvariant() : key;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24)
        {
            virtualKey = 0x70 + number - 1;
            keyName = "F" + key[1..];
            return true;
        }

        if (NamedKeys.TryGetValue(key, out virtualKey))
        {
            keyName = NamedKeys.Keys.First(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        return false;
    }
}
