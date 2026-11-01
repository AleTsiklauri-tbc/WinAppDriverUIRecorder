using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Naming;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>Phase 2 acceptance (spec 13.3): the locator engine against the real SampleApp.</summary>
public sealed class LocatorTests : IClassFixture<SampleAppFixture>, IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly SampleAppFixture _app;
    private readonly UiaService _uia = new(UiaOptions.Default, new ValuePatternReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
    private readonly LocatorService _locators = new(UiaOptions.Default, LocatorOptions.Default, TimeProvider.System, NullLogger<LocatorService>.Instance);

    public LocatorTests(SampleAppFixture app) => _app = app;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _locators.Dispose();
        _uia.Dispose();
    }

    [Fact]
    public async Task Every_SampleApp_control_gets_a_unique_locator_and_named_controls_get_their_AutomationId()
    {
        var tree = await FetchMainTreeAsync();
        var failures = new List<string>();
        var named = 0;
        foreach (var node in tree.Nodes)
        {
            var subject = LocatorSubject.From(node, _app.Session.ProcessId, LocatorScope.Window);
            var best = LocatorEngine.Analyze(subject, tree, node, TimeSpan.Zero).Best;
            if (best is null || !best.IsUnique)
            {
                failures.Add($"{node}: best {best?.RenderedValue ?? "none"} matches {best?.MatchCount}");
                continue;
            }

            // A designer name that occurs once in the window must win, as High.
            if (subject.IdVerdict().IsHigh && LocatorEvaluator.Evaluate(LocatorSpec.ForAttribute(AttributeName.AutomationId, node.AutomationId), tree).Count == 1)
            {
                named++;
                if (best.Strategy != LocatorStrategy.AccessibilityId || best.Stability != Stability.High)
                {
                    failures.Add($"{node}: named control got {best.Strategy} {best.RenderedValue}");
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{tree.Count} elements, {named} named controls, {failures.Count} problems.");
        failures.ShouldBeEmpty();
        named.ShouldBeGreaterThan(30);
    }

    [Fact]
    public async Task The_two_Save_buttons_get_distinct_unique_refined_locators()
    {
        var customer = await LocateAsync(_app.ById("btnSaveCustomer"));
        var guarantor = await LocateAsync(_app.ById("btnSaveGuarantor"));

        var customerRefined = customer.Candidates.Single(c => c.Explanation.Refinement == Refinement.Anchor);
        var guarantorRefined = guarantor.Candidates.Single(c => c.Explanation.Refinement == Refinement.Anchor);
        customerRefined.RenderedValue.ShouldBe("//*[@AutomationId='pnlCustomer']//Button[@Name='Save']");
        guarantorRefined.RenderedValue.ShouldBe("//*[@AutomationId='pnlGuarantor']//Button[@Name='Save']");
        customerRefined.IsUnique.ShouldBeTrue();
        guarantorRefined.IsUnique.ShouldBeTrue();
        customer.Candidates.Single(c => c.RenderedValue == "Save").MatchCount.ShouldBe(2);
        customer.Best!.RenderedValue.ShouldBe("btnSaveCustomer");
        guarantor.Best!.RenderedValue.ShouldBe("btnSaveGuarantor");
    }

    [Fact]
    public async Task The_Georgian_search_button_gets_a_name_locator_with_the_UI_text_warning_and_the_name_dzebnaButton()
    {
        var button = Wait.For(() => _app.Loan.FindFirstChild(c => c.ByControlType(ControlType.Button).And(c.ByName("ძებნა"))), Timeout, "the ძებნა button");
        var snapshot = await CaptureAsync(button);

        var report = await LocateAsync(snapshot);

        var best = report.Best.ShouldNotBeNull();
        best.Strategy.ShouldBe(LocatorStrategy.Name);
        best.RenderedValue.ShouldBe("ძებნა");
        best.IsUnique.ShouldBeTrue();
        best.Notes.ShouldContain(LocatorNote.UiText);
        VariableNamer.Suggest(snapshot, []).ShouldBe("dzebnaButton");
    }

    [Fact]
    public async Task The_MessageBox_and_the_modal_dialog_are_separate_top_level_windows_reachable_from_the_main_session()
    {
        var main = _app.Session.MainWindow;
        Click.Post(_app.ById("btnShowMessage"));
        var box = Wait.For(() => main.FindFirstChild(c => c.ByControlType(ControlType.Window).And(c.ByName("Information"))), Timeout, "the MessageBox");
        try
        {
            var ok = Wait.For(() => box.FindFirstDescendant(c => c.ByAutomationId("2")), Timeout, "the OK button");
            var snapshot = await CaptureAsync(ok, box.Properties.NativeWindowHandle.Value);

            snapshot.Host.Handle.ShouldBe(box.Properties.NativeWindowHandle.Value);
            snapshot.Host.Handle.ShouldNotBe(snapshot.TopLevelWindow.Handle);
            snapshot.Host.ClassName.ShouldBe("#32770");
            snapshot.IsDetachedPopup.ShouldBeFalse();
            snapshot.Scope.ShouldBe(LocatorScope.Window);
            PageKey.For(snapshot).DisplayName.ShouldBe("Information");
            (await LocateAsync(snapshot)).Best!.IsUnique.ShouldBeTrue();
            Click.Post(ok);
        }
        finally
        {
            Wait.Until(() => main.FindFirstChild(c => c.ByName("Information")) is null, Timeout, "the MessageBox to close");
        }

        Click.Post(_app.ById("btnOpenDialog"));
        var dialog = Wait.For(() => main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")), Timeout, "the modal dialog");
        try
        {
            var comment = Wait.ForDescendant(dialog, "txtComment");
            var snapshot = await CaptureAsync(comment, dialog.Properties.NativeWindowHandle.Value);

            snapshot.Host.AutomationId.ShouldBe("ConfirmDialog");
            snapshot.Host.Handle.ShouldNotBe(snapshot.TopLevelWindow.Handle);
            snapshot.Scope.ShouldBe(LocatorScope.Window);
            PageKey.For(snapshot).DisplayName.ShouldBe("ConfirmDialog");
            snapshot.LabelText.ShouldBe("Comment");
            var report = await LocateAsync(snapshot);
            report.Best!.RenderedValue.ShouldBe("txtComment");
            report.Best.IsUnique.ShouldBeTrue();
            Click.Post(Wait.ForDescendant(dialog, "btnCancel"));
        }
        finally
        {
            Wait.Until(() => main.FindFirstChild(c => c.ByAutomationId("ConfirmDialog")) is null, Timeout, "the dialog to close");
        }
    }

    [Fact]
    public async Task An_open_drop_down_list_needs_a_desktop_session()
    {
        var combo = _app.ById("cmbCurrency");
        Click.ShowDropDown(combo, show: true);
        try
        {
            var item = Wait.For(() => combo.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName("USD"))), Timeout, "the USD item");
            var listWindow = item.Parent.Properties.NativeWindowHandle.Value;
            var snapshot = await CaptureAsync(item, listWindow);

            snapshot.IsDetachedPopup.ShouldBeTrue();
            snapshot.Scope.ShouldBe(LocatorScope.Desktop);
            var report = await LocateAsync(snapshot);
            report.Scope.ShouldBe(LocatorScope.Desktop);
            report.Best!.IsUnique.ShouldBeTrue();
            report.Best.Notes.ShouldContain(LocatorNote.PopupWindow);
        }
        finally
        {
            Click.ShowDropDown(combo, show: false);
            Wait.Until(() => combo.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem)) is null, Timeout, "the drop-down to close");
        }
    }

    [Fact]
    public async Task Uniqueness_checks_are_fast_enough()
    {
        string[] ids = ["txtAmount", "cmbCurrency", "chkInsured", "rbFixed", "btnSaveCustomer", "lblAmount", "txtPassword", "btnOpenDialog", "txtStreet", "btnFreeze"];
        var snapshots = new List<ElementSnapshot>();
        foreach (var id in ids)
        {
            snapshots.Add(await CaptureAsync(_app.ById(id)));
        }

        await LocateAsync(snapshots[0]); // warm-up: connects the locator worker to the target
        var timings = new List<double>();
        for (var round = 0; round < 3; round++)
        {
            foreach (var snapshot in snapshots)
            {
                var stopwatch = Stopwatch.StartNew();
                var report = await LocateAsync(snapshot);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
                report.IsChecked.ShouldBeTrue();
            }
        }

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(0.95 * timings.Count) - 1];
        TestContext.Current.TestOutputHelper?.WriteLine($"Uniqueness check: median {timings[timings.Count / 2]:0} ms, p95 {p95:0} ms, max {timings[^1]:0} ms ({timings.Count} checks)");
        p95.ShouldBeLessThan(300);
    }

    private async Task<UiTree> FetchMainTreeAsync()
    {
        var handle = _app.MainWindowHandle;
        return await Task.Run(
            () =>
            {
                using var automation = new UIA3Automation();
                return new TreeFetcher(automation).Fetch(handle, Token);
            },
            Token);
    }

    private async Task<LocatorReport> LocateAsync(AutomationElement element) => await LocateAsync(await CaptureAsync(element));

    private async Task<LocatorReport> LocateAsync(ElementSnapshot snapshot)
    {
        var result = await _locators.LocateAsync(snapshot, LocateMode.Capture, Token);
        result.Status.ShouldBe(UiaStatus.Ok);
        return result.Value!;
    }

    private async Task<ElementSnapshot> CaptureAsync(AutomationElement element, nint? windowToRaise = null)
    {
        Desktop.Raise(windowToRaise ?? _app.MainWindowHandle);
        var result = await _uia.CaptureAtAsync(Desktop.CenterOf(element), Token);
        result.Status.ShouldBe(UiaStatus.Ok);
        result.Value!.SessionKey.ShouldBe(string.Join('.', element.Properties.RuntimeId.Value));
        return result.Value;
    }
}
