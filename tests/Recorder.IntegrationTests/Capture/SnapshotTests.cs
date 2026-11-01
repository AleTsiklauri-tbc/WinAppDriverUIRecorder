using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.Capture;

/// <summary>Phase 1: what the capture engine reads from a real .NET Framework 4.8 WinForms app (spec 7.2, 13.1).</summary>
public sealed class SnapshotTests : IClassFixture<SampleAppFixture>, IDisposable
{
    private readonly SampleAppFixture _app;
    private readonly SpyValueReader _values = new();
    private readonly UiaService _uia;
    private readonly LocatorService _locators;

    public SnapshotTests(SampleAppFixture app)
    {
        _app = app;
        _uia = new UiaService(UiaOptions.Default, _values, TimeProvider.System, NullLogger<UiaService>.Instance);
        _locators = new LocatorService(UiaOptions.Default, LocatorOptions.Default, TimeProvider.System, NullLogger<LocatorService>.Instance);
    }

    public void Dispose()
    {
        _locators.Dispose();
        _uia.Dispose();
    }

    [Theory]
    [InlineData("txtAmount", "Edit", "frmLoan")]
    [InlineData("cmbCurrency", "ComboBox", "frmLoan")]
    [InlineData("chkInsured", "CheckBox", "frmLoan")]
    [InlineData("rbFixed", "RadioButton", "grpRateType")]
    [InlineData("btnSaveCustomer", "Button", "pnlCustomer")]
    [InlineData("lblAmount", "Text", "frmLoan")]
    public async Task A_named_control_is_captured_with_its_id_type_bounds_and_ancestors(string automationId, string controlType, string parentId)
    {
        var expectedBounds = Desktop.BoundsOf(_app.ById(automationId));

        var snapshot = await CaptureAsync(automationId);

        snapshot.AutomationId.ShouldBe(automationId);
        snapshot.ControlType.ShouldBe(controlType);
        snapshot.FrameworkId.ShouldBe("WinForm");
        snapshot.Bounds.ShouldBe(expectedBounds);
        snapshot.ProcessId.ShouldBe(_app.Session.ProcessId);
        snapshot.Ancestors[0].AutomationId.ShouldBe(parentId);
        snapshot.Ancestors[^1].AutomationId.ShouldBe("frmMain");
        snapshot.Ancestors[^1].ControlType.ShouldBe("Window");
        snapshot.TopLevelWindow.AutomationId.ShouldBe("frmMain");
        snapshot.TopLevelWindow.Handle.ShouldBe(_app.MainWindowHandle);
    }

    [Fact]
    public async Task A_password_field_is_flagged_and_its_value_is_never_read()
    {
        _app.ById("txtAmount").Patterns.Value.Pattern.SetValue("1500");

        var password = await CaptureAsync("txtPassword");
        var amount = await CaptureAsync("txtAmount");

        password.IsPassword.ShouldBeTrue();
        password.Value.ShouldBeNull();
        _values.ReadFrom.ShouldNotContain("txtPassword");

        // The spy does see ordinary fields, so the check above is not vacuous.
        amount.Value.ShouldBe("1500");
        _values.ReadFrom.ShouldContain("txtAmount");
    }

    [Fact]
    public async Task The_absolute_path_leads_from_the_main_window_to_the_element_and_resolves_back_to_it()
    {
        var snapshot = await CaptureAsync("rbFixed");

        var result = await _locators.AbsolutePathAsync(snapshot, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(UiaStatus.Ok);
        var xpath = XPathRenderer.Render(result.Value!);
        xpath.ShouldStartWith("/Window[@AutomationId='frmMain']/");
        xpath.ShouldEndWith("/Window[@AutomationId='frmLoan']/Group[@AutomationId='grpRateType']/RadioButton[@AutomationId='rbFixed']");

        var desktop = _app.Session.Automation.GetDesktop();
        var found = desktop.FindFirstByXPath(xpath);
        found.ShouldNotBeNull(xpath);
        found.Properties.RuntimeId.Value.ShouldBe([.. snapshot.RuntimeIdForSessionOnly]);
    }

    [Fact]
    public async Task Capturing_is_fast_enough_for_hover_highlighting()
    {
        string[] ids = ["txtAmount", "cmbCurrency", "chkInsured", "rbFixed", "btnSaveCustomer", "lblAmount", "txtPassword", "dgvLoans"];
        var points = ids.Select(id => Desktop.CenterOf(_app.ById(id))).ToList();
        Desktop.Raise(_app.MainWindowHandle);
        var token = TestContext.Current.CancellationToken;
        (await _uia.HoverAtAsync(points[0], token)).Status.ShouldBe(UiaStatus.Ok); // warm-up: connects to the target

        var timings = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            foreach (var point in points)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await _uia.HoverAtAsync(point, token);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
                result.Status.ShouldBe(UiaStatus.Ok);
            }
        }

        timings.Sort();
        var median = timings[timings.Count / 2];
        var p95 = timings[(int)Math.Ceiling(0.95 * timings.Count) - 1];
        TestContext.Current.TestOutputHelper?.WriteLine($"Element under a point: median {median:0.0} ms, p95 {p95:0.0} ms, max {timings[^1]:0.0} ms ({timings.Count} lookups)");
        p95.ShouldBeLessThan(100);
    }

    private async Task<ElementSnapshot> CaptureAsync(string automationId)
    {
        var center = Desktop.CenterOf(_app.ById(automationId));
        Desktop.Raise(_app.MainWindowHandle);

        var result = await _uia.CaptureAtAsync(center, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(UiaStatus.Ok, "capturing " + automationId);
        return result.Value!;
    }
}
