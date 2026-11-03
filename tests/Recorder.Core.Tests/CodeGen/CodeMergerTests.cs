using Recorder.Core.CodeGen;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>Spec 10.5 and 13.2 (Java merge on sample files): conservative, text-based merge driven by the profile's patterns.</summary>
public sealed class CodeMergerTests
{
    private static readonly MergeSettings Java = ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java).Settings.Merge;
    private static readonly MergeSettings CSharp = ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.CSharp).Settings.Merge;

    private static readonly string GeneratedPage = """
        package tbc.automation.pages;

        import io.appium.java_client.windows.WindowsDriver;
        import org.openqa.selenium.WebElement;
        import tbc.automation.pages.BasePage;

        public class LoanPage extends BasePage {
            private final WindowsDriver winDriver;

            public LoanPage(WindowsDriver driver) {
                super(driver);
                this.winDriver = driver;
            }

            // Locators only - all public for access from Steps classes
            public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}
            public WebElement getSearchButton(){return winDriver.findElementByAccessibilityId("btnSearch");}
            public WebElement getLoanIdBox(){return winDriver.findElementByAccessibilityId("loanIdEditBox");}
        }

        """.ReplaceLineEndings("\n");

    [Fact]
    public void New_locators_go_after_the_last_one_of_a_target_style_page_and_nothing_else_changes()
    {
        var existing = File.ReadAllText(Path.Combine(RepoRoot.Find(), "docs", "target-style", "ClientSearchPage.java"));

        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.IsUnderstood.ShouldBeTrue(result.Problem.ToString());
        result.AddedMembers.ShouldBe(["getAmountInput"]);

        // The file's getSearchButton finds "searchTaskItem"; its getSearchByLoanIdTextBox finds "loanIdEditBox".
        result.NameTaken.ShouldBe(["getSearchButton"]);
        result.SameLocators.ShouldBe([new SameLocator("getLoanIdBox", "getSearchByLoanIdTextBox")]);
        result.AddedImports.ShouldBeEmpty();
        Lines(result.Text).ShouldBe(Lines(existing).Take(18)
            .Append("    public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId(\"txtAmount\");}")
            .Concat(Lines(existing).Skip(18)));
    }

    [Fact]
    public void A_getter_already_in_the_file_is_recognised_in_its_three_line_form_and_by_what_it_finds()
    {
        var existing = """
            package tbc.automation.pages;

            public class LoanPage extends BasePage {
                private final WindowsDriver driver;

                public WebElement getAmountInput() {
                    return driver.findElementByAccessibilityId( "txtAmount" );
                }
            }
            """;

        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.AddedMembers.ShouldBe(["getSearchButton", "getLoanIdBox"]);
        result.Text.ShouldContain("""
                    return driver.findElementByAccessibilityId( "txtAmount" );
                }
                public WebElement getSearchButton(){return winDriver.findElementByAccessibilityId("btnSearch");}
            """);
        result.AddedImports.ShouldBe(["import io.appium.java_client.windows.WindowsDriver;", "import org.openqa.selenium.WebElement;", "import tbc.automation.pages.BasePage;"]);
        result.Text.ShouldStartWith("package tbc.automation.pages;\n\nimport io.appium.java_client.windows.WindowsDriver;\n");
    }

    [Fact]
    public void A_name_the_file_uses_for_another_element_is_not_added_twice()
    {
        var existing = """
            public class LoanPage extends BasePage {
                public WebElement getAmountInput(){return winDriver.findElementByName("Amount");}
            }
            """;

        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.NameTaken.ShouldBe(["getAmountInput"]);
        result.AddedMembers.ShouldBe(["getSearchButton", "getLoanIdBox"]);
    }

    [Fact]
    public void New_steps_methods_go_before_the_closing_brace_and_return_the_files_class()
    {
        var existing = File.ReadAllText(Path.Combine(RepoRoot.Find(), "docs", "target-style", "ClientSearchSteps.java"));
        var generated = """
            package tbc.automation.steps;

            import io.appium.java_client.windows.WindowsDriver;
            import org.openqa.selenium.Keys;
            import tbc.automation.utils.Log;

            public class LoanSteps {
                private static final Log log = Log.getLogger(LoanSteps.class);

                public LoanSteps(WindowsDriver driver) {
                    log.info("LoanSteps initialized");
                }

                public LoanSteps searchByLoanID(String loanID) {
                    return this;
                }

                public LoanSteps pressEnterInSearch() {
                    log.info("Press Enter in Search");
                    clientSearchPage.getSearchButton().sendKeys(Keys.ENTER);
                    return this;
                }
            }
            """;

        var result = CodeMerger.Merge(existing, generated, Java);

        result.AddedMethods.ShouldBe(["pressEnterInSearch"]);
        result.AddedImports.ShouldBe(["import org.openqa.selenium.Keys;"]);
        result.Text.ShouldEndWith("""
                    return this;
                }

                public ClientSearchSteps pressEnterInSearch() {
                    log.info("Press Enter in Search");
                    clientSearchPage.getSearchButton().sendKeys(Keys.ENTER);
                    return this;
                }
            }

            """.ReplaceLineEndings("\n"));
        result.Text.ShouldContain("import io.appium.java_client.windows.WindowsDriver;\nimport tbc.automation.pages.Landing.clientCenter.clientSearch.ClientSearchPage;\nimport tbc.automation.utils.Log;\nimport org.openqa.selenium.Keys;\n");
    }

    [Fact]
    public void Line_breaks_and_tabs_of_the_file_are_kept()
    {
        var existing = "public class LoanPage extends BasePage {\r\n\tpublic WebElement getA(){return winDriver.findElementByName(\"A\");}\r\n}\r\n";

        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.Text.ShouldStartWith(
            "import io.appium.java_client.windows.WindowsDriver;\r\nimport org.openqa.selenium.WebElement;\r\nimport tbc.automation.pages.BasePage;\r\n\r\n" +
            "public class LoanPage extends BasePage {\r\n\tpublic WebElement getA(){return winDriver.findElementByName(\"A\");}\r\n" +
            "\tpublic WebElement getAmountInput(){return winDriver.findElementByAccessibilityId(\"txtAmount\");}\r\n");
        result.Text.ShouldEndWith("}\r\n");
    }

    [Fact]
    public void Braces_in_strings_comments_and_characters_do_not_confuse_it()
    {
        var existing = """
            public class LoanPage extends BasePage { // {
                /* } */ private final String brace = "}\"{";
                private final char open = '{';
                public WebElement getA(){return winDriver.findElementByName("}");}
            }
            """;

        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.Problem.ShouldBeNull();
        result.AddedMembers.Count.ShouldBe(3);
        result.Text.ShouldEndWith("""
                public WebElement getA(){return winDriver.findElementByName("}");}
                public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}
                public WebElement getSearchButton(){return winDriver.findElementByAccessibilityId("btnSearch");}
                public WebElement getLoanIdBox(){return winDriver.findElementByAccessibilityId("loanIdEditBox");}
            }
            """);
    }

    [Theory]
    [InlineData("public class A {\n}\nclass B {\n}\n", MergeProblem.SeveralClasses)]
    [InlineData("public class A {\n    void f() {\n}\n", MergeProblem.UnbalancedBraces)]
    [InlineData("interface A {\n}\n", MergeProblem.NoClass)]
    [InlineData("public class A {\n    String s = \"\"\"\n        x\"\"\";\n}\n", MergeProblem.TextBlocks)]
    [InlineData("public class A {\n    void f() {} }\n", MergeProblem.ClosingBraceNotOnItsOwnLine)]
    [InlineData("public class A { int a;\n}\n", MergeProblem.BodyNotOnItsOwnLine)]
    public void A_file_it_cannot_read_confidently_is_left_alone_and_everything_generated_is_offered_to_copy(string existing, MergeProblem problem)
    {
        var result = CodeMerger.Merge(existing, GeneratedPage, Java);

        result.Problem.ShouldBe(problem);
        result.Text.ShouldBe(existing);
        result.HasChanges.ShouldBeFalse();
        result.NewMembers.ShouldStartWith("import io.appium.java_client.windows.WindowsDriver;\n");
        result.NewMembers.ShouldContain("    public WebElement getLoanIdBox(){return winDriver.findElementByAccessibilityId(\"loanIdEditBox\");}\n");
    }

    [Fact]
    public void Merging_the_same_code_again_changes_nothing()
    {
        var result = CodeMerger.Merge(GeneratedPage, GeneratedPage, Java);

        result.HasChanges.ShouldBeFalse();
        result.Text.ShouldBe(GeneratedPage);
        result.NewMembers.ShouldBeEmpty();
    }

    [Fact]
    public void A_csharp_page_inside_a_namespace_block_gets_its_new_properties()
    {
        var existing = """
            using OpenQA.Selenium.Appium.Windows;

            namespace Recorded.Pages
            {
                public class LoanPage
                {
                    public WindowsElement AmountInput => _driver.FindElementByAccessibilityId("txtAmount");
                }
            }
            """;
        var generated = """
            using OpenQA.Selenium.Appium.Windows;

            namespace Recorded.Pages
            {
                public class LoanPage
                {
                    private readonly WindowsDriver<WindowsElement> _driver;

                    public LoanPage(WindowsDriver<WindowsElement> driver)
                    {
                        _driver = driver;
                    }

                    public WindowsElement AmountInput => _driver.FindElementByAccessibilityId("txtAmount");
                    public WindowsElement SaveButton => _driver.FindElementByAccessibilityId("btnSave");
                }
            }
            """;

        var result = CodeMerger.Merge(existing, generated, CSharp);

        result.AddedMembers.ShouldBe(["SaveButton"]);
        result.Text.ShouldContain("""
                    public WindowsElement AmountInput => _driver.FindElementByAccessibilityId("txtAmount");
                    public WindowsElement SaveButton => _driver.FindElementByAccessibilityId("btnSave");
                }
            }
            """);
    }

    [Fact]
    public void A_broken_pattern_in_the_profile_is_reported_not_thrown()
    {
        var result = CodeMerger.Merge(GeneratedPage, GeneratedPage, Java with { Member = "(" });

        result.Problem.ShouldBe(MergeProblem.InvalidPattern);
        result.ProblemDetail.ShouldNotBeNullOrEmpty();
        CodeMerger.Merge(GeneratedPage, GeneratedPage, new MergeSettings()).Problem.ShouldBe(MergeProblem.NoPatterns);
    }

    private static IEnumerable<string> Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');
}
