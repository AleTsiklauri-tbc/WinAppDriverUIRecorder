using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Model;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+X", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x58, "Ctrl+Shift+X")]
    [InlineData("shift + ctrl + x", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x58, "Ctrl+Shift+X")]
    [InlineData("Ctrl+Shift+A", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41, "Ctrl+Shift+A")]
    [InlineData("F9", HotkeyModifiers.None, 0x78, "F9")]
    [InlineData("Alt+F12", HotkeyModifiers.Alt, 0x7B, "Alt+F12")]
    [InlineData("Esc", HotkeyModifiers.None, 0x1B, "Esc")]
    [InlineData("Win+Control+7", HotkeyModifiers.Windows | HotkeyModifiers.Control, 0x37, "Ctrl+Win+7")]
    public void Parses_modifiers_and_key(string text, HotkeyModifiers modifiers, int virtualKey, string normalized)
    {
        HotkeyGesture.TryParse(text, out var gesture).ShouldBeTrue();

        gesture.Modifiers.ShouldBe(modifiers);
        gesture.VirtualKey.ShouldBe(virtualKey);
        gesture.ToString().ShouldBe(normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+X")]
    [InlineData("Hyper+X")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+ÄÖ")]
    public void Rejects_invalid_gestures(string? text) =>
        HotkeyGesture.TryParse(text, out _).ShouldBeFalse();
}
