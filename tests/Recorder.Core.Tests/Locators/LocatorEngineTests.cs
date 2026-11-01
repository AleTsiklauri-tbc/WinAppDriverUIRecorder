using Recorder.Core.Locators;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Locators;

public class LocatorEngineTests
{
    private static readonly UiTree Tree = TestTrees.SampleApp();

    private static LocatorReport Analyze(UiNode target, LocatorScope scope = LocatorScope.Window) =>
        LocatorEngine.Analyze(LocatorSubject.From(target, TestTrees.ProcessId, scope), Tree, target, TimeSpan.Zero);

    private static LocatorCandidate Candidate(LocatorReport report, string renderedValue) =>
        report.Candidates.Single(c => c.RenderedValue == renderedValue);

    [Fact]
    public void A_named_control_gets_its_AutomationId_as_a_unique_high_locator()
    {
        var report = Analyze(Tree.ById("txtAmount"));

        var best = report.Best.ShouldNotBeNull();
        best.Strategy.ShouldBe(LocatorStrategy.AccessibilityId);
        best.RenderedValue.ShouldBe("txtAmount");
        best.Stability.ShouldBe(Stability.High);
        best.IsUnique.ShouldBeTrue();
        best.MatchCount.ShouldBe(1);
        best.Score.ShouldBe(150);
        report.IsChecked.ShouldBeTrue();
        report.TreeSize.ShouldBe(Tree.Count);
    }

    [Fact]
    public void A_shared_label_text_is_refined_with_the_control_type()
    {
        var report = Analyze(Tree.ById("txtAmount"));

        var plain = Candidate(report, "Amount");
        plain.MatchCount.ShouldBe(2);
        plain.IsUnique.ShouldBeFalse();
        plain.Score.ShouldBe(70 - 80);

        var refined = Candidate(report, "//Edit[@Name='Amount']");
        refined.IsUnique.ShouldBeTrue();
        refined.Kind.ShouldBe(LocatorKind.Name);
        refined.Explanation.ShouldBe(new LocatorExplanation(LocatorKind.Name, Refinement.ControlType, MatchesBefore: 2));
    }

    [Fact]
    public void The_two_Save_buttons_get_distinct_unique_anchored_refinements()
    {
        var customer = Analyze(Tree.ById("btnSaveCustomer"));
        var guarantor = Analyze(Tree.ById("btnSaveGuarantor"));

        var customerAnchored = Candidate(customer, "//*[@AutomationId='pnlCustomer']//Button[@Name='Save']");
        var guarantorAnchored = Candidate(guarantor, "//*[@AutomationId='pnlGuarantor']//Button[@Name='Save']");
        customerAnchored.IsUnique.ShouldBeTrue();
        guarantorAnchored.IsUnique.ShouldBeTrue();
        customerAnchored.Explanation.ShouldBe(new LocatorExplanation(LocatorKind.Anchored, Refinement.Anchor, "pnlCustomer", MatchesBefore: 2));
        customer.Best!.RenderedValue.ShouldBe("btnSaveCustomer");
        guarantor.Best!.RenderedValue.ShouldBe("btnSaveGuarantor");

        // The steps on the way are kept, with what they matched.
        Candidate(customer, "Save").MatchCount.ShouldBe(2);
        Candidate(customer, "//Button[@Name='Save']").MatchCount.ShouldBe(2);
    }

    [Fact]
    public void An_unnamed_Georgian_button_gets_a_name_locator_with_the_UI_text_warning()
    {
        var report = Analyze(Tree.ByName("Button", "ძებნა"));

        var best = report.Best.ShouldNotBeNull();
        best.Strategy.ShouldBe(LocatorStrategy.Name);
        best.RenderedValue.ShouldBe("ძებნა");
        best.Notes.ShouldBe([LocatorNote.UiText]);
        best.IsUnique.ShouldBeTrue();
        best.Score.ShouldBe(70 + 50 - 10);
        report.Candidates.ShouldNotContain(c => c.Strategy == LocatorStrategy.AccessibilityId); // its id is its window handle
    }

    [Fact]
    public void An_unnamed_edit_named_after_its_label_is_found_by_type_and_text()
    {
        var report = Analyze(Tree.ByName("Edit", "თანხა"));

        report.Best!.RenderedValue.ShouldBe("//Edit[@Name='თანხა']");
        report.Best.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void A_clock_label_gets_a_low_name_locator_that_warns_it_looks_dynamic()
    {
        var report = Analyze(Tree.ByName("Text", "16:59:02"));

        report.Best!.Strategy.ShouldBe(LocatorStrategy.Name);
        report.Best.Stability.ShouldBe(Stability.Low);
        report.Best.Notes.ShouldContain(LocatorNote.LooksDynamic);
    }

    [Fact]
    public void A_control_with_nothing_to_go_by_falls_back_to_its_position_inside_the_anchor()
    {
        var addressControl = Tree.Find(n => n.AutomationId == "2015");

        var report = Analyze(addressControl);

        var best = report.Best.ShouldNotBeNull();
        best.RenderedValue.ShouldBe("(//*[@AutomationId='frmLoan']//Pane[starts-with(@ClassName,'WindowsForms10.Window.8')])[3]");
        best.Kind.ShouldBe(LocatorKind.Indexed);
        best.Stability.ShouldBe(Stability.Low);
        best.Notes.ShouldContain(LocatorNote.PositionBased);
        best.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void Grid_rows_are_located_by_the_Custom_tag_and_their_name() =>
        Analyze(Tree.ByName("Custom", "Row 0")).Best!.RenderedValue.ShouldBe("//Custom[@Name='Row 0']");

    [Fact]
    public void A_numeric_id_loses_to_unique_text()
    {
        var report = Analyze(Tree.ById("2"));

        report.Best!.RenderedValue.ShouldBe("OK");
        var numeric = Candidate(report, "2");
        numeric.Notes.ShouldBe([LocatorNote.NumericId]);
        numeric.Score.ShouldBe(100 + 50 - 10 - 30);
    }

    [Fact]
    public void An_id_ending_in_a_long_number_prefers_its_stable_prefix()
    {
        var report = Analyze(Tree.ById("row_48213"));

        report.Best!.RenderedValue.ShouldBe("//Edit[starts-with(@AutomationId,'row_')]");
        report.Best.Kind.ShouldBe(LocatorKind.IdPrefix);
        Candidate(report, "row_48213").Notes.ShouldBe([LocatorNote.DynamicIdSuffix]);
    }

    [Fact]
    public void A_specific_class_name_is_offered_only_when_it_is_unique()
    {
        var report = Analyze(Tree.ById("2019"));

        var byClass = report.Candidates.Single(c => c.Strategy == LocatorStrategy.ClassName);
        byClass.RenderedValue.ShouldBe("DevExpress.XtraEditors.PanelControl");
        byClass.IsUnique.ShouldBeTrue();
        report.Best.ShouldBe(byClass);
        Analyze(Tree.ById("2")).Candidates.ShouldNotContain(c => c.Strategy == LocatorStrategy.ClassName); // "Button" is a generic Win32 class
    }

    [Fact]
    public void A_title_bar_button_is_not_located_by_the_id_WinAppDriver_cannot_see()
    {
        var close = Tree.Find(n => n.RuntimeKey == "loan.close");

        var report = Analyze(close);

        report.Candidates.ShouldNotContain(c => c.Strategy == LocatorStrategy.AccessibilityId);
        report.Best!.RenderedValue.ShouldBe("//*[@AutomationId='frmLoan']//Button[@Name='Close']");
        report.Best.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void Candidates_are_sorted_by_score_and_the_best_unique_one_is_selected()
    {
        var report = Analyze(Tree.ById("txtAmount"));

        report.Candidates.Select(c => c.Score).ShouldBeInOrder(SortDirection.Descending);
        report.SelectedIndex.ShouldBe(0);
        report.Candidates.Select(c => c.Key).ShouldBeUnique();
    }

    [Fact]
    public void Elements_in_a_popup_window_are_flagged_for_a_desktop_session()
    {
        var report = Analyze(Tree.ById("txtStreet"), LocatorScope.Desktop);

        report.Scope.ShouldBe(LocatorScope.Desktop);
        report.Candidates.ShouldAllBe(c => c.Scope == LocatorScope.Desktop && c.Notes.Contains(LocatorNote.PopupWindow));
    }

    [Fact]
    public void Without_a_tree_the_candidates_are_listed_but_not_checked()
    {
        var target = Tree.ById("txtAmount");

        var report = LocatorEngine.Unchecked(LocatorSubject.From(target, TestTrees.ProcessId, LocatorScope.Window), TimeSpan.FromSeconds(2));

        report.IsChecked.ShouldBeFalse();
        report.Best!.RenderedValue.ShouldBe("txtAmount");
        report.Candidates.ShouldAllBe(c => c.MatchCount == -1 && !c.IsUnique && !c.IsChecked);
        report.Best.Score.ShouldBe(100);
    }

    [Theory]
    [InlineData(LocatorKind.Id, Stability.High, 0, true, true, 150)]
    [InlineData(LocatorKind.Name, Stability.Medium, 1, true, true, 110)]
    [InlineData(LocatorKind.Name, Stability.Medium, 0, true, false, -10)]
    [InlineData(LocatorKind.Anchored, Stability.Medium, 0, true, true, 95)]
    [InlineData(LocatorKind.Indexed, Stability.Low, 1, true, true, 30)]
    [InlineData(LocatorKind.ClassName, Stability.Medium, 0, false, false, 50)]
    public void Scores_follow_spec_7_5(LocatorKind kind, Stability stability, int warnings, bool isChecked, bool isUnique, int expected) =>
        LocatorEngine.Score(kind, stability, warnings, isChecked, isUnique).ShouldBe(expected);

    [Fact]
    public void The_absolute_path_runs_from_the_root_and_counts_alike_siblings()
    {
        XPathRenderer.Render(AbsolutePath.Build(Tree.ById("rbFixed"), TestTrees.ProcessId)).ShouldBe(
            "/Window[@AutomationId='frmMain']/Pane/Window[@AutomationId='frmLoan']/Group[@AutomationId='grpRateType']/RadioButton[@AutomationId='rbFixed']");
        XPathRenderer.Render(AbsolutePath.Build(Tree.Find(n => n.AutomationId == "2015"), TestTrees.ProcessId)).ShouldBe(
            "/Window[@AutomationId='frmMain']/Pane/Window[@AutomationId='frmLoan']/Pane[3]");
    }
}
