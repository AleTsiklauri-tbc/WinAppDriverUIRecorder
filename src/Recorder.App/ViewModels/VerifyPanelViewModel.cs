using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Verification;

namespace Recorder.App.ViewModels;

/// <summary>
/// The locator card's "Verify with WinAppDriver" panel (spec 7.7): the address, progress and outcome. Each locator row
/// shows WinAppDriver's own count; answers are kept per candidate so rebuilt rows show them again. UI thread only.
/// </summary>
public sealed partial class VerifyPanelViewModel : ObservableObject
{
    private readonly WinAppDriverService _winAppDriver;
    private readonly Dictionary<string, CandidateCheck> _answers = new(StringComparer.Ordinal);

    public VerifyPanelViewModel(WinAppDriverService winAppDriver)
    {
        ArgumentNullException.ThrowIfNull(winAppDriver);
        _winAppDriver = winAppDriver;
        Address = winAppDriver.Address;
    }

    /// <summary>The address as typed; saved when the user presses Enter or verifies.</summary>
    [ObservableProperty]
    public partial string Address { get; set; }

    /// <summary>"Asking WinAppDriver…", the outcome, or what is wrong with the address.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    /// <summary>The panel opens on the first Verify and stays open for the element shown.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    /// <summary>Forgets the answers and closes the panel: another element is shown, or it was checked again.</summary>
    public void Reset()
    {
        _answers.Clear();
        Status = null;
        IsVisible = false;
    }

    /// <summary>Shows WinAppDriver's earlier answer on a row that was just built.</summary>
    public void ShowKnownAnswer(LocatorItemViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_answers.TryGetValue(row.Candidate.Key, out var answer))
        {
            row.ShowVerification(answer);
        }
    }

    /// <summary>Asks WinAppDriver about every candidate; each answer appears on its row as it arrives.</summary>
    public async Task RunAsync(ElementSnapshot snapshot, LocatorReport report, Func<IEnumerable<LocatorItemViewModel>> rows)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rows);
        if (SaveAddressCore() is { Length: > 0 })
        {
            return;
        }

        IsVisible = true;
        Status = Strings.Verify_Running;
        var progress = new Progress<CandidateCheck>(answer =>
        {
            _answers[answer.CandidateKey] = answer;
            rows().FirstOrDefault(row => row.Candidate.Key == answer.CandidateKey)?.ShowVerification(answer);
        });
        var result = await _winAppDriver.VerifyAsync(snapshot, report.Candidates, progress).ConfigureAwait(true);
        Status = result.Outcome switch
        {
            VerificationOutcome.Verified => Text.Format(Strings.Verify_Done, _winAppDriver.Address),
            VerificationOutcome.Unreachable => Text.Format(Strings.Verify_Unreachable, _winAppDriver.Address),
            _ => Text.Format(Strings.Verify_Failed, result.Problem),
        };
    }

    [RelayCommand]
    private void SaveAddress() => SaveAddressCore();

    // Null or empty when the address is fine; the problem otherwise (shown in the panel).
    private string? SaveAddressCore()
    {
        if (string.Equals(Address.Trim(), _winAppDriver.Address, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var problem = _winAppDriver.TrySetAddress(Address);
        if (problem is not null)
        {
            IsVisible = true;
            Status = problem;
        }
        else
        {
            Address = _winAppDriver.Address;
        }

        return problem;
    }
}
