using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

/// <summary>Recording state for the toolbar, the status bar and the mini-bar (spec 11.2, 11.4). UI thread only.</summary>
public sealed partial class RecordingViewModel : ObservableObject
{
    private readonly RecordingCoordinator _coordinator;
    private readonly StepList _steps;
    private readonly ElementCatalog _catalog;
    private readonly TargetTracker _target;
    private readonly SettingsStore _settings;
    private readonly Shortcuts _shortcuts;
    private readonly DispatcherTimer _clock;

    public RecordingViewModel(RecordingCoordinator coordinator, StepList steps, ElementCatalog catalog, TargetTracker target, SettingsStore settings, Shortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(shortcuts);
        (_coordinator, _steps, _catalog, _target, _settings, _shortcuts) = (coordinator, steps, catalog, target, settings, shortcuts);
        shortcuts.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(PauseTip));
            OnPropertyChanged(nameof(AssertTip));
        };
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Refresh(), Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            IsEnabled = false,
        };
        coordinator.Changed += (_, _) =>
        {
            _clock.IsEnabled = coordinator.IsActive;
            Refresh();
        };
        coordinator.Started += (_, _) => Started?.Invoke(this, EventArgs.Empty);
        coordinator.Stopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
        steps.Changed += (_, _) => Refresh();
    }

    /// <inheritdoc cref="RecordingCoordinator.Started"/>
    public event EventHandler? Started;

    /// <inheritdoc cref="RecordingCoordinator.Stopped"/>
    public event EventHandler? Stopped;

    public bool IsIdle => !_coordinator.IsActive;

    public bool IsActive => _coordinator.IsActive;

    public bool IsRecording => _coordinator.Status == RecordingStatus.Recording;

    public bool IsPaused => _coordinator.Status == RecordingStatus.Paused;

    public bool IsAsserting => _coordinator.IsAsserting;

    /// <summary>Spec 11.4: the main window minimizes while recording.</summary>
    public bool MinimizeOnRecord => _settings.Current.MinimizeOnRecord;

    public string ElapsedText
    {
        get
        {
            var elapsed = _coordinator.Elapsed;
            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
                : elapsed.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
        }
    }

    public string StepCountText => _steps.Count == 1 ? Strings.Steps_CountOne : Text.Format(Strings.Steps_Count, _steps.Count);

    public string LastStepText => _steps.Count == 0 ? Strings.MiniBar_NoSteps : StepText.Sentence(_steps[^1], _catalog.FindById(_steps[^1].Target ?? Guid.Empty));

    public string TargetName => _target.State.ProcessName ?? string.Empty;

    public string MiniBarTitle => IsPaused ? Strings.MiniBar_Paused : Text.Format(Strings.MiniBar_Recording, TargetName);

    public string PauseText => IsPaused ? Strings.Toolbar_Resume : Strings.Toolbar_Pause;

    public string PauseTip => _shortcuts.Format(IsPaused ? Strings.Toolbar_Resume_Tip : Strings.Toolbar_Pause_Tip);

    public string AssertTip => _shortcuts.Format(IsAsserting ? Strings.Toolbar_Assert_Active_Tip : Strings.Toolbar_Assert_Tip);

    /// <summary>The status bar's state while recording; null when idle.</summary>
    public string? StatusText => _coordinator.Status switch
    {
        RecordingStatus.Recording when IsAsserting => Text.Format(Strings.Status_Asserting, TargetName),
        RecordingStatus.Recording => Text.Format(Strings.Status_Recording, ElapsedText, StepCountText),
        RecordingStatus.Paused => Text.Format(Strings.Status_Paused, StepCountText),
        _ => null,
    };

    /// <summary>Where the user left the mini-bar, in physical pixels; null for top center of the target's monitor.</summary>
    public (int Left, int Top)? MiniBarPosition =>
        _settings.Current is { MiniBarLeft: { } left, MiniBarTop: { } top } ? ((int)left, (int)top) : null;

    /// <summary>The target's window, to put the mini-bar on its monitor.</summary>
    public nint TargetWindow => _target.State.WindowHandle;

    public void RememberMiniBarPosition(int left, int top) =>
        _settings.Update(settings => settings with { MiniBarLeft = left, MiniBarTop = top });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Record() => _coordinator.StartAsync();

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void TogglePause() => _coordinator.TogglePause();

    [RelayCommand(CanExecute = nameof(IsActive))]
    private Task Stop() => _coordinator.StopAsync();

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void Assert() => _coordinator.ToggleAssert();

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        RecordCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        AssertCommand.NotifyCanExecuteChanged();
    }
}
