using Recorder.Core.CodeGen;
using Recorder.Core.Locators;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

public class JavaLocatorLineTests
{
    private static LocatorCandidate Candidate(LocatorStrategy strategy, string value) =>
        new(strategy, LocatorSpec.ForAttribute(AttributeName.Name, value), value, LocatorScope.Window, Stability.High, 150, 1, 1, true, [],
            new LocatorExplanation(LocatorKind.Id));

    [Fact]
    public void The_line_matches_the_page_style_of_the_target_project() =>
        JavaLocatorLine.Getter("searchByClientNumberTextBox", Candidate(LocatorStrategy.AccessibilityId, "clientNoEditBox")).ShouldBe(
            "public WebElement getSearchByClientNumberTextBox(){return winDriver.findElementByAccessibilityId(\"clientNoEditBox\");}");

    [Theory]
    [InlineData(LocatorStrategy.Name, "ძებნა", "public WebElement getDzebnaButton(){return winDriver.findElementByName(\"ძებნა\");}")]
    [InlineData(LocatorStrategy.ClassName, "DevExpress.XtraEditors.PanelControl", "public WebElement getDzebnaButton(){return winDriver.findElementByClassName(\"DevExpress.XtraEditors.PanelControl\");}")]
    [InlineData(LocatorStrategy.XPath, "//*[@AutomationId='pnlCustomer']//Button[@Name='Save']", "public WebElement getDzebnaButton(){return winDriver.findElementByXPath(\"//*[@AutomationId='pnlCustomer']//Button[@Name='Save']\");}")]
    public void Each_strategy_uses_its_java_client_7_method(LocatorStrategy strategy, string value, string line) =>
        JavaLocatorLine.Getter("dzebnaButton", Candidate(strategy, value)).ShouldBe(line);

    [Theory]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\LMS", "\"C:\\\\LMS\"")]
    [InlineData("a\nb\tc", "\"a\\nb\\tc\"")]
    [InlineData("\u0001", "\"\\u0001\"")]
    [InlineData("თანხა €", "\"თანხა €\"")]
    public void Java_strings_escape_only_what_Java_needs(string value, string literal) =>
        JavaString.Literal(value).ShouldBe(literal);

    [Fact]
    public void An_XPath_with_both_quote_kinds_survives_as_a_Java_string() =>
        JavaString.Literal("//Text[@Name=concat('It',\"'\",'s \"x\"')]").ShouldBe("\"//Text[@Name=concat('It',\\\"'\\\",'s \\\"x\\\"')]\"");
}
