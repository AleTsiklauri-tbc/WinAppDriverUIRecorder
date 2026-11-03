using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

/// <summary>A recording in Export ▾ Open recent.</summary>
public sealed record RecentFile(string Path, int Index, ICommand Command)
{
    public string Name => RecordingFiles.NameOf(Path);

    public string AutomationId => "Menu.Recent." + Index;
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CaptureCoordinator _capture;
    private readonly ElementCatalog _catalog;
    private readonly StepList _steps;
    private readonly EditHistory _history;
    private readonly RecordingFiles _files;
    private readonly SettingsDialog _settingsDialog;
    private readonly SettingsStore _settings;
    private TargetState _targetState = TargetState.None;
    private bool _busy;
    private bool _isPicking;

    public MainWindowViewModel(
        FirstRunViewModel firstRun, ToastService toasts, BannerService banners, TargetSelectorViewModel target, ElementsViewModel elements,
        InspectorViewModel inspector, CaptureCoordinator capture, TargetTracker tracker, ElementCatalog catalog, ElementLocating locating,
        WinAppDriverService winAppDriver, StepsViewModel steps, RecordingViewModel recording, StepList stepList, EditHistory history, RecordingFiles files,
        CodeViewModel code, CheatSheetViewModel cheatSheet, SettingsDialog settingsDialog, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(locating);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(stepList);
        ArgumentNullException.ThrowIfNull(history);
        (FirstRun, Toasts, Banners, Target, Elements, Inspector, _capture, _catalog) = (firstRun, toasts, banners, target, elements, inspector, capture, catalog);
        (Steps, Recording, _steps, _history, _files) = (steps, recording, stepList, history, files);
        (WinAppDriver, Code, CheatSheet, _settingsDialog, _settings) = (winAppDriver, code, cheatSheet, settingsDialog, settings);
        locating.Merged += (_, merge) =>
        {
            Elements.Remove(merge.Removed);
            Elements.Show(merge.Kept);
            Inspector.ShowElement(merge.Kept);
        };

        elements.SelectionChanged += (_, item) =>
        {
            if (item is not null)
            {
                inspector.ShowElement(item.Element);
            }
        };
        steps.SelectionChanged += (_, element) =>
        {
            if (element is not null)
            {
                inspector.ShowElement(element);
            }
        };
        inspector.CaptureRequested += (_, snapshot) => capture.Capture(snapshot);
        capture.Captured += OnCaptured;
        capture.PickingChanged += (_, _) => UpdateStatus();
        capture.TargetBusyChanged += (_, busy) =>
        {
            _busy = busy;
            UpdateStatus();
        };

        // Elements of recorded steps are listed too; opening a recording replaces the list.
        catalog.ElementAdded += (_, element) =>
        {
            Elements.Include(element);
            UpdateHasWork();
        };
        catalog.Reset += (_, _) =>
        {
            Elements.Reset(catalog.Elements);
            UpdateHasWork();
        };
        stepList.Changed += (_, _) => UpdateHasWork();
        history.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        recording.PropertyChanged += (_, _) =>
        {
            UpdateStatus();
            SaveCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
            OpenRecentCommand.NotifyCanExecuteChanged();
            OpenSettingsCommand.NotifyCanExecuteChanged();
        };
        recording.Started += (_, _) => SelectedLeftTab = 0;
        recording.Stopped += (_, _) =>
        {
            SelectedLeftTab = 0;
            Steps.SelectLast();
        };

        var dispatcher = Application.Current?.Dispatcher;
        tracker.Changed += (_, state) => dispatcher?.BeginInvoke(() => ApplyTarget(state));
        settings.Changed += (_, _) => dispatcher?.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(RecentFiles));
            OnPropertyChanged(nameof(HasRecentFiles));
        });
        ApplyTarget(tracker.State);
    }

    public FirstRunViewModel FirstRun { get; }

    public ToastService Toasts { get; }

    public BannerService Banners { get; }

    public TargetSelectorViewModel Target { get; }

    public ElementsViewModel Elements { get; }

    public InspectorViewModel Inspector { get; }

    public StepsViewModel Steps { get; }

    public RecordingViewModel Recording { get; }

    /// <summary>The code pane (spec 10.4); also Ctrl+Shift+C and Export code (Ctrl+E).</summary>
    public CodeViewModel Code { get; }

    /// <summary>The ? button and F1 (spec 11.9).</summary>
    public CheatSheetViewModel CheatSheet { get; }

    /// <summary>For the status bar's WinAppDriver indicator.</summary>
    public WinAppDriverService WinAppDriver { get; }

    /// <summary>Export ▾ Open recent (spec 12): the recordings saved or opened lately, most recent first.</summary>
    public IReadOnlyList<RecentFile> RecentFiles => [.. _settings.Current.RecentRecordings.Select((path, index) => new RecentFile(path, index + 1, OpenRecentCommand))];

    public bool HasRecentFiles => _settings.Current.RecentRecordings.Count > 0;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = Strings.Status_Idle;

    [ObservableProperty]
    public partial string TargetStatusText { get; private set; } = Strings.Status_NoTarget;

    /// <summary>
    /// Bound two-way to the Pick toggle, so the UI Automation Toggle pattern works exactly like a click.
    /// Starting can fail (no app chosen, mouse hook blocked, recording), so the real state is always reported back.
    /// </summary>
    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (value != _capture.IsPicking && (!value || Recording.IsIdle))
            {
                _capture.TogglePick();
            }

            UpdateStatus();
        }
    }

    /// <summary>0 = Steps, 1 = Elements.</summary>
    [ObservableProperty]
    public partial int SelectedLeftTab { get; set; }

    /// <summary>Ctrl+C outside text boxes: the selected element's Java line (spec 11.9).</summary>
    [RelayCommand]
    private void CopyJavaLine()
    {
        if (Inspector.Locators.CopyJavaLineCommand.CanExecute(null))
        {
            Inspector.Locators.CopyJavaLineCommand.Execute(null);
        }
    }

    /// <summary>The ⚙ button (spec 12). Not while recording: the window is modal (spec 11.1).</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private void OpenSettings() => _settingsDialog.Show();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _history.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _history.Redo();

    /// <summary>Ctrl+S. Not while recording: the file dialog is modal (spec 11.1).</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private void Save()
    {
        if (Owner() is { } owner)
        {
            _files.Save(owner);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private void SaveAs()
    {
        if (Owner() is { } owner)
        {
            _files.SaveAs(owner);
        }
    }

    /// <summary>Ctrl+O.</summary>
    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private void Open()
    {
        if (Owner() is { } owner)
        {
            _files.Open(owner);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFiles))]
    private void OpenRecent(string? path)
    {
        if (path is not null && Owner() is { } owner)
        {
            _files.OpenRecent(owner, path);
        }
    }

    private bool CanUndo() => _history.CanUndo;

    private bool CanRedo() => _history.CanRedo;

    private bool CanUseFiles() => Recording.IsIdle;

    private static Window? Owner() => Application.Current?.MainWindow;

    private void OnCaptured(object? sender, PageElement element)
    {
        UpdateHasWork();
        SelectedLeftTab = 1;
        Elements.Show(element);
        Inspector.ShowElement(element);
    }

    private void UpdateHasWork() => FirstRun.HasWork = _catalog.Count > 0 || _steps.Count > 0;

    private void ApplyTarget(TargetState state)
    {
        _targetState = state;
        Target.Apply(state);
        FirstRun.HasTarget = state.Status != TargetStatus.None;
        TargetStatusText = state.Status == TargetStatus.None ? Strings.Status_NoTarget : Target.Caption;
        switch (state.Status)
        {
            case TargetStatus.Waiting:
                Banners.Show("target", Text.Format(state.WasClosed ? Strings.Banner_TargetClosed : Strings.Banner_TargetNotStarted, state.ProcessName));
                break;
            case TargetStatus.Elevated:
                Banners.Show("target", Strings.Banner_Elevated, isWarning: true, Strings.Banner_Elevated_Action, ElevationRestart.Restart);
                break;
            default:
                Banners.Remove("target");
                break;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _isPicking = _capture.IsPicking;
        OnPropertyChanged(nameof(IsPicking));
        StatusText = Recording.StatusText
            ?? (!_isPicking ? Strings.Status_Idle
                : _busy ? Text.Format(Strings.Status_Busy, _targetState.ProcessName)
                : Text.Format(Strings.Status_Picking, _targetState.ProcessName));
    }
}
