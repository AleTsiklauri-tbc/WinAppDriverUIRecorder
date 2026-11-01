using Recorder.Core.Model;
using Recorder.Core.Naming;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Naming;

public class TransliterationTests
{
    [Theory]
    [InlineData("ძებნა", "dzebna")]
    [InlineData("თანხა", "tankha")]
    [InlineData("შეწყვეტა", "shetsqveta")]
    [InlineData("ჟღერადობა ჭურჭელი", "zhgheradoba churcheli")]
    [InlineData("ᲫᲔᲑᲜᲐ", "dzebna")] // Mtavruli capitals
    [InlineData("Поиск", "Poisk")]
    [InlineData("Сохранить счёт", "Sokhranit schyot")]
    [InlineData("Save", "Save")]
    public void Georgian_and_Russian_become_Latin_and_everything_else_stays(string text, string latin) =>
        Transliteration.ToLatin(text, Transliteration.BuiltIn).ShouldBe(latin);
}

public class VariableNamerTests
{
    private static string Name(string? automationId, string? name, string controlType, nint handle = 3001, string? label = null, params string[] taken) =>
        VariableNamer.Suggest(TestSnapshots.Edit(automationId, name, handle, controlType: controlType) with { LabelText = label }, taken);

    [Theory]
    [InlineData("txtAmount", "Amount", "Edit", "amountInput")]
    [InlineData("clientNoEditBox", "Client No", "Edit", "clientNoEditBox")]
    [InlineData("textBox12", "", "Edit", "textBox12")]
    [InlineData("btnSaveCustomer", "Save", "Button", "saveCustomerButton")]
    [InlineData("searchTaskItem", "Search", "Button", "searchTaskItemButton")]
    [InlineData("chkInsured", "Insured", "CheckBox", "insuredCheckbox")]
    [InlineData("rbFixed", "Fixed", "RadioButton", "fixedRadio")]
    [InlineData("cmbCurrency", "Currency", "ComboBox", "currencyDropdown")]
    [InlineData("dgvLoans", "DataGridView", "Table", "loansGrid")]
    [InlineData("lblAmount", "Amount", "Text", "amountLabel")]
    [InlineData("frmLoan", "Loan", "Window", "loanWindow")]
    public void Names_come_from_the_AutomationId_without_its_type_prefix(string id, string name, string controlType, string expected) =>
        Name(id, name, controlType).ShouldBe(expected);

    [Fact]
    public void An_unnamed_Georgian_button_is_named_after_its_transliterated_text() =>
        Name("3001", "ძებნა", "Button", handle: 3001).ShouldBe("dzebnaButton");

    [Fact]
    public void The_label_comes_before_the_controls_own_name() =>
        Name("3001", "Amount", "Edit", handle: 3001, label: "Street").ShouldBe("streetInput");

    [Fact]
    public void An_input_named_after_its_Georgian_label_is_transliterated() =>
        Name("3001", "თანხა", "Edit", handle: 3001).ShouldBe("tankhaInput");

    [Fact]
    public void A_numeric_id_is_skipped_in_favour_of_the_text() =>
        Name("2", "OK", "Button").ShouldBe("okButton");

    [Fact]
    public void Dynamic_text_is_not_a_name_and_the_control_type_numbers_it() =>
        Name("3001", "16:59:02", "Text", handle: 3001, label: null, "label1").ShouldBe("label2");

    [Fact]
    public void Names_are_unique_within_the_page() =>
        Name("btnSave", "Save", "Button", 3001, null, "saveButton", "SAVEBUTTON2").ShouldBe("saveButton3");

    [Fact]
    public void Keywords_are_avoided() =>
        Name("new", null, "Thumb").ShouldBe("newElement");

    [Fact]
    public void The_getter_capitalises_the_variable() =>
        VariableNamer.Getter("dzebnaButton").ShouldBe("getDzebnaButton");

    [Theory]
    [InlineData("amount Input!", "amountInput")]
    [InlineData("  save_button ", "save_button")]
    [InlineData("1abc", null)]
    [InlineData("ძებნა", null)]
    public void Typed_names_are_reduced_to_valid_identifiers(string typed, string? expected) =>
        VariableNamer.Clean(typed).ShouldBe(expected);
}

public class LabelFinderTests
{
    private static readonly PixelRect Input = new(120, 12, 160, 20);

    private static LabelCandidate Text(string name, int x, int y, int width = 60, int height = 15) => new("Text", name, new PixelRect(x, y, width, height), false);

    [Fact]
    public void The_nearest_text_left_of_the_input_on_the_same_row_is_its_label() =>
        LabelFinder.Find(Input, [Text("Far", 0, 14), Text("Amount", 50, 15), Text("Below", 120, 60)], scale: 1).ShouldBe("Amount");

    [Fact]
    public void A_text_directly_above_counts_too() =>
        LabelFinder.Find(Input, [Text("Amount", 120, -10)], scale: 1).ShouldBe("Amount");

    [Fact]
    public void Text_on_another_row_or_too_far_away_is_ignored()
    {
        LabelFinder.Find(Input, [Text("Other row", 50, 60)], scale: 1).ShouldBeNull();
        LabelFinder.Find(new PixelRect(400, 12, 160, 20), [Text("Too far", 0, 14, width: 100)], scale: 1).ShouldBeNull();
    }

    [Fact]
    public void The_reach_grows_with_the_display_scale() =>
        LabelFinder.Find(new PixelRect(400, 12, 160, 20), [Text("Amount", 0, 14, width: 100)], scale: 1.5).ShouldBe("Amount");

    [Fact]
    public void Only_visible_text_controls_are_labels() =>
        LabelFinder.Find(Input, [new LabelCandidate("Button", "Amount", new PixelRect(50, 15, 60, 15), false), Text("", 50, 15)], scale: 1).ShouldBeNull();
}
