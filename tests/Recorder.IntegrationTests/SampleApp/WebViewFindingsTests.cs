using System.Diagnostics;
using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.SampleApp;

/// <summary>
/// LMS hosts Angular/Kendo forms in WebView2. Their DOM elements are served by msedgewebview2.exe,
/// so filtering captured elements by the target's process id would miss all of them.
/// </summary>
public class WebViewFindingsTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app;

    public WebViewFindingsTests(SampleAppFixture app) => _app = app;

    [Fact]
    public void WebView2_content_belongs_to_another_process_and_reports_the_Chrome_framework()
    {
        Assert.SkipUnless(WebView2Runtime.IsInstalled(), "The WebView2 runtime is not installed.");

        var date = _app.OpenWebFormDateInput();

        date.ControlType.ShouldBe(ControlType.Edit);
        date.Properties.FrameworkId.Value.ShouldBe("Chrome");
        date.Properties.Name.Value.ShouldBe("ვადის დასასრული");
        date.Properties.ClassName.Value.ShouldContain("ng-pristine");

        var ownerPid = date.Properties.ProcessId.Value;
        ownerPid.ShouldNotBe(_app.Session.ProcessId);
        using var owner = Process.GetProcessById(ownerPid);
        owner.ProcessName.ShouldBe("msedgewebview2");
    }
}
