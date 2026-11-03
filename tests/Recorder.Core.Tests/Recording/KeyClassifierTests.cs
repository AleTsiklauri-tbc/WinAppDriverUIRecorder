using Recorder.Core.Model;
using Recorder.Core.Recording;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Recording;

/// <summary>Spec 9.4: special keys are recorded, keys that type are dropped, and nothing is ever turned into a character.</summary>
public sealed class KeyClassifierTests
{
    [Theory]
    [InlineData(0x41, HotkeyModifiers.None)]
    [InlineData(0x41, HotkeyModifiers.Shift)]
    [InlineData(0x35, HotkeyModifiers.None)]
    [InlineData(0x20, HotkeyModifiers.None)]
    [InlineData(0x08, HotkeyModifiers.None)]
    [InlineData(0x08, HotkeyModifiers.Control)]
    [InlineData(0x61, HotkeyModifiers.None)]
    [InlineData(0xBE, HotkeyModifiers.Shift)]
    [InlineData(0x45, HotkeyModifiers.Control | HotkeyModifiers.Alt)] // AltGr+E types "€" on many layouts
    [InlineData(0xE7, HotkeyModifiers.None)] // VK_PACKET: Unicode text from an input method or SendInput
    [InlineData(0xE5, HotkeyModifiers.None)] // IME processing
    public void Keys_that_type_are_typing(int virtualKey, HotkeyModifiers modifiers) =>
        KeyClassifier.Classify(virtualKey, modifiers).ShouldBe(KeyKind.Typing);

    [Theory]
    [InlineData(0x0D, HotkeyModifiers.None, "Enter")]
    [InlineData(0x09, HotkeyModifiers.Shift, "Shift+Tab")]
    [InlineData(0x1B, HotkeyModifiers.None, "Esc")]
    [InlineData(0x74, HotkeyModifiers.None, "F5")]
    [InlineData(0x7B, HotkeyModifiers.Control, "Ctrl+F12")]
    [InlineData(0x2E, HotkeyModifiers.None, "Delete")]
    [InlineData(0x28, HotkeyModifiers.None, "Down")]
    [InlineData(0x53, HotkeyModifiers.Control, "Ctrl+S")]
    [InlineData(0x53, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+S")]
    [InlineData(0x46, HotkeyModifiers.Alt, "Alt+F")]
    [InlineData(0x73, HotkeyModifiers.Alt, "Alt+F4")]
    public void Special_keys_and_chords_are_recordable_with_their_usual_names(int virtualKey, HotkeyModifiers modifiers, string name)
    {
        KeyClassifier.Classify(virtualKey, modifiers).ShouldBe(KeyKind.Recordable);
        KeyClassifier.Chord(virtualKey, modifiers).ToString().ShouldBe(name);
    }

    [Theory]
    [InlineData(0xA2, HotkeyModifiers.Control)]
    [InlineData(0x10, HotkeyModifiers.None)]
    [InlineData(0x5B, HotkeyModifiers.None)]
    public void Modifiers_alone_are_modifiers(int virtualKey, HotkeyModifiers modifiers) =>
        KeyClassifier.Classify(virtualKey, modifiers).ShouldBe(KeyKind.Modifier);

    [Theory]
    [InlineData(0x24, HotkeyModifiers.None)]
    [InlineData(0x21, HotkeyModifiers.None)]
    [InlineData(0x14, HotkeyModifiers.None)]
    [InlineData(0x52, HotkeyModifiers.Windows)]
    [InlineData(0x0D, HotkeyModifiers.Windows)]
    public void Other_keys_and_Windows_shortcuts_are_ignored(int virtualKey, HotkeyModifiers modifiers) =>
        KeyClassifier.Classify(virtualKey, modifiers).ShouldBe(KeyKind.Other);

    [Fact]
    public void Only_recordable_keys_get_a_chord()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => KeyClassifier.Chord(0x24, HotkeyModifiers.None));
    }

    [Theory]
    [InlineData(0x41, HotkeyModifiers.None, KeyKind.Typing)]
    [InlineData(0xE7, HotkeyModifiers.None, KeyKind.Typing)]
    [InlineData(0x08, HotkeyModifiers.None, KeyKind.Typing)]
    [InlineData(0x2E, HotkeyModifiers.None, KeyKind.Typing)] // Delete edits the password
    [InlineData(0x0D, HotkeyModifiers.None, KeyKind.Recordable)]
    [InlineData(0x09, HotkeyModifiers.None, KeyKind.Recordable)]
    [InlineData(0x09, HotkeyModifiers.Shift, KeyKind.Recordable)]
    [InlineData(0x1B, HotkeyModifiers.None, KeyKind.Other)]
    [InlineData(0x25, HotkeyModifiers.None, KeyKind.Other)]
    [InlineData(0x74, HotkeyModifiers.None, KeyKind.Other)]
    [InlineData(0x56, HotkeyModifiers.Control, KeyKind.Other)] // Ctrl+V would paste into the password
    [InlineData(0x10, HotkeyModifiers.None, KeyKind.Other)]
    [InlineData(0x0D, HotkeyModifiers.Windows, KeyKind.Other)]
    public void In_a_password_field_only_Enter_and_Tab_are_recorded_and_typing_is_only_noted(int virtualKey, HotkeyModifiers modifiers, KeyKind kind) =>
        KeyClassifier.InPasswordField(virtualKey, modifiers).ShouldBe(kind);

    [Theory]
    [InlineData(0x25, HotkeyModifiers.None, true)]
    [InlineData(0x27, HotkeyModifiers.Shift, true)]
    [InlineData(0x2E, HotkeyModifiers.None, true)]
    [InlineData(0x41, HotkeyModifiers.Control, true)]
    [InlineData(0x56, HotkeyModifiers.Control, true)]
    [InlineData(0x5A, HotkeyModifiers.Control, true)]
    [InlineData(0x53, HotkeyModifiers.Control, false)]
    [InlineData(0x0D, HotkeyModifiers.None, false)]
    [InlineData(0x09, HotkeyModifiers.None, false)]
    public void Editing_keys_are_told_apart(int virtualKey, HotkeyModifiers modifiers, bool editsText) =>
        KeyClassifier.EditsText(KeyClassifier.Chord(virtualKey, modifiers)).ShouldBe(editsText);
}
