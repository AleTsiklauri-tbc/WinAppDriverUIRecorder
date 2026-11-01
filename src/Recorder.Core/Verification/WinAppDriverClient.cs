using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Core.Verification;

/// <summary>A WinAppDriver call that failed: the driver is unreachable, timed out or reported an error.</summary>
public sealed class WinAppDriverException : Exception
{
    public WinAppDriverException()
    {
    }

    public WinAppDriverException(string message)
        : base(message)
    {
    }

    public WinAppDriverException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Nothing listens at the address (or it did not answer at all).</summary>
    public bool IsUnreachable { get; init; }
}

/// <summary>
/// The smallest JSON Wire Protocol client WinAppDriver needs (spec 7.7): create a session, find elements, delete the
/// session. Every request gives up after <see cref="RequestTimeout"/>.
/// </summary>
public sealed class WinAppDriverClient : IDisposable
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;

    public WinAppDriverClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan; // each request has its own timeout
    }

    /// <summary>True when WinAppDriver answers /status.</summary>
    public async Task<bool> IsReachableAsync(WinAppDriverEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            await SendAsync(HttpMethod.Get, endpoint.Resolve("status"), body: null, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (WinAppDriverException exception) when (exception.IsUnreachable)
        {
            return false;
        }
    }

    /// <summary>A session on one top-level window (attach, nothing is started), or on the whole desktop.</summary>
    public async Task<string> CreateSessionAsync(WinAppDriverEndpoint endpoint, nint? topLevelWindow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var capabilities = new JsonObject();
        if (topLevelWindow is { } window)
        {
            capabilities["appTopLevelWindow"] = "0x" + ((long)window).ToString("X", CultureInfo.InvariantCulture);
        }
        else
        {
            capabilities["app"] = "Root";
        }

        var response = await SendAsync(HttpMethod.Post, endpoint.Resolve("session"), new JsonObject { ["desiredCapabilities"] = capabilities }, cancellationToken)
            .ConfigureAwait(false);
        return response["sessionId"]?.GetValue<string>() is { Length: > 0 } sessionId
            ? sessionId
            : throw new WinAppDriverException("WinAppDriver did not return a session id.");
    }

    /// <summary>How many elements a locator finds, and how long WinAppDriver took.</summary>
    public async Task<(int Count, TimeSpan Elapsed)> CountElementsAsync(
        WinAppDriverEndpoint endpoint, string sessionId, string strategy, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var stopwatch = Stopwatch.StartNew();
        var response = await SendAsync(HttpMethod.Post, endpoint.Resolve($"session/{sessionId}/elements"), new JsonObject { ["using"] = strategy, ["value"] = value }, cancellationToken)
            .ConfigureAwait(false);
        var count = response["value"] is JsonArray found ? found.Count : 0;
        return (count, stopwatch.Elapsed);
    }

    /// <summary>Ends the session. For an attached window, WinAppDriver leaves the app running (F-24).</summary>
    public async Task DeleteSessionAsync(WinAppDriverEndpoint endpoint, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await SendAsync(HttpMethod.Delete, endpoint.Resolve($"session/{sessionId}"), body: null, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonObject> SendAsync(HttpMethod method, Uri uri, JsonObject? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new WinAppDriverException("WinAppDriver is not reachable.", exception) { IsUnreachable = true };
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WinAppDriverException("WinAppDriver did not answer within " + RequestTimeout.TotalSeconds + " s.", exception);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JsonObject? json;
            try
            {
                json = text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException exception)
            {
                throw new WinAppDriverException("WinAppDriver sent an answer that is not JSON.", exception);
            }

            var status = json?["status"]?.GetValue<int>() ?? 0;
            if (!response.IsSuccessStatusCode || status != 0 || json is null)
            {
                throw new WinAppDriverException(ErrorMessage(json) ?? $"WinAppDriver answered {(int)response.StatusCode}.");
            }

            return json;
        }
    }

    private static string? ErrorMessage(JsonObject? json) => json?["value"] switch
    {
        JsonObject value => value["message"]?.GetValue<string>() ?? value["error"]?.GetValue<string>(),
        JsonValue value when value.TryGetValue<string>(out var message) => message,
        _ => null,
    };
}
