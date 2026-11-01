using System.Net;

namespace Recorder.Core.Verification;

/// <summary>
/// Where WinAppDriver listens. Only this machine is allowed unless the user explicitly allows a remote address
/// (spec 3: the one optional network use).
/// </summary>
public sealed record WinAppDriverEndpoint
{
    public const string DefaultUrl = "http://127.0.0.1:4723";

    private WinAppDriverEndpoint(Uri baseUrl, string pathPrefix)
    {
        BaseUrl = baseUrl;
        PathPrefix = pathPrefix;
    }

    public Uri BaseUrl { get; }

    /// <summary>"" or a prefix such as "/wd/hub".</summary>
    public string PathPrefix { get; }

    /// <summary>The address as people write it: base URL plus prefix.</summary>
    public string Display => BaseUrl.GetLeftPart(UriPartial.Authority) + PathPrefix;

    public static WinAppDriverEndpoint? TryCreate(string? url, string? pathPrefix, bool allowRemote, out EndpointProblem problem)
    {
        problem = EndpointProblem.None;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            problem = EndpointProblem.InvalidUrl;
            return null;
        }

        if (!allowRemote && !IsLoopback(parsed))
        {
            problem = EndpointProblem.RemoteNotAllowed;
            return null;
        }

        var prefix = (pathPrefix ?? string.Empty).Trim().TrimEnd('/');
        if (prefix.Length > 0 && !prefix.StartsWith('/'))
        {
            prefix = "/" + prefix;
        }

        // A path in the URL itself counts as the prefix ("http://127.0.0.1:4723/wd/hub").
        var urlPath = parsed.AbsolutePath.TrimEnd('/');
        if (prefix.Length == 0 && urlPath.Length > 0)
        {
            prefix = urlPath;
        }

        return new WinAppDriverEndpoint(new Uri(parsed.GetLeftPart(UriPartial.Authority)), prefix);
    }

    public Uri Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return new Uri(BaseUrl, PathPrefix + "/" + relativePath.TrimStart('/'));
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address));
}

public enum EndpointProblem
{
    None,
    InvalidUrl,
    RemoteNotAllowed,
}
