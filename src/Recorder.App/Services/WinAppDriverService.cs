using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Recorder.App.Resources;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Verification;

namespace Recorder.App.Services;

public enum WinAppDriverReachability
{
    Unknown,
    Reachable,
    Unreachable,
}

/// <summary>
/// "Verify with WinAppDriver" (spec 7.7). Talks to WinAppDriver only when the user asks: there is no background
/// polling, and the status bar shows the result of the last check. UI thread only.
/// </summary>
public sealed partial class WinAppDriverService : ObservableObject, IDisposable
{
    private readonly SettingsStore _settings;
    private readonly ILogger<WinAppDriverService> _logger;
    private readonly WinAppDriverClient _client = new();

    public WinAppDriverService(SettingsStore settings, ILogger<WinAppDriverService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [ObservableProperty]
    public partial WinAppDriverReachability Reachability { get; private set; }

    /// <summary>The address as the user typed it, including any path prefix.</summary>
    public string Address
    {
        get
        {
            var current = _settings.Current;
            return current.WinAppDriverUrl.TrimEnd('/') + current.WinAppDriverPathPrefix;
        }
    }

    /// <summary>Saves a new address; returns a message when it cannot be used.</summary>
    public string? TrySetAddress(string typed)
    {
        var endpoint = WinAppDriverEndpoint.TryCreate(typed, null, _settings.Current.AllowRemoteWinAppDriver, out var problem);
        if (endpoint is null)
        {
            return problem == EndpointProblem.RemoteNotAllowed ? Strings.Verify_Address_Remote : Strings.Verify_Address_Invalid;
        }

        _settings.Update(settings => settings with
        {
            WinAppDriverUrl = endpoint.BaseUrl.GetLeftPart(UriPartial.Authority),
            WinAppDriverPathPrefix = endpoint.PathPrefix,
        });
        Reachability = WinAppDriverReachability.Unknown;
        return null;
    }

    public async Task<VerificationReport> VerifyAsync(ElementSnapshot target, IReadOnlyList<LocatorCandidate> candidates, IProgress<CandidateCheck>? progress)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = _settings.Current;
        var endpoint = WinAppDriverEndpoint.TryCreate(current.WinAppDriverUrl, current.WinAppDriverPathPrefix, current.AllowRemoteWinAppDriver, out var problem);
        if (endpoint is null)
        {
            return new VerificationReport(VerificationOutcome.Failed, [], problem == EndpointProblem.RemoteNotAllowed ? Strings.Verify_Address_Remote : Strings.Verify_Address_Invalid);
        }

        var window = target.Scope == LocatorScope.Window ? target.TopLevelWindow.Handle : target.Host.Handle;
        var report = await new LocatorVerifier(_client).VerifyAsync(endpoint, window, target.Scope, candidates, progress, CancellationToken.None).ConfigureAwait(true);
        Reachability = report.Outcome == VerificationOutcome.Unreachable ? WinAppDriverReachability.Unreachable : WinAppDriverReachability.Reachable;
        LogVerified(_logger, report.Outcome, report.Checks.Count);
        return report;
    }

    public void Dispose() => _client.Dispose();

    [LoggerMessage(EventId = 632, Level = LogLevel.Information, Message = "Verify with WinAppDriver: {Outcome}, {Checks} locators")]
    private static partial void LogVerified(ILogger logger, VerificationOutcome outcome, int checks);
}
