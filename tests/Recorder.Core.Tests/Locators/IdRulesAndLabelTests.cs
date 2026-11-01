using Recorder.Core.Locators;
using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Locators;

public class IdRulesAndLabelTests
{
    [Theory]
    [InlineData("525548", 525548, true)]
    [InlineData("525548", 999, false)]
    [InlineData("txtAmount", 525548, false)]
    [InlineData("", 525548, false)]
    [InlineData("0", 0, false)]
    public void An_AutomationId_equal_to_the_window_handle_is_detected(string automationId, long handle, bool expected) =>
        IdRules.IsWindowHandle(automationId, (nint)handle).ShouldBe(expected);

    [Theory]
    [InlineData("txtAmount", 3001, true)]
    [InlineData("3001", 3001, false)]
    [InlineData("  ", 3001, false)]
    [InlineData(null, 3001, false)]
    public void Usable_ids_are_present_and_not_handles(string? automationId, long handle, bool expected) =>
        IdRules.IsUsable(automationId, (nint)handle).ShouldBe(expected);

    [Fact]
    public void Label_prefers_the_AutomationId() =>
        ElementLabel.For(TestSnapshots.Edit()).ShouldBe("Edit · txtAmount");

    [Fact]
    public void Label_falls_back_to_the_quoted_name_when_the_id_is_a_window_handle() =>
        ElementLabel.For(TestSnapshots.Edit(automationId: "724508", name: "ძებნა", handle: 724508, controlType: "Button"))
            .ShouldBe("Button · “ძებნა”");

    [Fact]
    public void Label_is_the_control_type_alone_when_nothing_identifies_the_element() =>
        ElementLabel.For(TestSnapshots.Edit(automationId: null, name: " ", controlType: "Pane")).ShouldBe("Pane");

    [Fact]
    public void Long_or_multi_line_names_are_shortened_to_one_line()
    {
        var label = ElementLabel.For(TestSnapshots.Edit(automationId: null, name: "first line\nsecond line that is quite long and goes on and on"));

        label.ShouldStartWith("Edit · “first line second line");
        label.ShouldEndWith("…”");
        label.ShouldNotContain("\n");
    }
}
