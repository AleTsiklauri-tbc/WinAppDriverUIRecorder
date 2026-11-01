using Recorder.Core.Locators;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Locators;

public class XPathRendererTests
{
    [Theory]
    [InlineData("Amount", "'Amount'")]
    [InlineData("თანხა", "'თანხა'")]
    [InlineData("It's", "\"It's\"")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("It's \"x\"", "concat('It',\"'\",'s \"x\"')")]
    [InlineData("'start \"x\"", "concat(\"'\",'start \"x\"')")]
    [InlineData("end \"x\"'", "concat('end \"x\"',\"'\")")]
    [InlineData("'only'", "\"'only'\"")]
    [InlineData("a''b \"", "concat('a',\"'\",\"'\",'b \"')")]
    [InlineData("", "''")]
    public void Literals_use_single_quotes_then_double_quotes_then_concat(string value, string expected) =>
        XPathLiteral.Quote(value).ShouldBe(expected);

    [Fact]
    public void Anchored_descendant_locator_renders_with_double_slashes()
    {
        var spec = new LocatorSpec(
        [
            new LocatorStep(Axis.Descendant, null, [new AttrEquals(AttributeName.AutomationId, "pnlSearch")]),
            new LocatorStep(Axis.Descendant, "Edit", [new AttrEquals(AttributeName.Name, "Amount")]),
        ]);

        XPathRenderer.Render(spec).ShouldBe("//*[@AutomationId='pnlSearch']//Edit[@Name='Amount']");
    }

    [Fact]
    public void Starts_with_contains_and_index_render_as_XPath_functions_and_positions()
    {
        var spec = new LocatorSpec(
        [
            new LocatorStep(Axis.Descendant, "Window", [new AttrEquals(AttributeName.AutomationId, "frmLoan")]),
            new LocatorStep(Axis.Descendant, "Edit", [new AttrStartsWith(AttributeName.ClassName, "WindowsForms10.EDIT")], Index: 2),
            new LocatorStep(Axis.Child, "Text", [new AttrContains(AttributeName.Name, "Row 1,")]),
        ]);

        XPathRenderer.Render(spec).ShouldBe(
            "//Window[@AutomationId='frmLoan']//Edit[starts-with(@ClassName,'WindowsForms10.EDIT')][2]/Text[contains(@Name,'Row 1,')]");
    }

    [Fact]
    public void A_step_without_a_control_type_renders_as_star() =>
        XPathRenderer.Render(new LocatorSpec([new LocatorStep(Axis.Child, null, [])])).ShouldBe("/*");

    [Fact]
    public void An_element_without_a_control_type_renders_as_Custom_like_WinAppDriver() =>
        XPathRenderer.Render(new LocatorSpec([new LocatorStep(Axis.Descendant, "Unknown", [new AttrEquals(AttributeName.Name, "Row 0")])]))
            .ShouldBe("//Custom[@Name='Row 0']");

    [Fact]
    public void A_result_index_wraps_the_whole_path_so_it_counts_in_document_order() =>
        XPathRenderer.Render(new LocatorSpec(
            [
                new LocatorStep(Axis.Descendant, "Window", [new AttrEquals(AttributeName.AutomationId, "frmLoan")]),
                new LocatorStep(Axis.Descendant, "Edit", [new AttrStartsWith(AttributeName.ClassName, "WindowsForms10.EDIT")]),
            ],
            ResultIndex: 3))
            .ShouldBe("(//Window[@AutomationId='frmLoan']//Edit[starts-with(@ClassName,'WindowsForms10.EDIT')])[3]");

    [Fact]
    public void A_result_index_must_be_one_based() =>
        Should.Throw<ArgumentOutOfRangeException>(() => XPathRenderer.Render(LocatorSpec.ForAttribute(AttributeName.Name, "x") with { ResultIndex = 0 }));

    [Fact]
    public void An_empty_locator_is_rejected() =>
        Should.Throw<ArgumentException>(() => XPathRenderer.Render(new LocatorSpec([])));

    [Fact]
    public void Index_must_be_one_based() =>
        Should.Throw<ArgumentOutOfRangeException>(() => XPathRenderer.Render(new LocatorSpec([new LocatorStep(Axis.Child, "Pane", [], Index: 0)])));
}
