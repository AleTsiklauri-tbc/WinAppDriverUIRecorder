using Recorder.Core.Locators;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Locators;

public class IdRulesTests
{
    private static readonly nint OwnHandle = 919076;
    private static readonly nint ParentHandle = 123456;

    private static IdVerdict Judge(string? id, string? frameworkId = "WinForm", bool nonClient = false, int processId = 4242) =>
        IdRules.Evaluate(id, new IdFacts([OwnHandle, ParentHandle], processId, frameworkId, nonClient));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ids_are_rejected(string? id) =>
        Judge(id).Rejection.ShouldBe(IdRejection.Empty);

    [Theory]
    [InlineData("919076")]     // an unnamed WinForms control: its own handle (F-3)
    [InlineData("12780708")]   // checked against every known handle, not only the element's
    [InlineData("panel_123456")]
    [InlineData("hwnd_1E240")] // 123456 in hex
    public void Ids_containing_a_window_handle_are_rejected(string id)
    {
        var verdict = IdRules.Evaluate(id, new IdFacts([OwnHandle, ParentHandle, 12780708], 4242, "WinForm", false));

        verdict.Rejection.ShouldBe(IdRejection.WindowHandle);
    }

    [Fact]
    public void Ids_containing_the_process_id_are_rejected() =>
        Judge("form_4242_main").Rejection.ShouldBe(IdRejection.ProcessId);

    [Fact]
    public void A_short_process_id_does_not_reject_designer_names() =>
        Judge("button12", processId: 12).IsHigh.ShouldBeTrue();

    [Theory]
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    [InlineData("item_3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void Guid_like_ids_are_rejected(string id) =>
        Judge(id).Rejection.ShouldBe(IdRejection.GuidValue);

    [Fact]
    public void Ids_in_a_title_bar_are_rejected_because_WinAppDriver_sees_other_ones() =>
        Judge("Close", nonClient: true).Rejection.ShouldBe(IdRejection.NonClient);

    [Theory]
    [InlineData("2")]
    [InlineData("4247891719")]
    public void Purely_numeric_ids_are_kept_as_low_with_a_warning(string id)
    {
        var verdict = Judge(id);

        verdict.IsAccepted.ShouldBeTrue();
        verdict.Stability.ShouldBe(Stability.Low);
        verdict.Notes.ShouldBe([LocatorNote.NumericId]);
    }

    [Theory]
    [InlineData("textBox12")]
    [InlineData("button3")]
    [InlineData("comboBox1")]
    [InlineData("dataGridView1")]
    [InlineData("label1")]
    [InlineData("simpleButton2")]
    [InlineData("comboBoxEdit1")]
    public void Designer_default_names_are_high_with_a_note(string id)
    {
        var verdict = Judge(id);

        verdict.IsHigh.ShouldBeTrue();
        verdict.Notes.ShouldBe([LocatorNote.DesignerDefaultName]);
        verdict.Notes.ShouldAllBe(note => !note.IsWarning());
    }

    [Theory]
    [InlineData("row_48213", "row_")]
    [InlineData("item.000123", "item.")]
    [InlineData("grid48213", "grid")]
    public void A_long_number_at_the_end_is_dynamic_and_offers_its_prefix(string id, string prefix)
    {
        var verdict = Judge(id);

        verdict.Stability.ShouldBe(Stability.Low);
        verdict.Notes.ShouldBe([LocatorNote.DynamicIdSuffix]);
        verdict.StablePrefix.ShouldBe(prefix);
    }

    [Fact]
    public void Web_widget_counters_are_dynamic_even_when_short()
    {
        var web = Judge("datepicker-3", frameworkId: "Chrome");

        web.Notes.ShouldBe([LocatorNote.DynamicIdSuffix]);
        web.StablePrefix.ShouldBe("datepicker-");
    }

    [Fact]
    public void A_short_number_after_a_separator_is_stable_in_WinForms() =>
        Judge("column_3").IsHigh.ShouldBeTrue();

    [Theory]
    [InlineData("txtAmount")]
    [InlineData("clientNoEditBox")]
    [InlineData("address2")]
    [InlineData("frmLoan")]
    public void Ordinary_designer_names_are_high_without_notes(string id)
    {
        var verdict = Judge(id);

        verdict.IsHigh.ShouldBeTrue();
        verdict.Notes.ShouldBeEmpty();
        verdict.StablePrefix.ShouldBeNull();
    }
}

public class NameRulesTests
{
    [Theory]
    [InlineData(null, "Button")]
    [InlineData("  ", "Button")]
    [InlineData("Edit", "Edit")]
    [InlineData("Check Box", "CheckBox")]
    [InlineData("line one\nline two", "Text")]
    public void Unusable_names_are_rejected(string? name, string controlType) =>
        NameRules.Evaluate(name, controlType).IsAccepted.ShouldBeFalse();

    [Fact]
    public void Names_longer_than_80_characters_are_rejected() =>
        NameRules.Evaluate(new string('a', 81), "Text").IsAccepted.ShouldBeFalse();

    [Theory]
    [InlineData("Save")]
    [InlineData("Amount")]
    [InlineData("Row 0")]
    [InlineData("Status Row 0, Not sorted.")]
    public void Plain_text_is_medium_without_warnings(string name)
    {
        var verdict = NameRules.Evaluate(name, "Button");

        verdict.IsAccepted.ShouldBeTrue();
        verdict.Stability.ShouldBe(Stability.Medium);
        verdict.Notes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("16:59:02")]
    [InlineData("Due 29.10.2026")]
    [InlineData("1,500.00")]
    [InlineData("1 500,00")]
    [InlineData("GEL 100")]
    [InlineData("100 ₾")]
    [InlineData("ACC-0001")]
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    public void Numbers_dates_times_and_amounts_look_dynamic(string name)
    {
        var verdict = NameRules.Evaluate(name, "Text");

        verdict.Stability.ShouldBe(Stability.Low);
        verdict.Notes.ShouldContain(LocatorNote.LooksDynamic);
    }

    [Theory]
    [InlineData("ძებნა")]
    [InlineData("Поиск")]
    public void Non_Latin_text_is_kept_with_the_UI_text_warning(string name)
    {
        var verdict = NameRules.Evaluate(name, "Button");

        verdict.IsAccepted.ShouldBeTrue();
        verdict.Stability.ShouldBe(Stability.Medium);
        verdict.Notes.ShouldBe([LocatorNote.UiText]);
    }
}

public class ClassNameRulesTests
{
    [Theory]
    [InlineData("WindowsForms10.EDIT.app.0.141b42a_r8_ad1", true)]
    [InlineData("HwndWrapper[App;;8f7c]", true)]
    [InlineData("Afx:00400000:8", true)]
    [InlineData("#32770", false)]
    [InlineData("DevExpress.XtraEditors.TextEdit", false)]
    public void Generated_class_names_are_recognised(string className, bool generated) =>
        ClassNameRules.IsGenerated(className).ShouldBe(generated);

    [Fact]
    public void Web_class_lists_are_never_locators() =>
        ClassNameRules.CanBeLocator("k-input ng-pristine ng-valid", "Chrome").ShouldBeFalse();

    [Theory]
    [InlineData("Button")]
    [InlineData("#32770")]
    [InlineData("SysListView32")]
    [InlineData("Chrome_WidgetWin_1")]
    public void Generic_windows_classes_are_never_locators(string className) =>
        ClassNameRules.CanBeLocator(className, "Win32").ShouldBeFalse();

    [Fact]
    public void A_third_party_class_name_can_be_a_locator() =>
        ClassNameRules.CanBeLocator("DevExpress.XtraEditors.PanelControl", "WinForm").ShouldBeTrue();

    [Theory]
    [InlineData("WindowsForms10.EDIT.app.0.141b42a_r8_ad1", "WindowsForms10.EDIT")]
    [InlineData("WindowsForms10.Window.8.app.0.141b42a_r8_ad1", "WindowsForms10.Window.8")]
    [InlineData("WindowsForms10.SysTabControl32.app.0.2bf8098_r6_ad1", "WindowsForms10.SysTabControl32")]
    [InlineData("#32770", null)]
    public void The_stable_prefix_keeps_everything_before_the_build_specific_part(string className, string? prefix) =>
        ClassNameRules.StablePrefix(className).ShouldBe(prefix);
}
