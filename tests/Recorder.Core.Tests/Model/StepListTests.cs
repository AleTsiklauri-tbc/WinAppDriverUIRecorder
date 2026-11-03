using Recorder.Core.Model;
using Recorder.Core.Recording;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Model;

public sealed class StepListTests
{
    [Fact]
    public void Every_change_is_reported_with_where_it_happened()
    {
        var list = new StepList();
        var changes = new List<(StepListChange Change, int Index, int OldIndex)>();
        list.Changed += (_, e) => changes.Add((e.Change, e.Index, e.OldIndex));
        RecordedStep a = new() { Kind = StepKind.Click }, b = new() { Kind = StepKind.SetText, Text = "5000" }, c = new() { Kind = StepKind.SelectTab };

        list.Add(a);
        list.Add(b);
        list.Insert(1, c);
        list.Move(0, 2);
        list.Replace(0, c with { Text = "General" });
        list.RemoveAt(1);

        list.Items.Select(step => step.Kind).ShouldBe([StepKind.SelectTab, StepKind.Click]);
        list[0].Text.ShouldBe("General");
        list.IndexOf(a.Id).ShouldBe(1);
        changes.ShouldBe([
            (StepListChange.Inserted, 0, -1), (StepListChange.Inserted, 1, -1), (StepListChange.Inserted, 1, -1),
            (StepListChange.Moved, 2, 0), (StepListChange.Replaced, 0, -1), (StepListChange.Removed, 1, -1)]);
    }

    [Fact]
    public void Steps_of_a_merged_element_follow_it_and_reset_replaces_everything()
    {
        var list = new StepList();
        Guid removed = Guid.NewGuid(), kept = Guid.NewGuid();
        list.Add(new RecordedStep { Kind = StepKind.Click, Target = removed });
        list.Add(new RecordedStep { Kind = StepKind.Click, Target = Guid.NewGuid() });

        list.Retarget(removed, kept);
        list[0].Target.ShouldBe(kept);

        list.Reset([new RecordedStep { Kind = StepKind.Comment, Comment = "start" }]);
        list.Count.ShouldBe(1);
    }
}

public sealed class EditHistoryTests
{
    [Fact]
    public void Edits_undo_and_redo_in_order_and_a_new_edit_clears_redo()
    {
        var history = new EditHistory();
        var value = 0;
        var changed = 0;
        history.Changed += (_, _) => changed++;

        history.Execute(() => value = 1, () => value = 0);
        history.Execute(() => value = 2, () => value = 1);
        history.Undo();
        value.ShouldBe(1);
        history.CanRedo.ShouldBeTrue();
        history.Redo();
        value.ShouldBe(2);
        history.Undo();
        history.Execute(() => value = 5, () => value = 1);

        history.CanRedo.ShouldBeFalse();
        history.Undo();
        history.Undo();
        value.ShouldBe(0);
        history.CanUndo.ShouldBeFalse();
        changed.ShouldBe(8);
    }
}
