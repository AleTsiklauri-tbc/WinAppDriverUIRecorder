using Recorder.Core.Locators;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Locators;

/// <summary>The evaluator must count exactly as WinAppDriver does (Findings F-20, F-21).</summary>
public class LocatorEvaluatorTests
{
    private static readonly UiTree Tree = TestTrees.SampleApp();

    private static IReadOnlyList<UiNode> Find(params LocatorStep[] steps) => LocatorEvaluator.Evaluate(new LocatorSpec(steps), Tree);

    private static LocatorStep Descendant(string? tag, params Predicate[] predicates) => new(Axis.Descendant, tag, predicates);

    private static AttrEquals Id(string value) => new(AttributeName.AutomationId, value);

    private static AttrEquals Name(string value) => new(AttributeName.Name, value);

    [Fact]
    public void A_search_includes_the_session_root_itself() =>
        Find(Descendant(null, Id("frmMain"))).ShouldBe([Tree.Root]);

    [Fact]
    public void A_child_step_from_the_document_reaches_only_the_root()
    {
        Find(new LocatorStep(Axis.Child, "Window", [])).ShouldBe([Tree.Root]);
        Find(new LocatorStep(Axis.Child, "Edit", [])).ShouldBeEmpty();
    }

    [Fact]
    public void A_step_index_counts_per_parent_but_a_result_index_counts_in_document_order()
    {
        var firstPerParent = Find(new LocatorStep(Axis.Descendant, "Edit", [], Index: 1));
        var firstOverall = LocatorEvaluator.Evaluate(new LocatorSpec([Descendant("Edit")], ResultIndex: 1), Tree);

        // //Edit[1]: the first Edit child of frmLoan, of the address control and of each grid row.
        firstPerParent.Select(e => e.AutomationId).ShouldBe(["txtAmount", "txtStreet", "77", "78"]);
        firstOverall.ShouldBe([Tree.ById("txtAmount")]);
    }

    [Fact]
    public void An_anchored_path_finds_the_button_in_that_panel_only() =>
        Find(Descendant(null, Id("pnlCustomer")), Descendant("Button", Name("Save"))).ShouldBe([Tree.ById("btnSaveCustomer")]);

    [Fact]
    public void Nested_context_nodes_do_not_count_an_element_twice()
    {
        var edits = Find(Descendant("Window"), Descendant("Edit"));

        edits.Count.ShouldBe(edits.Distinct().Count());
        edits.Select(e => e.Order).ShouldBeInOrder();
    }

    [Fact]
    public void Starts_with_and_contains_compare_ordinally()
    {
        Find(Descendant("Edit", new AttrStartsWith(AttributeName.ClassName, "WindowsForms10.EDIT"))).Count.ShouldBe(4);
        Find(Descendant(null, new AttrContains(AttributeName.Name, "Row 1,"))).ShouldBe([Tree.Find(n => n.Name == "Status Row 1, Not sorted.")]);
        Find(Descendant(null, Name("save"))).ShouldBeEmpty();
    }

    [Fact]
    public void A_child_step_with_a_predicate_and_an_index_counts_matching_siblings()
    {
        var spec = new LocatorSpec(
        [
            Descendant(null, Id("frmLoan")),
            new LocatorStep(Axis.Child, "Pane", [], Index: 3),
        ]);

        LocatorEvaluator.Evaluate(spec, Tree).ShouldBe([Tree.Find(n => n.AutomationId == "2015")]);
    }

    [Fact]
    public void Elements_without_a_control_type_are_matched_by_the_Custom_tag() =>
        Find(Descendant("Custom", Name("Row 0"))).ShouldBe([Tree.ByName("Custom", "Row 0")]);

    [Fact]
    public void An_open_menu_lists_the_same_item_twice_and_WinAppDriver_counts_both()
    {
        var tree = TestTrees.OpenMenu();
        var target = tree.ByName("MenuItem", "Export");

        var matches = LocatorEvaluator.Evaluate(LocatorSpec.ForAttribute(AttributeName.Name, "Export"), tree);

        matches.Count.ShouldBe(2);
        LocatorEngine.Distinct(matches).ShouldBe(1);
        LocatorEngine.IsUnique(matches, target).ShouldBeTrue();
    }
}
