using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.Core.Locators;
using Recorder.Core.Verification;

namespace Recorder.App.ViewModels;

/// <summary>One locator candidate as shown in the best-locator card and the "Other locators" list.</summary>
public sealed partial class LocatorItemViewModel : ObservableObject
{
    private readonly Action<LocatorItemViewModel> _choose;

    public LocatorItemViewModel(LocatorCandidate candidate, int index, Action<LocatorItemViewModel> choose)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        Index = index;
        _choose = choose;
        Warnings = [.. candidate.Notes.Where(LocatorNotes.IsWarning).Select(LocatorText.Note)];
        Infos = [.. candidate.Notes.Where(note => !note.IsWarning()).Select(LocatorText.Note)];
    }

    public LocatorCandidate Candidate { get; }

    /// <summary>Position in the element's report.</summary>
    public int Index { get; }

    public string Badge => LocatorText.Badge(Candidate.Strategy);

    public LocatorQuality Quality => LocatorText.Quality(Candidate);

    public string Value => Candidate.RenderedValue;

    public string MatchText => LocatorText.Matches(Candidate);

    public string Explanation => LocatorText.Explanation(Candidate);

    public string StabilityText => LocatorText.Stability(Candidate.Stability);

    /// <summary>Bars lit in the stability meter: 3 High, 2 Medium, 1 Low.</summary>
    public int StabilityLevel => 3 - (int)Candidate.Stability;

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> Infos { get; }

    /// <summary>"Badge value": what screen readers announce for a row.</summary>
    public string AccessibleName => Badge + " " + Value;

    /// <summary>
    /// Bound two-way to the row's radio button, so a click and the UI Automation SelectionItem pattern both choose the
    /// locator. Rows in "Other locators" are never the chosen one, so it reads false.
    /// </summary>
    public bool IsChosen
    {
        get => false;
        set
        {
            if (value)
            {
                _choose(this);
            }
        }
    }

    [ObservableProperty]
    public partial string? VerifyText { get; private set; }

    /// <summary>WinAppDriver found a different number of matches than the recorder counted.</summary>
    [ObservableProperty]
    public partial bool VerifyDisagrees { get; private set; }

    public void ShowVerification(CandidateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var ms = (int)Math.Round(check.Elapsed.TotalMilliseconds);
        VerifyText = check.Error is { } error ? Text.Format(Strings.Verify_Result_Error, error)
            : check.MatchCount == 1 ? Text.Format(Strings.Verify_Result_One, ms)
            : check.MatchCount == 0 ? Text.Format(Strings.Verify_Result_None, ms)
            : Text.Format(Strings.Verify_Result_Many, check.MatchCount, ms);
        VerifyDisagrees = check.Error is null && Candidate.IsChecked && check.MatchCount != Candidate.MatchCount;
    }

    [RelayCommand]
    private void Choose() => _choose(this);
}
