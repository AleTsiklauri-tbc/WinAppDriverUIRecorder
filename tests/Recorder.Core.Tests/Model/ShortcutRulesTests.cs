using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Model;

public sealed class ShortcutRulesTests
{
    private static HotkeyGesture Gesture(string text) => HotkeyGesture.TryParse(text, out var gesture) ? gesture : throw new ArgumentException(text);

    private static (ShortcutProblem Problem, ShortcutId? Other) Check(ShortcutId id, string text) =>
        ShortcutRules.Check(id, Gesture(text), ShortcutRules.Defaults);

    [Theory]
    [InlineData("Ctrl+Shift+R")]
    [InlineData("Alt+F9")]
    [InlineData("Win+K")]
    [InlineData("F8")]
    [InlineData("Shift+F11")]
    [InlineData("F24")]
    [InlineData("Ctrl+Alt+Space")]
    public void Keys_that_leave_other_apps_alone_can_be_a_shortcut(string text)
    {
        Check(ShortcutId.Record, text).ShouldBe((ShortcutProblem.None, null));
    }

    [Theory]
    [InlineData("R")]
    [InlineData("Shift+R")]
    [InlineData("5")]
    [InlineData("Enter")]
    [InlineData("Shift+Tab")]
    [InlineData("Space")]
    [InlineData("Down")]
    [InlineData("Delete")]
    public void A_typing_key_needs_ctrl_alt_or_win(string text)
    {
        Check(ShortcutId.Capture, text).Problem.ShouldBe(ShortcutProblem.NeedsModifier);
    }

    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+Z")]
    [InlineData("Alt+F4")]
    [InlineData("Alt+Tab")]
    [InlineData("Ctrl+Shift+C")]
    [InlineData("Ctrl+E")]
    [InlineData("Alt+Up")]
    [InlineData("F2")]
    public void Shortcuts_everyone_relies_on_are_reserved(string text)
    {
        Check(ShortcutId.Assert, text).Problem.ShouldBe(ShortcutProblem.Reserved);
    }

    [Fact]
    public void Esc_stays_with_pick_and_assert_mode()
    {
        // Esc cancels Pick and Assert mode, so it never becomes a global shortcut on its own.
        Check(ShortcutId.Pause, "Esc").Problem.ShouldBe(ShortcutProblem.NeedsModifier);
        Check(ShortcutId.Pause, "Escape").Problem.ShouldBe(ShortcutProblem.NeedsModifier);
    }

    [Fact]
    public void Another_shortcuts_keys_are_named()
    {
        Check(ShortcutId.Record, "F10").ShouldBe((ShortcutProblem.SameAs, ShortcutId.Pause));
        Check(ShortcutId.Capture, "Ctrl+Shift+A").ShouldBe((ShortcutProblem.SameAs, ShortcutId.Assert));
    }

    [Fact]
    public void A_shortcut_may_keep_its_own_keys()
    {
        foreach (var (id, gesture) in ShortcutRules.Defaults)
        {
            ShortcutRules.Check(id, gesture, ShortcutRules.Defaults).ShouldBe((ShortcutProblem.None, null), id.ToString());
        }
    }

    [Fact]
    public void Spelling_does_not_matter()
    {
        ShortcutRules.Same(Gesture("Ctrl+Shift+x"), Gesture("control+shift+X")).ShouldBeTrue();
        ShortcutRules.Same(Gesture("F9"), Gesture("Shift+F9")).ShouldBeFalse();
        Check(ShortcutId.Record, "shift+ctrl+a").ShouldBe((ShortcutProblem.SameAs, ShortcutId.Assert));
    }
}
