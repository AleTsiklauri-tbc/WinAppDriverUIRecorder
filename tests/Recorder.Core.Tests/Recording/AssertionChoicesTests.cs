using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Recording;
using Shouldly;
using Xunit;
using static Recorder.Core.Tests.Recording.Elements;

namespace Recorder.Core.Tests.Recording;

/// <summary>Spec 9.6: the picker offers what is valid for the element, pre-filled with its current value.</summary>
public sealed class AssertionChoicesTests
{
    [Fact]
    public void An_input_offers_its_value_but_not_its_label_as_text()
    {
        var choices = AssertionChoices.For(Amount, new ElementState(true, false, false, "Amount", "5000", null));

        choices.ShouldBe([
            new AssertionChoice(StepKind.AssertValue, "5000", null),
            new AssertionChoice(StepKind.AssertEnabled, null, true),
            new AssertionChoice(StepKind.AssertVisible, null, null)]);
    }

    [Fact]
    public void A_check_box_offers_its_state_first_then_its_text()
    {
        var choices = AssertionChoices.For(Insured, new ElementState(false, false, false, "Insured", null, false));

        choices.Select(choice => choice.Kind).ShouldBe([StepKind.AssertChecked, StepKind.AssertText, StepKind.AssertEnabled, StepKind.AssertVisible]);
        choices[0].State.ShouldBe(false);
        choices[1].Text.ShouldBe("Insured");
        choices[2].State.ShouldBe(false);
    }

    [Fact]
    public void A_password_field_never_offers_a_value()
    {
        var choices = AssertionChoices.For(Password with { Value = "leaked?" }, new ElementState(true, false, true, "Password", "leaked?", null));

        choices.Select(choice => choice.Kind).ShouldBe([StepKind.AssertEnabled, StepKind.AssertVisible]);
    }

    [Fact]
    public void Without_a_state_the_snapshot_fills_in()
    {
        var choices = AssertionChoices.For(ShowMessage, null);

        choices.Select(choice => (choice.Kind, choice.Text)).ShouldBe([(StepKind.AssertText, "Show message"), (StepKind.AssertEnabled, null), (StepKind.AssertVisible, null)]);
    }
}
