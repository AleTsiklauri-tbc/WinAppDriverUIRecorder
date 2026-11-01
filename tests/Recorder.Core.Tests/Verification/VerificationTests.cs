using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Recorder.Core.Locators;
using Recorder.Core.Verification;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Verification;

public class WinAppDriverEndpointTests
{
    [Fact]
    public void The_default_address_is_local()
    {
        var endpoint = WinAppDriverEndpoint.TryCreate(WinAppDriverEndpoint.DefaultUrl, null, allowRemote: false, out var problem).ShouldNotBeNull();

        problem.ShouldBe(EndpointProblem.None);
        endpoint.Resolve("session").ToString().ShouldBe("http://127.0.0.1:4723/session");
    }

    [Theory]
    [InlineData("http://localhost:4727", "/wd/hub", "http://localhost:4727/wd/hub/session")]
    [InlineData("http://127.0.0.1:4727", "wd/hub/", "http://127.0.0.1:4727/wd/hub/session")]
    [InlineData("http://127.0.0.1:4727/wd/hub", "", "http://127.0.0.1:4727/wd/hub/session")]
    [InlineData("http://[::1]:4723", null, "http://[::1]:4723/session")]
    public void A_path_prefix_is_honoured(string url, string? prefix, string session) =>
        WinAppDriverEndpoint.TryCreate(url, prefix, allowRemote: false, out _)!.Resolve("session").ToString().ShouldBe(session);

    [Fact]
    public void Remote_addresses_are_refused_unless_allowed()
    {
        WinAppDriverEndpoint.TryCreate("http://10.0.0.5:4723", null, allowRemote: false, out var problem).ShouldBeNull();
        problem.ShouldBe(EndpointProblem.RemoteNotAllowed);
        WinAppDriverEndpoint.TryCreate("http://10.0.0.5:4723", null, allowRemote: true, out _).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://127.0.0.1:4723")]
    public void Invalid_addresses_are_refused(string url)
    {
        WinAppDriverEndpoint.TryCreate(url, null, allowRemote: true, out var problem).ShouldBeNull();
        problem.ShouldBe(EndpointProblem.InvalidUrl);
    }
}

public class LocatorVerifierTests
{
    private static readonly WinAppDriverEndpoint Endpoint = WinAppDriverEndpoint.TryCreate("http://127.0.0.1:4791", null, false, out _)!;

    private static LocatorCandidate Candidate(LocatorStrategy strategy, string value) =>
        new(strategy, LocatorSpec.ForAttribute(AttributeName.Name, value), value, LocatorScope.Window, Stability.Medium, 0, 1, 1, true, [], new LocatorExplanation(LocatorKind.Name));

    [Fact]
    public async Task Each_candidate_is_counted_in_a_session_attached_to_the_window_which_is_then_deleted()
    {
        var driver = new FakeDriver();
        driver.Counts["accessibility id:txtAmount"] = 1;
        driver.Counts["xpath://Button[@Name='ძებნა']"] = 1;
        driver.Counts["name:Save"] = 2;
        using var client = new WinAppDriverClient(driver);

        var report = await new LocatorVerifier(client).VerifyAsync(Endpoint, 0xA90E1C, LocatorScope.Window,
            [Candidate(LocatorStrategy.AccessibilityId, "txtAmount"), Candidate(LocatorStrategy.Name, "Save"), Candidate(LocatorStrategy.XPath, "//Button[@Name='ძებნა']")],
            progress: null, TestContext.Current.CancellationToken);

        report.Outcome.ShouldBe(VerificationOutcome.Verified);
        report.Checks.Select(c => c.MatchCount).ShouldBe([1, 2, 1]);
        report.Checks.ShouldAllBe(c => c.Error == null);
        driver.Requests[0].ShouldBe("POST /session {\"desiredCapabilities\":{\"appTopLevelWindow\":\"0xA90E1C\"}}");
        driver.Requests[^1].ShouldBe("DELETE /session/S1 ");
    }

    [Fact]
    public async Task A_popup_is_verified_from_a_desktop_session()
    {
        var driver = new FakeDriver();
        using var client = new WinAppDriverClient(driver);

        await new LocatorVerifier(client).VerifyAsync(Endpoint, 0x1234, LocatorScope.Desktop, [Candidate(LocatorStrategy.Name, "USD")], null, TestContext.Current.CancellationToken);

        driver.Requests[0].ShouldBe("POST /session {\"desiredCapabilities\":{\"app\":\"Root\"}}");
    }

    [Fact]
    public async Task An_error_for_one_locator_is_reported_and_the_rest_still_run()
    {
        var driver = new FakeDriver();
        driver.Errors["xpath:(//Edit"] = "Invalid XPath expression";
        driver.Counts["name:Amount"] = 3;
        using var client = new WinAppDriverClient(driver);

        var report = await new LocatorVerifier(client).VerifyAsync(Endpoint, 1, LocatorScope.Window,
            [Candidate(LocatorStrategy.XPath, "(//Edit"), Candidate(LocatorStrategy.Name, "Amount")], null, TestContext.Current.CancellationToken);

        report.Outcome.ShouldBe(VerificationOutcome.Verified);
        report.Checks[0].ShouldBe(new CandidateCheck("XPath:(//Edit", -1, TimeSpan.Zero, "Invalid XPath expression"));
        report.Checks[1].MatchCount.ShouldBe(3);
        driver.Requests[^1].ShouldStartWith("DELETE");
    }

    [Fact]
    public async Task Nothing_listening_means_unreachable()
    {
        using var client = new WinAppDriverClient(new FakeDriver { Refuse = true });

        var report = await new LocatorVerifier(client).VerifyAsync(Endpoint, 1, LocatorScope.Window, [Candidate(LocatorStrategy.Name, "x")], null, TestContext.Current.CancellationToken);

        report.Outcome.ShouldBe(VerificationOutcome.Unreachable);
        (await client.IsReachableAsync(Endpoint, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_session_WinAppDriver_refuses_is_a_failure_with_its_message()
    {
        using var client = new WinAppDriverClient(new FakeDriver { SessionError = "Failed to locate opened application window with appId: 0x1" });

        var report = await new LocatorVerifier(client).VerifyAsync(Endpoint, 1, LocatorScope.Window, [Candidate(LocatorStrategy.Name, "x")], null, TestContext.Current.CancellationToken);

        report.Outcome.ShouldBe(VerificationOutcome.Failed);
        report.Problem.ShouldBe("Failed to locate opened application window with appId: 0x1");
    }

    [Theory]
    [InlineData(LocatorStrategy.AccessibilityId, "accessibility id")]
    [InlineData(LocatorStrategy.Name, "name")]
    [InlineData(LocatorStrategy.ClassName, "class name")]
    [InlineData(LocatorStrategy.XPath, "xpath")]
    public void Strategies_use_the_JSON_wire_names(LocatorStrategy strategy, string name) =>
        LocatorVerifier.StrategyName(strategy).ShouldBe(name);

    /// <summary>Answers like WinAppDriver 1.2 did in the spike (F-20).</summary>
    private sealed class FakeDriver : HttpMessageHandler
    {
        public Dictionary<string, int> Counts { get; } = [];

        public Dictionary<string, string> Errors { get; } = [];

        public List<string> Requests { get; } = [];

        public bool Refuse { get; init; }

        public string? SessionError { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Refuse)
            {
                throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path} {body}");
            if (path == "/status")
            {
                return Json(HttpStatusCode.OK, "{\"build\":{\"version\":\"1.2.2009\"}}");
            }

            if (path == "/session" && request.Method == HttpMethod.Post)
            {
                return SessionError is null
                    ? Json(HttpStatusCode.OK, "{\"sessionId\":\"S1\",\"status\":0,\"value\":{}}")
                    : Json(HttpStatusCode.InternalServerError, "{\"status\":13,\"value\":{\"error\":\"unknown error\",\"message\":\"" + SessionError + "\"}}");
            }

            if (path.EndsWith("/elements", StringComparison.Ordinal))
            {
                var query = JsonNode.Parse(body)!;
                var key = query["using"]!.GetValue<string>() + ":" + query["value"]!.GetValue<string>();
                if (Errors.TryGetValue(key, out var error))
                {
                    return Json(HttpStatusCode.BadRequest, "{\"status\":32,\"value\":{\"error\":\"invalid selector\",\"message\":\"" + error + "\"}}");
                }

                var found = string.Join(',', Enumerable.Range(0, Counts.GetValueOrDefault(key)).Select(i => $"{{\"ELEMENT\":\"e{i}\"}}"));
                return Json(HttpStatusCode.OK, "{\"sessionId\":\"S1\",\"status\":0,\"value\":[" + found + "]}");
            }

            return Json(HttpStatusCode.OK, "{\"sessionId\":\"S1\",\"status\":0,\"value\":null}");
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
            new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
