using Recorder.Core.CodeGen;
using Recorder.Core.Model;
using Recorder.Core.Naming;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>Spec 10.2 and 10.3: class and method names, key chords and string literals in generated code.</summary>
public sealed class CodeNamesTests
{
    [Theory]
    [InlineData("id:frmLoanSearch", "frmLoanSearch", "LoanSearch")]
    [InlineData("id:frmLoan", "frmLoan", "Loan")]
    [InlineData("id:frmain", "frmain", "Frmain")]
    [InlineData("id:loanIdEditBox", "loanIdEditBox", "LoanIdEditBox")]
    [InlineData("id:ფორმა", "ფორმა", "Porma")]
    [InlineData("id:2", "2", "Window2")]
    public void A_page_is_named_after_its_forms_automation_id(string id, string displayName, string name) =>
        CodeNames.PageName(new PageKey(id, displayName), ["frm"]).ShouldBe(name);

    [Theory]
    [InlineData("name:Clients - John Example", "Clients - John Example", "Window")]
    [InlineData("class:#32770", "#32770", "Dialog")]
    [InlineData("window", "Window", "Window")]
    public void A_page_is_never_named_after_a_window_title(string id, string displayName, string name) =>
        CodeNames.PageName(new PageKey(id, displayName), ["frm"]).ShouldBe(name);

    [Theory]
    [InlineData("amountInput", "Edit", "amount")]
    [InlineData("clientNoEditBox", "Edit", "clientNo")]
    [InlineData("currencyDropdown", "ComboBox", "currency")]
    [InlineData("insuredCheckbox", "CheckBox", "insured")]
    [InlineData("input", "Edit", "input")]
    [InlineData("statusLabel", "Text", "status")]
    public void A_method_name_drops_the_control_type_suffix(string variable, string controlType, string baseName) =>
        CodeNames.BaseOf(variable, controlType, NamingOptions.Default).ShouldBe(baseName);

    [Theory]
    [InlineData("Ctrl+S", "java", "Keys.chord(Keys.CONTROL, \"s\")")]
    [InlineData("Enter", "java", "Keys.ENTER")]
    [InlineData("Shift+Tab", "java", "Keys.chord(Keys.SHIFT, Keys.TAB)")]
    [InlineData("Alt+F4", "java", "Keys.chord(Keys.ALT, Keys.F4)")]
    [InlineData("Ctrl+S", "csharp", "Keys.Control + \"s\" + Keys.Null")]
    [InlineData("Esc", "csharp", "Keys.Escape")]
    public void Key_chords_become_selenium_keys(string chord, string language, string code) =>
        KeyCode.Expression(chord, language).ShouldBe(code);

    [Theory]
    [InlineData("say \"hi\"", "csharp", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\LMS", "csharp", "\"C:\\\\LMS\"")]
    [InlineData("თანხა\n€", "csharp", "\"თანხა\\n€\"")]
    [InlineData("\u0001", "csharp", "\"\\u0001\"")]
    [InlineData("თანხა\n€", "java", "\"თანხა\\n€\"")]
    public void Literals_escape_what_the_language_needs_and_keep_non_ascii_text(string value, string language, string literal) =>
        CodeNames.Literal(value, language).ShouldBe(literal);

    [Theory]
    [InlineData("class", "java", "classValue")]
    [InlineData("string", "csharp", "stringValue")]
    [InlineData("amount", "java", "amount")]
    public void Names_never_collide_with_keywords(string name, string language, string safe) =>
        CodeNames.Safe(name, language).ShouldBe(safe);

    [Fact]
    public void Unique_names_get_a_number()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        new[] { CodeNames.Unique("Loan", taken), CodeNames.Unique("Loan", taken), CodeNames.Unique("Loan", taken) }.ShouldBe(["Loan", "Loan2", "Loan3"]);
    }

    [Fact]
    public void A_window_is_never_named_after_its_title()
    {
        var window = TestSnapshots.Edit(automationId: null, name: "Clients - John Example", controlType: "Window", ancestors: []);

        VariableNamer.Suggest(window, []).ShouldBe("window1");
    }

    [Fact]
    public void A_renamed_page_keeps_its_class_name_in_the_code_and_in_the_recording()
    {
        var recording = SampleRecording.AcceptanceFlow();
        var loan = recording.Catalog.Pages.Single(p => p.Key.Id == "id:frmLoan");

        recording.Catalog.RenamePage(loan, "loan details page!").ShouldBeTrue();
        var code = CodeGenerationSnapshotTests.Generate(ProfileIds.Java, recording, "flow");
        var saved = RecordingStore.Deserialize(RecordingStore.Serialize(
            RecordingDocument.From(recording.Catalog, new StepList(), "Sample", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)));

        loan.ClassName.ShouldBe("LoanDetailsPage");
        code.Files.Select(f => f.ClassName).ShouldContain("LoanDetailsPage");
        code.Files.Select(f => f.ClassName).ShouldContain("LoanDetailsSteps");
        saved.PageClassNames.ShouldNotBeNull()[loan.Key].ShouldBe("LoanDetailsPage");
        recording.Catalog.RenamePage(loan, "!!").ShouldBeFalse();
        recording.Catalog.RenamePage(loan, null).ShouldBeTrue();
        loan.ClassName.ShouldBeNull();
    }

    [Theory]
    [InlineData("LMSLoanPage", "LMSLoanPage")]
    [InlineData("loanPage", "LoanPage")]
    [InlineData("loan details page!", "LoanDetailsPage")]
    [InlineData("სესხი", "Seskhi")]
    [InlineData("2nd page", null)]
    [InlineData("  ", null)]
    public void A_typed_class_name_keeps_its_casing_when_it_is_already_an_identifier(string typed, string? name) =>
        VariableNamer.ClassName(typed).ShouldBe(name);
}
