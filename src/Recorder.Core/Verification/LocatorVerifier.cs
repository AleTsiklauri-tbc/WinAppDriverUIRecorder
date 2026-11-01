using Recorder.Core.Locators;

namespace Recorder.Core.Verification;

public enum VerificationOutcome
{
    /// <summary>Every candidate was sent to WinAppDriver (some may still have failed individually).</summary>
    Verified,

    /// <summary>Nothing answers at the address.</summary>
    Unreachable,

    /// <summary>WinAppDriver answered but could not start a session or broke off.</summary>
    Failed,
}

/// <param name="MatchCount">Elements WinAppDriver found; -1 when it reported an error for this locator.</param>
/// <param name="Error">WinAppDriver's message for this locator, for example about an XPath it cannot read.</param>
public sealed record CandidateCheck(string CandidateKey, int MatchCount, TimeSpan Elapsed, string? Error);

public sealed record VerificationReport(VerificationOutcome Outcome, IReadOnlyList<CandidateCheck> Checks, string? Problem);

/// <summary>
/// "Verify with WinAppDriver" (spec 7.7): the ground truth for match counts. One session per check, attached to the
/// app's window (or the desktop for pop-ups), one find per candidate, then the session is deleted.
/// </summary>
public sealed class LocatorVerifier(WinAppDriverClient client)
{
    public static string StrategyName(LocatorStrategy strategy) => strategy switch
    {
        LocatorStrategy.AccessibilityId => "accessibility id",
        LocatorStrategy.Name => "name",
        LocatorStrategy.ClassName => "class name",
        _ => "xpath",
    };

    public async Task<VerificationReport> VerifyAsync(
        WinAppDriverEndpoint endpoint,
        nint window,
        LocatorScope scope,
        IReadOnlyList<LocatorCandidate> candidates,
        IProgress<CandidateCheck>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(candidates);
        string sessionId;
        try
        {
            sessionId = await client.CreateSessionAsync(endpoint, scope == LocatorScope.Window ? window : null, cancellationToken).ConfigureAwait(false);
        }
        catch (WinAppDriverException exception)
        {
            return new VerificationReport(exception.IsUnreachable ? VerificationOutcome.Unreachable : VerificationOutcome.Failed, [], exception.Message);
        }

        var checks = new List<CandidateCheck>(candidates.Count);
        try
        {
            foreach (var candidate in candidates)
            {
                CandidateCheck check;
                try
                {
                    var (count, elapsed) = await client.CountElementsAsync(endpoint, sessionId, StrategyName(candidate.Strategy), candidate.RenderedValue, cancellationToken)
                        .ConfigureAwait(false);
                    check = new CandidateCheck(candidate.Key, count, elapsed, null);
                }
                catch (WinAppDriverException exception) when (!exception.IsUnreachable)
                {
                    check = new CandidateCheck(candidate.Key, -1, TimeSpan.Zero, exception.Message);
                }

                checks.Add(check);
                progress?.Report(check);
            }
        }
        catch (WinAppDriverException exception)
        {
            return new VerificationReport(VerificationOutcome.Unreachable, checks, exception.Message);
        }
        finally
        {
            await DeleteQuietlyAsync(endpoint, sessionId).ConfigureAwait(false);
        }

        return new VerificationReport(VerificationOutcome.Verified, checks, null);
    }

    private async Task DeleteQuietlyAsync(WinAppDriverEndpoint endpoint, string sessionId)
    {
        try
        {
            await client.DeleteSessionAsync(endpoint, sessionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (WinAppDriverException)
        {
            // WinAppDriver ends abandoned sessions itself; nothing else to do.
        }
    }
}
