using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CaptureCoordinator _capture;
    private readonly ElementCatalog _catalog;
    private TargetState _targetState = TargetState.None;
    private bool _busy;
    private bool _isPicking;

    public MainWindowViewModel(
        FirstRunViewModel firstRun, ToastService toasts, BannerService banners, TargetSelectorViewModel target, ElementsViewModel elements,
        InspectorViewModel inspector, CaptureCoordinator capture, TargetTracker tracker, ElementCatalog catalog, ElementLocating locating,
        WinAppDriverService winAppDriver)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(locating);
        (FirstRun, Toasts, Banners, Target, Elements, Inspector, _capture, _catalog) = (firstRun, toasts, banners, target, elements, inspector, capture, catalog);
        WinAppDriver = winAppDriver;
        locating.Merged += (_, merge) =>
        {
            Elements.Remove(merge.Removed);
            Elements.Show(merge.Kept);
            Inspector.Show(merge.Kept.Snapshot);
        };

        elements.SelectionChanged += (_, item) =>
        {
            if (item is not null)
            {
                inspector.Show(item.Element.Snapshot);
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

        var dispatcher = Application.Current?.Dispatcher;
        tracker.Changed += (_, state) => dispatcher?.BeginInvoke(() => ApplyTarget(state));
        ApplyTarget(tracker.State);
    }

    public FirstRunViewModel FirstRun { get; }

    public ToastService Toasts { get; }

    public BannerService Banners { get; }

    public TargetSelectorViewModel Target { get; }

    public ElementsViewModel Elements { get; }

    public InspectorViewModel Inspector { get; }

    /// <summary>For the status bar's WinAppDriver indicator.</summary>
    public WinAppDriverService WinAppDriver { get; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = Strings.Status_Idle;

    [ObservableProperty]
    public partial string TargetStatusText { get; private set; } = Strings.Status_NoTarget;

    /// <summary>
    /// Bound two-way to the Pick toggle, so the UI Automation Toggle pattern works exactly like a click.
    /// Starting can fail (no app chosen, mouse hook blocked), so the real state is always reported back.
    /// </summary>
    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (value != _capture.IsPicking)
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

    private void OnCaptured(object? sender, PageElement element)
    {
        FirstRun.HasWork = _catalog.Count > 0;
        SelectedLeftTab = 1;
        Elements.Show(element);
        Inspector.Show(element.Snapshot);
    }

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
        StatusText = !_isPicking ? Strings.Status_Idle
            : _busy ? Text.Format(Strings.Status_Busy, _targetState.ProcessName)
            : Text.Format(Strings.Status_Picking, _targetState.ProcessName);
    }
}
