using System.Text.Json.Nodes;
using System.Xml.Linq;
using Recorder.Core.Verification;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>
/// A WinAppDriver session for comparison tests. The address comes from WINFORMSRECORDER_WINAPPDRIVER_URL
/// (default http://127.0.0.1:4723); tests that need it are skipped when nothing answers there.
/// </summary>
internal sealed class WinAppDriverSession : IAsyncDisposable
{
    public const string UrlVariable = "WINFORMSRECORDER_WINAPPDRIVER_URL";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private WinAppDriverSession(WinAppDriverClient client, WinAppDriverEndpoint endpoint, string sessionId)
    {
        Client = client;
        Endpoint = endpoint;
        SessionId = sessionId;
    }

    public WinAppDriverClient Client { get; }

    public WinAppDriverEndpoint Endpoint { get; }

    public string SessionId { get; }

    public static WinAppDriverEndpoint ConfiguredEndpoint =>
        WinAppDriverEndpoint.TryCreate(Environment.GetEnvironmentVariable(UrlVariable) ?? WinAppDriverEndpoint.DefaultUrl, null, allowRemote: false, out _)
        ?? throw new InvalidOperationException(UrlVariable + " is not a local http address.");

    public static async Task<bool> IsAvailableAsync()
    {
        using var client = new WinAppDriverClient();
        return await client.IsReachableAsync(ConfiguredEndpoint, CancellationToken.None);
    }

    public static async Task<WinAppDriverSession> AttachAsync(nint window)
    {
        var client = new WinAppDriverClient();
        var endpoint = ConfiguredEndpoint;
        var sessionId = await client.CreateSessionAsync(endpoint, window, CancellationToken.None);
        return new WinAppDriverSession(client, endpoint, sessionId);
    }

    public async Task<int> CountAsync(string strategy, string value) =>
        (await Client.CountElementsAsync(Endpoint, SessionId, strategy, value, CancellationToken.None)).Count;

    /// <summary>WinAppDriver's page source: the XML document its XPath queries run against.</summary>
    /// <remarks>WinAppDriver 1.2 occasionally answers 500 to /source while the app is idle (F-24); a retry gets the page.</remarks>
    public async Task<XElement> SourceAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await Http.GetAsync(Endpoint.Resolve($"session/{SessionId}/source"));
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var xml = JsonNode.Parse(text)!["value"]!.GetValue<string>();
                return XDocument.Parse(xml).Root!;
            }

            if (attempt == 3)
            {
                throw new InvalidOperationException($"WinAppDriver /source answered {(int)response.StatusCode}: {text}");
            }

            await Task.Delay(500);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DeleteSessionAsync(Endpoint, SessionId, CancellationToken.None);
        Client.Dispose();
    }
}
