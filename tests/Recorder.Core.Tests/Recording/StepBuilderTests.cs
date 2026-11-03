using Recorder.Core.Model;
using Recorder.Core.Recording;
using Shouldly;
using Xunit;
using static Recorder.Core.Tests.Recording.Elements;

namespace Recorder.Core.Tests.Recording;

/// <summary>Spec 9.5: every StepBuilder rule, with synthetic event streams.</summary>
public sealed class StepBuilderTests
{
    private readonly StepBuilder _builder = new(MainWindow, TimeSpan.FromMilliseconds(500), slop: 8, maskTypedValues: false);
    private readonly DraftList _list = new();

    [Fact]
    public void The_phase_3_acceptance_flow_becomes_exactly_its_eight_steps()
    {
        Apply(_builder.Click(Click(Amount, time: 1000)));
        Apply(_builder.Value(Value(Amount, string.Empty, "5000")));
        Apply(_builder.Click(Click(Password, time: 3000)));
        Apply(_builder.Value(Value(Password, null, null, secret: true)));
        Apply(_builder.Click(Click(Currency, time: 5000)));
        Apply(_builder.Click(Click(Gel, Currency, time: 6000)));
        Apply(_builder.Value(Value(Currency, string.Empty, "GEL")));
        var check = _builder.Click(Click(Insured, time: 7000));
        Apply(check);
        Apply(_builder.CompleteState(check[^1].Step, state: true));
        Apply(_builder.Click(Click(FileItem, time: 8000)));
        Apply(_builder.Click(Click(ExportItem, FileItem, time: 9000)));
        Apply(_builder.Click(Click(PdfItem, FileItem, time: 10000)));
        Apply(_builder.Click(Click(ShowMessage, time: 11000)));
        Apply(_builder.Click(Click(MessageOk, time: 12000)));
        Apply(_builder.Click(Click(StatusCell, Grid, time: 13000)));

        _list.Kinds.ShouldBe([
            StepKind.SetText, StepKind.SetText, StepKind.SelectOption, StepKind.SetChecked,
            StepKind.MenuPath, StepKind.Click, StepKind.Click, StepKind.GridCellClick]);
        var steps = _list.Steps;
        (steps[0].Target, steps[0].Text, steps[0].IsSecret).ShouldBe((Amount, "5000", false));
        (steps[1].Target, steps[1].Text, steps[1].IsSecret).ShouldBe((Password, null, true));
        (steps[2].Target, steps[2].Text).ShouldBe((Currency, "GEL"));
        (steps[3].Target, steps[3].State).ShouldBe((Insured, true));
        steps[4].MenuItems.ShouldBe(["File", "Export", "PDF"]);
        steps[4].Target.ShouldBe(FileItem);
        steps[5].Target.ShouldBe(ShowMessage);
        steps[6].Target.ShouldBe(MessageOk);
        (steps[7].Target, steps[7].RowIndex, steps[7].ColumnIndex, steps[7].ColumnHeader).ShouldBe((Grid, 2, 3, "Status"));
    }

    [Fact]
    public void A_click_that_only_put_the_caret_into_an_input_gives_way_to_its_value()
    {
        Apply(_builder.Click(Click(Amount)));
        Apply(_builder.Value(Value(Amount, string.Empty, "5000")));

        _list.Kinds.ShouldBe([StepKind.SetText]);
    }

    [Fact]
    public void A_click_on_an_input_is_kept_when_nothing_is_typed()
    {
        Apply(_builder.Click(Click(Amount, time: 1000)));
        Apply(_builder.Value(Value(Amount, "5000", "5000")));
        Apply(_builder.Click(Click(ShowMessage, time: 3000)));

        _list.Kinds.ShouldBe([StepKind.Click, StepKind.Click]);
    }

    [Fact]
    public void An_emptied_input_becomes_ClearText_and_masking_turns_typed_text_into_a_test_data_key()
    {
        Apply(_builder.Value(Value(Amount, "5000", string.Empty)));
        var masked = new StepBuilder(MainWindow, TimeSpan.FromMilliseconds(500), 8, maskTypedValues: true);
        var change = masked.Value(Value(Amount, string.Empty, "5000")).Single();

        _list.Kinds.ShouldBe([StepKind.ClearText]);
        (change.Step.Kind, change.Step.Text, change.Step.IsSecret).ShouldBe((StepKind.SetText, null, true));
    }

    [Fact]
    public void A_second_click_on_the_same_element_within_the_double_click_time_becomes_a_double_click()
    {
        Apply(_builder.Click(Click(ShowMessage, time: 1000)));
        Apply(_builder.Click(Click(ShowMessage, time: 1400)));

        _list.Kinds.ShouldBe([StepKind.DoubleClick]);
    }

    [Theory]
    [InlineData(1600u, 0)]
    [InlineData(1200u, 20)]
    public void Clicks_too_slow_or_too_far_apart_stay_two_clicks(uint secondTime, int offset)
    {
        Apply(_builder.Click(Click(ShowMessage, time: 1000)));
        var far = Click(ShowMessage, time: secondTime) with { Point = new PixelPoint(ShowMessage.Bounds.Center.X + offset, ShowMessage.Bounds.Center.Y) };
        Apply(_builder.Click(far));

        _list.Kinds.ShouldBe([StepKind.Click, StepKind.Click]);
    }

    [Fact]
    public void Clicks_on_two_different_elements_are_never_a_double_click()
    {
        Apply(_builder.Click(Click(ShowMessage, time: 1000)));
        Apply(_builder.Click(Click(SaveCustomer, time: 1100)));

        _list.Kinds.ShouldBe([StepKind.Click, StepKind.Click]);
    }

    [Fact]
    public void A_double_click_on_a_grid_cell_keeps_the_cell()
    {
        Apply(_builder.Click(Click(StatusCell, Grid, time: 1000)));
        Apply(_builder.Click(Click(StatusCell, Grid, time: 1200)));

        var step = _list.Steps.Single();
        (step.Kind, step.Target, step.RowIndex, step.ColumnHeader).ShouldBe((StepKind.DoubleClick, Grid, 2, "Status"));
    }

    [Fact]
    public void The_right_button_records_a_right_click_and_on_a_cell_keeps_its_row_and_column()
    {
        Apply(_builder.Click(Click(ShowMessage, button: PointerButton.Right)));
        Apply(_builder.Click(Click(StatusCell, Grid, button: PointerButton.Right)));

        _list.Kinds.ShouldBe([StepKind.RightClick, StepKind.RightClick]);
        (_list.Steps[1].Target, _list.Steps[1].RowIndex, _list.Steps[1].ColumnIndex).ShouldBe((Grid, 2, 3));
    }

    [Fact]
    public void Check_boxes_and_radio_buttons_become_SetChecked_once_their_state_is_read()
    {
        var radio = Make("RadioButton", "rbFixed", "Fixed", 3050, [Loan, Main], ["SelectionItem"]);
        var check = _builder.Click(Click(Insured, time: 1000));
        Apply(check);
        check.Single().Step.State.ShouldBeNull();
        Apply(_builder.CompleteState(check.Single().Step, state: true));
        var choose = _builder.Click(Click(radio, time: 3000));
        Apply(choose);
        Apply(_builder.CompleteState(choose.Single().Step, state: null));

        _list.Kinds.ShouldBe([StepKind.SetChecked, StepKind.SetChecked]);
        _list.Steps[0].State.ShouldBe(true);
        _list.Steps[1].Warnings.ShouldBe([StepWarning.StateUnavailable]);
    }

    [Fact]
    public void A_tab_item_becomes_SelectTab_with_its_text()
    {
        Apply(_builder.Click(Click(GeneralTab)));

        (_list.Steps.Single().Kind, _list.Steps.Single().Text, _list.Steps.Single().Target).ShouldBe((StepKind.SelectTab, "General", GeneralTab));
    }

    [Fact]
    public void A_drop_down_item_replaces_the_click_that_opened_its_combo_box()
    {
        Apply(_builder.Click(Click(Currency, time: 1000)));
        Apply(_builder.Click(Click(Gel, Currency, time: 3000)));
        Apply(_builder.Value(Value(Currency, string.Empty, "GEL")));

        var step = _list.Steps.Single();
        (step.Kind, step.Target, step.Text).ShouldBe((StepKind.SelectOption, Currency, "GEL"));
    }

    [Fact]
    public void A_selection_made_with_the_keyboard_is_recorded_on_focus_out()
    {
        Apply(_builder.Click(Click(Currency)));
        Apply(_builder.Keys(KeyClassifier.Chord(0x28, HotkeyModifiers.None), Currency, null, DateTimeOffset.UnixEpoch));
        Apply(_builder.Keys(KeyClassifier.Chord(0x0D, HotkeyModifiers.None), Currency, null, DateTimeOffset.UnixEpoch));
        Apply(_builder.Value(Value(Currency, string.Empty, "USD")));

        (_list.Steps.Single().Kind, _list.Steps.Single().Text).ShouldBe((StepKind.SelectOption, "USD"));
    }

    [Fact]
    public void Menu_clicks_become_one_menu_path_even_when_the_submenu_opened_by_hovering()
    {
        Apply(_builder.Click(Click(FileItem, time: 1000)));
        Apply(_builder.Click(Click(PdfItem, FileItem, time: 3000)));

        var step = _list.Steps.Single();
        (step.Kind, step.Target).ShouldBe((StepKind.MenuPath, FileItem));
        step.MenuItems.ShouldBe(["File", "Export", "PDF"]);
    }

    [Fact]
    public void A_menu_opened_and_left_for_another_menu_is_replaced_by_the_one_used()
    {
        var windowMenu = Make("MenuItem", null, "Window", 1104, [MenuBar, Main], ["ExpandCollapse"]);
        var webForm = Make("MenuItem", null, "Web form", 1105, [TestSnapshots.Ancestor("MenuItem", null, "Window", 1104), MenuBar, Main], ["Invoke"]);
        Apply(_builder.Click(Click(FileItem, time: 1000)));
        Apply(_builder.Click(Click(windowMenu, time: 3000)));
        Apply(_builder.Click(Click(webForm, windowMenu, time: 5000)));

        _list.Steps.Single().MenuItems.ShouldBe(["Window", "Web form"]);
    }

    [Fact]
    public void A_context_menu_item_after_a_right_click_is_a_menu_path_of_its_own()
    {
        var gridMenu = TestSnapshots.Ancestor("ToolBar", "gridMenu", "DropDown", 7000);
        var copy = Make("MenuItem", null, "Copy", 7001, [gridMenu, Main], ["Invoke"]);
        Apply(_builder.Click(Click(StatusCell, Grid, button: PointerButton.Right)));
        Apply(_builder.Click(Click(copy, time: 3000)));

        _list.Kinds.ShouldBe([StepKind.RightClick, StepKind.MenuPath]);
        _list.Steps[1].MenuItems.ShouldBe(["Copy"]);
    }

    [Fact]
    public void Grid_cells_and_row_headers_become_GridCellClick_on_the_grid_and_a_header_less_column_has_no_header()
    {
        Apply(_builder.Click(Click(CheckCell, Grid, time: 1000)));
        Apply(_builder.Click(Click(RowHeader, Grid, time: 3000)));

        _list.Kinds.ShouldBe([StepKind.GridCellClick, StepKind.GridCellClick]);
        (_list.Steps[0].RowIndex, _list.Steps[0].ColumnIndex, _list.Steps[0].ColumnHeader).ShouldBe((2, 0, null));
        (_list.Steps[1].RowIndex, _list.Steps[1].ColumnIndex, _list.Steps[1].ColumnHeader).ShouldBe((2, null, null));
    }

    [Fact]
    public void A_step_in_another_top_level_window_gets_a_switch_before_it_and_back()
    {
        Apply(_builder.Click(Click(PrintButton, window: ReportWindow, time: 1000)));
        Apply(_builder.Click(Click(ShowMessage, window: MainWindowElement, time: 3000)));

        _list.Kinds.ShouldBe([StepKind.SwitchWindow, StepKind.Click, StepKind.SwitchWindow, StepKind.Click]);
        _list.Steps[0].Target.ShouldBe(ReportWindow);
        _list.Steps[2].Target.ShouldBe(MainWindowElement);
    }

    [Fact]
    public void Owned_dialogs_and_message_boxes_need_no_switch()
    {
        Apply(_builder.Click(Click(ShowMessage, time: 1000)));
        Apply(_builder.Click(Click(MessageOk, time: 3000)));

        _list.Kinds.ShouldBe([StepKind.Click, StepKind.Click]);
    }

    [Fact]
    public void Keys_are_recorded_with_the_focused_element_except_editing_keys_inside_an_input()
    {
        var at = DateTimeOffset.UnixEpoch;
        Apply(_builder.Keys(KeyClassifier.Chord(0x25, HotkeyModifiers.None), Amount, null, at));
        Apply(_builder.Keys(KeyClassifier.Chord(0x56, HotkeyModifiers.Control), Amount, null, at));
        Apply(_builder.Keys(KeyClassifier.Chord(0x0D, HotkeyModifiers.None), Amount, null, at));
        Apply(_builder.Keys(KeyClassifier.Chord(0x09, HotkeyModifiers.Shift), Amount, null, at));
        Apply(_builder.Keys(KeyClassifier.Chord(0x53, HotkeyModifiers.Control), null, null, at));
        Apply(_builder.Keys(KeyClassifier.Chord(0x2E, HotkeyModifiers.None), Grid, null, at));

        _list.Steps.Select(step => step.Text).ShouldBe(["Enter", "Shift+Tab", "Ctrl+S", "Delete"]);
        _list.Steps[0].Target.ShouldBe(Amount);
        _list.Steps[2].Target.ShouldBeNull();
    }

    [Fact]
    public void A_click_whose_element_never_answered_is_kept_with_a_warning()
    {
        Apply(_builder.Click(new ClickInput(PointerButton.Left, null, null, null, new PixelPoint(5, 5), 1000, DateTimeOffset.UnixEpoch)));
        Apply(_builder.Click(Click(ShowMessage, time: 3000) with { ElementChanged = true }));

        _list.Steps[0].Target.ShouldBeNull();
        _list.Steps[0].Warnings.ShouldBe([StepWarning.ElementUnavailable]);
        _list.Steps[1].Warnings.ShouldBe([StepWarning.ElementChanged]);
    }

    [Fact]
    public void Wheel_notches_in_one_direction_are_one_scroll_comment()
    {
        Apply(_builder.Scroll(Grid, null, -1, DateTimeOffset.UnixEpoch));
        Apply(_builder.Scroll(Grid, null, -2, DateTimeOffset.UnixEpoch));
        Apply(_builder.Scroll(Grid, null, 1, DateTimeOffset.UnixEpoch));

        _list.Steps.Select(step => step.Scroll).ShouldBe([-3, 1]);
        _list.Kinds.ShouldAllBe(kind => kind == StepKind.Comment);
    }

    [Fact]
    public void Assertions_carry_their_expected_value_and_only_assertion_kinds_are_accepted()
    {
        Apply(_builder.Assert(StepKind.AssertText, ShowMessage, null, "Show message", null, DateTimeOffset.UnixEpoch));
        Apply(_builder.Assert(StepKind.AssertChecked, Insured, null, null, true, DateTimeOffset.UnixEpoch));

        (_list.Steps[0].Kind, _list.Steps[0].Text).ShouldBe((StepKind.AssertText, "Show message"));
        _list.Steps[1].State.ShouldBe(true);
        Should.Throw<ArgumentOutOfRangeException>(() => _builder.Assert(StepKind.Click, ShowMessage, null, null, null, DateTimeOffset.UnixEpoch));
    }

    private void Apply(IReadOnlyList<StepChange> changes) => _list.Apply(changes);
}
