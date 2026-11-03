using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Overlay;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;
using Recorder.Core.CodeGen;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Naming;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

public enum LocatorsState
{
    Empty,
    Checking,
    Ready,
    Gone,
    Busy,
}

/// <summary>
/// The Inspector's locator card and "Other locators" (spec 11.2) for the element shown: copy the Java line,
/// highlight matches, verify with WinAppDriver, choose another locator. UI thread only.
/// </summary>
public sealed partial class LocatorsViewModel : ObservableObject
{
    private readonly ILocatorService _locators;
    private readonly ElementLocating _locating;
    private readonly ElementCatalog _catalog;
    private readonly Overlays _overlays;
    private readonly IClipboardService _clipboard;
    private readonly ToastService _toasts;
    private readonly EditHistory _history;
    private readonly CodeProfiles _profiles;
    private PageElement? _element;
    private ElementSnapshot? _snapshot;
    private LocatorReport? _localReport;
    private UiaStatus? _localFailure;
    private int _localSelected = -1;
    private bool _localChecking;
    private int _request;

    // What the rows were built from. Renaming changes only the Java line, so the rows (and a click on one) survive it.
    private LocatorReport? _rowsReport;
    private int _rowsSelected = -1;

    public LocatorsViewModel(
        ILocatorService locators, ElementLocating locating, ElementCatalog catalog, WinAppDriverService winAppDriver,
        Overlays overlays, IClipboardService clipboard, ToastService toasts, EditHistory history, CodeProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(locating);
        ArgumentNullException.ThrowIfNull(profiles);
        (_locators, _locating, _catalog, _overlays, _clipboard, _toasts) = (locators, locating, catalog, overlays, clipboard, toasts);
        (_history, _profiles) = (history, profiles);
        VerifyPanel = new VerifyPanelViewModel(winAppDriver);
        locating.CheckingChanged += (_, element) =>
        {
            if (ReferenceEquals(element, _element))
            {
                Refresh();
            }
        };

        // Another profile, or an edited snippet.sbn: the line and its labels change.
        profiles.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CodeLineTitle));
            OnPropertyChanged(nameof(CopyLineText));
            OnPropertyChanged(nameof(CopyLineTip));
            Refresh();
        };
    }

    public ObservableCollection<LocatorItemViewModel> Others { get; } = [];

    public VerifyPanelViewModel VerifyPanel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected), nameof(SelectedTitle))]
    [NotifyCanExecuteChangedFor(nameof(CopyJavaLineCommand), nameof(CopyValueCommand), nameof(HighlightMatchesCommand), nameof(VerifyCommand))]
    public partial LocatorItemViewModel? Selected { get; private set; }

    [ObservableProperty]
    public partial LocatorsState State { get; private set; }

    /// <summary>"No unique locator found…", "Not checked…", "That control is gone…" or null.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>The selected locator as the active profile's snippet (snippet.sbn): the Java getter by default (D-32).</summary>
    [ObservableProperty]
    public partial string? JavaLine { get; private set; }

    /// <summary>Why snippet.sbn failed, e.g. "snippet.sbn line 1: …"; null when it worked.</summary>
    [ObservableProperty]
    public partial string? CodeLineError { get; private set; }

    /// <summary>"Java line" or "C# line", after the active profile.</summary>
    public string CodeLineTitle => Text.Format(Strings.Locator_CodeLine, _profiles.LanguageName);

    public string CopyLineText => Text.Format(Strings.Locator_CopyLine, _profiles.LanguageName);

    public string CopyLineTip => Text.Format(Strings.Locator_CopyLine_Tip, _profiles.LanguageName);

    public bool HasSelected => Selected is not null;

    public string SelectedTitle => (_element?.UserChoseLocator ?? _localSelected >= 0) ? Strings.Locator_SelectedTitle : Strings.Locator_BestTitle;

    public void Show(PageElement? element, ElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_element is not null)
        {
            _element.Changed -= OnElementChanged;
        }

        (_element, _snapshot, _localReport, _localFailure, _localSelected) = (element, snapshot, null, null, -1);
        _request++;
        BuildRows(null, -1);
        VerifyPanel.Reset();
        if (element is not null)
        {
            element.Changed += OnElementChanged;
            Refresh();
        }
        else
        {
            _ = LocateLocallyAsync(snapshot, _request);
        }
    }

    private void OnElementChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var report = _element?.Locators ?? _localReport;
        var checking = _element is not null ? _locating.IsChecking(_element) : _localChecking;
        var failure = _element is not null ? _locating.LastFailure(_element) : _localFailure;
        State = report is not null ? LocatorsState.Ready
            : checking ? LocatorsState.Checking
            : failure == UiaStatus.Gone ? LocatorsState.Gone
            : failure is not null ? LocatorsState.Busy
            : LocatorsState.Empty;

        var selectedIndex = report is null ? -1 : _element?.SelectedIndex ?? (_localSelected >= 0 ? _localSelected : report.SelectedIndex);
        if (!ReferenceEquals(report, _rowsReport) || selectedIndex != _rowsSelected)
        {
            BuildRows(report, selectedIndex);
        }

        StatusMessage = State switch
        {
            LocatorsState.Gone => Strings.Locator_Gone,
            LocatorsState.Busy => Strings.Locator_Busy,
            LocatorsState.Ready when !report!.IsChecked => Strings.Locator_NotChecked,
            LocatorsState.Ready when !report!.HasUniqueLocator => Strings.Locator_NoUnique,
            _ => null,
        };
        (JavaLine, CodeLineError) = Selected is null || _snapshot is null ? (null, null) : _profiles.Snippet(VariableName(_snapshot), _snapshot, Selected.Candidate);
        OnPropertyChanged(nameof(SelectedTitle));
        CheckAgainCommand.NotifyCanExecuteChanged();
    }

    private void BuildRows(LocatorReport? report, int selectedIndex)
    {
        (_rowsReport, _rowsSelected) = (report, selectedIndex);
        Others.Clear();
        Selected = null;
        for (var i = 0; report is not null && i < report.Candidates.Count; i++)
        {
            var row = new LocatorItemViewModel(report.Candidates[i], i, Choose);
            VerifyPanel.ShowKnownAnswer(row);
            if (i == selectedIndex)
            {
                Selected = row;
            }
            else
            {
                Others.Add(row);
            }
        }
    }

    private string VariableName(ElementSnapshot snapshot) => _element?.VariableName ?? VariableNamer.Suggest(snapshot, [], _catalog.Naming);

    private void Choose(LocatorItemViewModel item)
    {
        if (_element is not null)
        {
            // Undoable (spec 11.6): puts the earlier choice back exactly, including whether the user had made it.
            var element = _element;
            var (index, oldIndex, oldChose) = (item.Index, element.SelectedIndex, element.UserChoseLocator);
            _history.Execute(() => _catalog.ChooseLocator(element, index), () => _catalog.RestoreChoice(element, oldIndex, oldChose));
        }
        else
        {
            _localSelected = item.Index;
            Refresh();
        }
    }

    private async Task LocateLocallyAsync(ElementSnapshot snapshot, int request)
    {
        _localChecking = true;
        Refresh();
        var result = await _locators.LocateAsync(snapshot, LocateMode.Capture, CancellationToken.None).ConfigureAwait(true);
        if (request != _request)
        {
            return; // another element is shown now
        }

        _localChecking = false;
        _localReport = result.Value;
        _localFailure = result.Value is null ? result.Status : null;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void CopyJavaLine()
    {
        if (JavaLine is not null)
        {
            Copy(JavaLine);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void CopyValue()
    {
        if (Selected is not null)
        {
            Copy(Selected.Value);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task HighlightMatchesAsync()
    {
        if (Selected is not { } selected || _snapshot is null)
        {
            return;
        }

        var result = await _locators.FindMatchesAsync(_snapshot, selected.Candidate.Spec, CancellationToken.None).ConfigureAwait(true);
        var visible = (result.Value ?? []).Where(match => !match.IsOffscreen && !match.Bounds.IsEmpty).ToList();
        if (visible.Count == 0)
        {
            _toasts.Show(Strings.Locator_Highlight_NoneVisible);
            return;
        }

        var style = visible.Count == 1 ? LocatorText.Style(selected.Quality) : HighlightStyle.NoUnique;
        var outlines = visible.Select((match, i) => (match.Bounds, string.Create(CultureInfo.CurrentCulture, $"{i + 1}/{visible.Count}"))).ToList();
        _overlays.Matches.Show(outlines, style);
        if (visible.Count > MatchHighlighter.MaxShown)
        {
            _toasts.Show(Text.Format(Strings.Locator_Highlight_More, MatchHighlighter.MaxShown, visible.Count));
        }
    }

    // While it runs, the command disables itself (one verification at a time).
    [RelayCommand(CanExecute = nameof(HasSelected))]
    private async Task VerifyAsync()
    {
        if ((_element?.Locators ?? _localReport) is { } report && _snapshot is not null)
        {
            await VerifyPanel.RunAsync(_snapshot, report, () => Others.Prepend(Selected).OfType<LocatorItemViewModel>()).ConfigureAwait(true);
        }
    }

    private bool CanCheckAgain() => _snapshot is not null && State != LocatorsState.Checking;

    [RelayCommand(CanExecute = nameof(CanCheckAgain))]
    private async Task CheckAgainAsync()
    {
        VerifyPanel.Reset();
        if (_element is not null)
        {
            await _locating.LocateAsync(_element).ConfigureAwait(true);
        }
        else if (_snapshot is not null)
        {
            await LocateLocallyAsync(_snapshot, ++_request).ConfigureAwait(true);
        }
    }

    private void Copy(string text) => _toasts.Show(_clipboard.TrySetText(text) ? Strings.Toast_Copied : Strings.Error_ClipboardBusy);
}