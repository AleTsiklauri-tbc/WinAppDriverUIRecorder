using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Tests.CodeGen;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Model;

/// <summary>
/// D-70: a window title (in LMS: the open customer's name and client number) is shown while the recorder runs, but never
/// becomes a locator or a name and is never written to a recording.
/// </summary>
public sealed class WindowTitleTests
{
    private const string Title = "Loan - ქორიძე (7693577)";

    [Theory]
    [InlineData("Window")]
    [InlineData("TitleBar")]
    [InlineData("Window", "Recorder Sample")]
    public void A_window_title_is_never_a_locator(string controlType, string title = Title) =>
        NameRules.Evaluate(title, controlType).IsAccepted.ShouldBeFalse();

    [Fact]
    public void A_position_locator_in_a_window_known_only_by_its_title_counts_from_the_nearest_window_with_an_id()
    {
        var dialog = TestSnapshots.Ancestor("Window", null, "Loan - John Smith", handle: 6001);
        var subject = LocatorSubject.From(TestSnapshots.Edit(automationId: null, name: null, ancestors: [dialog, TestSnapshots.MainForm]), LocatorScope.Window);

        var xpath = XPathRenderer.Render(CandidateGenerator.IndexBase(subject, anchor: null));

        xpath.ShouldNotContain("John Smith");
        xpath.ShouldStartWith("//Window[@AutomationId='frmMain']//Edit");
    }

    [Fact]
    public void A_window_captured_without_an_id_gets_no_candidate_with_its_title()
    {
        var window = TestSnapshots.Edit(automationId: null, name: Title, controlType: "Window", ancestors: []);

        var report = LocatorEngine.Unchecked(LocatorSubject.From(window, LocatorScope.Window), TimeSpan.Zero);

        report.Candidates.ShouldAllBe(candidate => !candidate.RenderedValue.Contains("7693577"));
    }

    [Fact]
    public void A_saved_recording_holds_no_window_title()
    {
        // The message box is known by its title "Recorder Sample"; the Clients window's Name is "Clients - John Example".
        var json = Save(SampleRecording.EveryStepKind());

        json.ShouldNotContain("Recorder Sample");
        json.ShouldNotContain("John Example");
        json.ShouldContain("\"id\": \"window:1\"");
        json.ShouldContain("\"displayName\": \"Window 1\"");
    }

    [Fact]
    public void A_numbered_page_keeps_its_class_name_and_stays_numbered_when_saved_again()
    {
        var recording = SampleRecording.AcceptanceFlow();
        recording.Catalog.RenamePage(recording.Catalog.Pages.Single(p => p.Key.IsTitled), "MessageBoxPage").ShouldBeTrue();

        var opened = Open(Save(recording));
        var again = RecordingStore.Deserialize(Save(opened));

        opened.Pages.Single(p => p.Key.IsNumbered).ClassName.ShouldBe("MessageBoxPage");
        again.PageClassNames.ShouldNotBeNull()[PageKey.Numbered(1)].ShouldBe("MessageBoxPage");
    }

    [Fact]
    public void A_capture_in_the_window_finds_its_numbered_page_again_with_its_elements_and_class_name()
    {
        var recording = SampleRecording.AcceptanceFlow();
        recording.Catalog.RenamePage(recording.Catalog.Pages.Single(p => p.Key.IsTitled), "MessageBoxPage");
        var opened = Open(Save(recording));
        var numbered = opened.Pages.Single(p => p.Key.IsNumbered);
        var (resets, removed) = (0, new List<PageElement>());
        opened.Reset += (_, _) => resets++;
        opened.ElementRemoved += (_, element) => removed.Add(element);

        // The OK button captured again in a new session: another runtime id, the live title.
        var live = recording.Ok.Snapshot with { RuntimeIdForSessionOnly = [42, 9001], NativeWindowHandle = 9001 };
        var (element, _) = opened.Add(live);
        var kept = opened.AttachLocators(element, recording.Ok.Locators!);

        kept.Id.ShouldBe(recording.Ok.Id);
        opened.Pages.Count.ShouldBe(3);
        opened.Pages.ShouldContain(numbered);
        numbered.Key.ShouldBe(PageKey.For(live));
        numbered.ClassName.ShouldBe("MessageBoxPage");
        numbered.Elements.ShouldBe([kept]);
        kept.Page.ShouldBe(numbered.Key);
        opened.Find(live.SessionKey).ShouldBeSameAs(kept);
        (resets, removed.Single()).ShouldBe((1, element));
    }

    [Fact]
    public void A_capture_in_another_titled_window_leaves_the_numbered_page_alone()
    {
        var recording = SampleRecording.AcceptanceFlow();
        var opened = Open(Save(recording));
        var cancel = recording.Ok.Snapshot with { AutomationId = "6", Name = "Cancel", RuntimeIdForSessionOnly = [42, 9002], NativeWindowHandle = 9002 };

        var (element, _) = opened.Add(cancel);
        var kept = opened.AttachLocators(element, new LocatorReport([SampleRecording.Id("6")], 0, true, LocatorScope.Window, 120, TimeSpan.Zero));

        kept.ShouldBeSameAs(element);
        opened.Pages.Count.ShouldBe(4);
        opened.Pages.Count(p => p.Key.IsNumbered).ShouldBe(1);
    }

    private static string Save(SampleRecording recording) => Save(recording.Catalog);

    private static string Save(ElementCatalog catalog) =>
        RecordingStore.Serialize(RecordingDocument.From(catalog, new StepList(), "Recorder.SampleApp.exe", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

    private static ElementCatalog Open(string json)
    {
        var document = RecordingStore.Deserialize(json);
        var catalog = new ElementCatalog();
        catalog.Load(document.Elements, document.PageClassNames);
        return catalog;
    }
}
