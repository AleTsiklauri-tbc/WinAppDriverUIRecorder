using Recorder.Core.Locators;
using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Model;

public class ElementCatalogTests
{
    [Fact]
    public void Elements_are_grouped_by_their_nearest_form()
    {
        var catalog = new ElementCatalog();

        catalog.Add(TestSnapshots.Edit("txtAmount", handle: 3001));
        catalog.Add(TestSnapshots.Edit("txtComment", handle: 3002, ancestors: [TestSnapshots.Ancestor("Window", "ConfirmDialog", "Confirm", 5001), TestSnapshots.MainForm]));
        catalog.Add(TestSnapshots.Edit("cmbCurrency", handle: 3003));

        catalog.Pages.Select(p => p.Key.DisplayName).ShouldBe(["frmLoan", "ConfirmDialog"]);
        catalog.Pages[0].Elements.Select(e => e.Label).ShouldBe(["Edit · txtAmount", "Edit · cmbCurrency"]);
    }

    [Fact]
    public void Capturing_the_same_element_twice_keeps_one_entry()
    {
        var catalog = new ElementCatalog();
        var added = new List<PageElement>();
        catalog.ElementAdded += (_, element) => added.Add(element);

        var first = catalog.Add(TestSnapshots.Edit(runtimeId: [42, 7]));
        var second = catalog.Add(TestSnapshots.Edit(runtimeId: [42, 7]));

        first.IsNew.ShouldBeTrue();
        second.IsNew.ShouldBeFalse();
        second.Element.ShouldBeSameAs(first.Element);
        catalog.Count.ShouldBe(1);
        added.Count.ShouldBe(1);
        catalog.Contains("42.7").ShouldBeTrue();
    }

    [Fact]
    public void A_window_without_a_usable_id_is_named_by_its_title_without_the_MDI_suffix()
    {
        var mainWithoutId = TestSnapshots.Ancestor("Window", "1001", "Loan Management System - [ქორიძე (7693577)]", handle: 1001);

        var key = PageKey.For(TestSnapshots.Edit(ancestors: [TestSnapshots.MdiClient, mainWithoutId]));

        key.DisplayName.ShouldBe("Loan Management System");
        key.Id.ShouldBe("name:Loan Management System");
    }

    [Fact]
    public void A_captured_window_is_its_own_page() =>
        PageKey.For(TestSnapshots.Edit("ConfirmDialog", "Confirm", controlType: "Window", ancestors: [TestSnapshots.MainForm]))
            .ShouldBe(new PageKey("id:ConfirmDialog", "ConfirmDialog"));

    [Fact]
    public void A_MessageBox_without_an_id_is_named_by_its_caption() =>
        PageKey.For(TestSnapshots.Edit("2", "OK", controlType: "Button", ancestors: [TestSnapshots.Ancestor("Window", null, "Information", 6001), TestSnapshots.MainForm]))
            .DisplayName.ShouldBe("Information");

    [Fact]
    public void Each_element_gets_a_variable_name_unique_within_its_page()
    {
        var catalog = new ElementCatalog();

        var amount = catalog.Add(TestSnapshots.Edit("txtAmount", handle: 3001)).Element;
        var save = catalog.Add(TestSnapshots.Edit("3002", "Save", handle: 3002, controlType: "Button")).Element;
        var otherSave = catalog.Add(TestSnapshots.Edit("3003", "Save", handle: 3003, controlType: "Button")).Element;
        var dialogSave = catalog.Add(TestSnapshots.Edit("3004", "Save", handle: 3004, controlType: "Button",
            ancestors: [TestSnapshots.Ancestor("Window", "ConfirmDialog", "Confirm", 5001), TestSnapshots.MainForm])).Element;

        amount.VariableName.ShouldBe("amountInput");
        save.VariableName.ShouldBe("saveButton");
        otherSave.VariableName.ShouldBe("saveButton2");
        dialogSave.VariableName.ShouldBe("saveButton"); // another page
    }

    [Fact]
    public void The_best_locator_is_selected_and_a_users_choice_survives_a_new_check()
    {
        var catalog = new ElementCatalog();
        var element = catalog.Add(TestSnapshots.Edit("txtAmount")).Element;
        var changes = 0;
        element.Changed += (_, _) => changes++;

        catalog.AttachLocators(element, Report(Id("txtAmount"), Name("Amount")));
        element.Selected!.RenderedValue.ShouldBe("txtAmount");

        catalog.ChooseLocator(element, 1);
        catalog.AttachLocators(element, Report(Id("txtAmount"), Name("Amount")));
        element.Selected!.RenderedValue.ShouldBe("Amount");
        element.UserChoseLocator.ShouldBeTrue();

        catalog.AttachLocators(element, Report(Id("txtAmount")));
        element.Selected!.RenderedValue.ShouldBe("txtAmount"); // the chosen one is no longer offered
        element.UserChoseLocator.ShouldBeFalse();
        changes.ShouldBe(4);
    }

    [Fact]
    public void A_control_captured_again_after_a_restart_is_merged_by_its_locator()
    {
        var catalog = new ElementCatalog();
        var removed = new List<PageElement>();
        catalog.ElementRemoved += (_, element) => removed.Add(element);
        var before = catalog.Add(TestSnapshots.Edit("txtAmount", runtimeId: [42, 1])).Element;
        catalog.AttachLocators(before, Report(Id("txtAmount")));
        var afterRestart = TestSnapshots.Edit("txtAmount", runtimeId: [42, 99]);
        var again = catalog.Add(afterRestart).Element;

        var survivor = catalog.AttachLocators(again, Report(Id("txtAmount")));

        survivor.ShouldBeSameAs(before);
        survivor.Snapshot.ShouldBeSameAs(afterRestart);
        removed.ShouldBe([again]);
        catalog.Count.ShouldBe(1);
        catalog.Find("42.99").ShouldBeSameAs(before);
    }

    [Fact]
    public void Renaming_cleans_the_name_and_keeps_it_unique()
    {
        var catalog = new ElementCatalog();
        var amount = catalog.Add(TestSnapshots.Edit("txtAmount", handle: 3001)).Element;
        var other = catalog.Add(TestSnapshots.Edit("txtOther", handle: 3002)).Element;

        catalog.Rename(other, "amount Input!").ShouldBeTrue();
        catalog.Rename(amount, "1 2 3").ShouldBeFalse();

        other.VariableName.ShouldBe("amountInput2");
        other.UserRenamed.ShouldBeTrue();
        amount.VariableName.ShouldBe("amountInput");
        amount.UserRenamed.ShouldBeFalse();
    }

    private static LocatorCandidate Id(string id) =>
        new(LocatorStrategy.AccessibilityId, LocatorSpec.ForAttribute(AttributeName.AutomationId, id), id, LocatorScope.Window, Stability.High, 150, 1, 1, true, [],
            new LocatorExplanation(LocatorKind.Id));

    private static LocatorCandidate Name(string name) =>
        new(LocatorStrategy.Name, LocatorSpec.ForAttribute(AttributeName.Name, name), name, LocatorScope.Window, Stability.Medium, 120, 1, 1, true, [],
            new LocatorExplanation(LocatorKind.Name));

    private static LocatorReport Report(params LocatorCandidate[] candidates) =>
        new(candidates, 0, IsChecked: true, LocatorScope.Window, TreeSize: 10, TimeSpan.FromMilliseconds(5));
}
