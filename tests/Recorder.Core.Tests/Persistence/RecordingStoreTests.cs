using System.Text;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Persistence;

/// <summary>Spec 12: *.wfrec.json recordings are schema-versioned, human-readable and hold no customer data they do not need.</summary>
public sealed class RecordingStoreTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_saved_recording_opens_with_the_same_elements_locators_and_steps()
    {
        var document = Sample();
        var path = Path.Combine(_folder, "flow" + RecordingStore.Extension);

        RecordingStore.Save(path, document);
        var loaded = RecordingStore.Load(path);

        loaded.TargetProcessName.ShouldBe("Recorder.SampleApp.exe");
        (loaded.CreatedAt, loaded.ModifiedAt).ShouldBe((document.CreatedAt, document.ModifiedAt));
        var element = loaded.Elements.Single();
        (element.Id, element.Page, element.VariableName, element.UserRenamed).ShouldBe((document.Elements[0].Id, document.Elements[0].Page, "amountInput", true));
        (element.Snapshot.ControlType, element.Snapshot.AutomationId, element.Snapshot.Name).ShouldBe(("Edit", "txtAmount", "Amount"));
        (element.SelectedIndex, element.UserChoseLocator).ShouldBe((1, true));
        element.Locators!.Candidates.Select(Describe).ShouldBe(document.Elements[0].Locators!.Candidates.Select(Describe));
        loaded.Steps.Select(step => step.Id).ShouldBe(document.Steps.Select(step => step.Id));
        RecordingStore.Serialize(loaded).ShouldBe(RecordingStore.Serialize(document));
    }

    private static string Describe(LocatorCandidate c) =>
        $"{c.Strategy}|{c.RenderedValue}|{c.Scope}|{c.Stability}|{c.Score}|{c.MatchCount}|{c.DistinctMatchCount}|{c.IsUnique}|{string.Join(',', c.Notes)}|{c.Explanation}|{XPathRenderer.Render(c.Spec)}";

    [Fact]
    public void The_file_is_readable_UTF8_without_BOM_and_keeps_non_Latin_text_as_it_is()
    {
        var path = Path.Combine(_folder, "flow" + RecordingStore.Extension);

        RecordingStore.Save(path, Sample());

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldContain("\"schemaVersion\": 1");
        text.ShouldContain("\"kind\": \"GridCellClick\"");
        text.ShouldContain("ძებნა");
    }

    [Fact]
    public void Values_window_titles_and_runtime_ids_are_never_written()
    {
        var json = RecordingStore.Serialize(Sample());

        json.ShouldNotContain("typed-value-from-the-screen");
        json.ShouldNotContain("Client 12345");
        json.ShouldNotContain("runtime", Case.Insensitive);
    }

    [Fact]
    public void A_file_from_a_newer_version_or_another_program_is_refused_with_the_reason()
    {
        var json = RecordingStore.Serialize(Sample());

        Should.Throw<RecordingFileException>(() => RecordingStore.Deserialize(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal)))
            .Problem.ShouldBe(RecordingFileProblem.NewerVersion);
        Should.Throw<RecordingFileException>(() => RecordingStore.Deserialize("{\"schemaVersion\": 1}")).Problem.ShouldBe(RecordingFileProblem.NotARecording);
        Should.Throw<RecordingFileException>(() => RecordingStore.Deserialize("not json")).Problem.ShouldBe(RecordingFileProblem.NotARecording);
    }

    [Fact]
    public void Opening_a_recording_fills_the_catalog_with_elements_that_a_new_capture_with_the_same_locator_merges_into()
    {
        var catalog = new ElementCatalog();
        var document = RecordingStore.Deserialize(RecordingStore.Serialize(Sample()));

        catalog.Load(document.Elements);
        var (captured, _) = catalog.Add(TestSnapshots.Edit());
        var kept = catalog.AttachLocators(captured, Report());

        catalog.Count.ShouldBe(1);
        kept.Id.ShouldBe(document.Elements[0].Id);
        kept.VariableName.ShouldBe("amountInput");
        catalog.Find(TestSnapshots.Edit().SessionKey).ShouldBe(kept);
    }

    private static RecordingDocument Sample()
    {
        var snapshot = TestSnapshots.Edit(ancestors: [TestSnapshots.LoanForm, TestSnapshots.Ancestor("Window", "frmMain", "LMS - Client 12345")]) with
        {
            Value = "typed-value-from-the-screen",
        };
        var element = new SavedElement(Guid.NewGuid(), PageKey.For(snapshot), snapshot, "amountInput", UserRenamed: true, Report(), SelectedIndex: 1, UserChoseLocator: true);
        var grid = Guid.NewGuid();
        RecordedStep[] steps =
        [
            new() { Kind = StepKind.SetText, Target = element.Id, Text = "5000", At = Created },
            new() { Kind = StepKind.SetText, Target = element.Id, TestDataKey = "passwordInput", At = Created },
            new() { Kind = StepKind.SetChecked, Target = element.Id, State = true, Warnings = [StepWarning.StateUnavailable], At = Created },
            new() { Kind = StepKind.MenuPath, Target = element.Id, MenuItems = ["File", "Export", "PDF"], At = Created },
            new() { Kind = StepKind.GridCellClick, Target = grid, RowIndex = 2, ColumnIndex = 3, ColumnHeader = "Status", CellControlType = "Edit", At = Created },
            new() { Kind = StepKind.Click, Target = element.Id, Comment = "ძებნა", At = Created },
            new() { Kind = StepKind.Comment, Scroll = -3, At = Created },
        ];
        return new RecordingDocument("Recorder.SampleApp.exe", Created, Created.AddMinutes(5), [element], steps);
    }

    private static LocatorReport Report()
    {
        var id = new LocatorCandidate(
            LocatorStrategy.AccessibilityId, LocatorSpec.ForAttribute(AttributeName.AutomationId, "txtAmount"), "txtAmount", LocatorScope.Window, Stability.High,
            150, 1, 1, true, [], new LocatorExplanation(LocatorKind.Id));
        var anchored = new LocatorCandidate(
            LocatorStrategy.XPath,
            new LocatorSpec(
                [
                    new LocatorStep(Axis.Descendant, null, [new AttrEquals(AttributeName.AutomationId, "frmLoan")]),
                    new LocatorStep(Axis.Descendant, "Edit", [new AttrStartsWith(AttributeName.ClassName, "WindowsForms10.EDIT"), new AttrContains(AttributeName.Name, "Amo")]),
                ],
                ResultIndex: 2),
            "(//*[@AutomationId='frmLoan']//Edit[starts-with(@ClassName,'WindowsForms10.EDIT')][contains(@Name,'Amo')])[2]", LocatorScope.Window, Stability.Low,
            25, 1, 1, true, [LocatorNote.PositionBased], new LocatorExplanation(LocatorKind.Indexed, Refinement.Index, "frmLoan", 3));
        return new LocatorReport([id, anchored], 0, IsChecked: true, LocatorScope.Window, 131, TimeSpan.FromMilliseconds(160));
    }
}
