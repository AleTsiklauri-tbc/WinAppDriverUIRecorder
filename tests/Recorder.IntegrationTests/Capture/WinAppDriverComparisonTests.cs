using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Verification;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>
/// Spec 7.5 and 13.3: our match counts must equal WinAppDriver's. Skipped when WinAppDriver is not running at
/// WINFORMSRECORDER_WINAPPDRIVER_URL (default http://127.0.0.1:4723). The full comparison takes several minutes.
/// </summary>
[Trait("Requires", "WinAppDriver")]
public sealed partial class WinAppDriverComparisonTests : IClassFixture<SampleAppFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly SampleAppFixture _app;

    public WinAppDriverComparisonTests(SampleAppFixture app) => _app = app;

    [Fact]
    public async Task The_tree_we_search_is_the_tree_WinAppDriver_searches_with_dialogs_menus_and_drop_downs_open()
    {
        Assert.SkipUnless(await WinAppDriverSession.IsAvailableAsync(), "WinAppDriver is not running at " + WinAppDriverSession.ConfiguredEndpoint.Display);
        await using var driver = await WinAppDriverSession.AttachAsync(_app.MainWindowHandle);
        var main = _app.Session.MainWindow;

        await CompareTreesAsync(driver, "idle");

        Click.Post(_app.ById("btnShowMessage"));
        var box = Wait.For(() => main.FindFirstChild(c => c.ByName("Information")), Timeout, "the MessageBox");
        await CompareTreesAsync(driver, "MessageBox open");
        Click.Post(Wait.ForDescendant(box, "2"));
        Wait.Until(() => main.FindFirstChild(c => c.ByName("Information")) is null, Timeout, "the MessageBox to close");

        Click.Post(_app.ById("btnOpenDialog"));
        var dialog = Wait.For(() => main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")), Timeout, "the dialog");
        await CompareTreesAsync(driver, "modal dialog open");
        Click.Post(Wait.ForDescendant(dialog, "btnCancel"));
        Wait.Until(() => main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")) is null, Timeout, "the dialog to close");

        var file = Wait.For(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("File"))), Timeout, "the File menu");
        file.Patterns.ExpandCollapse.Pattern.Expand();
        Wait.For(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Export"))), Timeout, "File › Export");
        await CompareTreesAsync(driver, "File menu open");
        file.Patterns.ExpandCollapse.Pattern.Collapse();

        var combo = _app.ById("cmbCurrency");
        Click.ShowDropDown(combo, show: true);
        Wait.For(() => combo.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem)), Timeout, "the drop-down list");
        await CompareTreesAsync(driver, "drop-down open");
        Click.ShowDropDown(combo, show: false);
    }

    [Fact]
    public async Task Our_match_counts_equal_WinAppDriver_counts_for_every_generated_candidate()
    {
        Assert.SkipUnless(await WinAppDriverSession.IsAvailableAsync(), "WinAppDriver is not running at " + WinAppDriverSession.ConfiguredEndpoint.Display);
        await using var driver = await WinAppDriverSession.AttachAsync(_app.MainWindowHandle);
        var tree = await FetchMainTreeAsync();

        var expected = new Dictionary<string, (LocatorCandidate Candidate, string Element)>(StringComparer.Ordinal);
        foreach (var node in tree.Nodes)
        {
            var report = LocatorEngine.Analyze(LocatorSubject.From(node, _app.Session.ProcessId, LocatorScope.Window), tree, node, TimeSpan.Zero);
            foreach (var candidate in report.Candidates.Where(c => !Clock().IsMatch(c.RenderedValue)))
            {
                expected.TryAdd(candidate.Key, (candidate, node.ToString()));
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var mismatches = new List<string>();
        foreach (var (candidate, element) in expected.Values)
        {
            var theirs = await driver.CountAsync(LocatorVerifier.StrategyName(candidate.Strategy), candidate.RenderedValue);
            if (theirs != candidate.MatchCount)
            {
                mismatches.Add($"{candidate.Strategy} {candidate.RenderedValue} (for {element}): ours {candidate.MatchCount}, WinAppDriver {theirs}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Compared {expected.Count} distinct candidates for {tree.Count} elements in {stopwatch.Elapsed.TotalSeconds:0} s; {mismatches.Count} mismatches.");
        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Verify_with_WinAppDriver_reports_the_same_counts_as_the_engine()
    {
        Assert.SkipUnless(await WinAppDriverSession.IsAvailableAsync(), "WinAppDriver is not running at " + WinAppDriverSession.ConfiguredEndpoint.Display);
        using var uia = new UiaService(UiaOptions.Default, new ValuePatternReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
        using var locators = new LocatorService(UiaOptions.Default, LocatorOptions.Default, TimeProvider.System, NullLogger<LocatorService>.Instance);
        Desktop.Raise(_app.MainWindowHandle);
        var snapshot = (await uia.CaptureAtAsync(Desktop.CenterOf(_app.ById("btnSaveGuarantor")), CancellationToken.None)).Value!;
        var report = (await locators.LocateAsync(snapshot, LocateMode.Capture, CancellationToken.None)).Value!;
        using var client = new WinAppDriverClient();

        var verification = await new LocatorVerifier(client).VerifyAsync(
            WinAppDriverSession.ConfiguredEndpoint, snapshot.TopLevelWindow.Handle, snapshot.Scope, report.Candidates, null, CancellationToken.None);

        verification.Outcome.ShouldBe(VerificationOutcome.Verified);
        verification.Checks.Select(c => c.MatchCount).ShouldBe(report.Candidates.Select(c => c.MatchCount));
        verification.Checks.ShouldAllBe(c => c.Error == null && c.Elapsed > TimeSpan.Zero);
        Process.GetProcessById(_app.Session.ProcessId).HasExited.ShouldBeFalse(); // deleting the session leaves the app running
    }

    private async Task CompareTreesAsync(WinAppDriverSession driver, string state)
    {
        var ours = Flatten(await FetchMainTreeAsync());
        var theirs = Flatten(await driver.SourceAsync());

        var firstDifference = Enumerable.Range(0, Math.Min(ours.Count, theirs.Count)).FirstOrDefault(i => ours[i] != theirs[i], -1);
        TestContext.Current.TestOutputHelper?.WriteLine($"{state}: {ours.Count} elements here, {theirs.Count} in WinAppDriver.");
        ours.Count.ShouldBe(theirs.Count, $"{state}: element count");
        firstDifference.ShouldBe(-1, firstDifference < 0 ? state : $"{state}: first difference at {firstDifference}: ours '{ours[firstDifference]}', WinAppDriver '{theirs[firstDifference]}'");
    }

    // Document order: tag, name and (outside title bars, F-22) AutomationId. The clock's text changes every second.
    private static List<string> Flatten(UiTree tree) =>
        [.. tree.Nodes.Select(node => Describe(node.ControlType, node.Name, node.IsNonClient ? string.Empty : node.AutomationId))];

    private static List<string> Flatten(XElement root) =>
        [.. root.DescendantsAndSelf().Select(element => Describe(
            element.Name.LocalName,
            (string?)element.Attribute("Name") ?? string.Empty,
            element.Ancestors().Any(a => a.Name.LocalName == "TitleBar") ? string.Empty : (string?)element.Attribute("AutomationId") ?? string.Empty))];

    private static string Describe(string tag, string name, string automationId) =>
        $"{tag}|{(Clock().IsMatch(name) ? "<clock>" : name)}|{automationId}";

    private async Task<UiTree> FetchMainTreeAsync()
    {
        var handle = _app.MainWindowHandle;
        return await Task.Run(() =>
        {
            using var automation = new UIA3Automation();
            return new TreeFetcher(automation).Fetch(handle, CancellationToken.None);
        });
    }

    [GeneratedRegex(@"\d\d:\d\d:\d\d")]
    private static partial Regex Clock();
}
