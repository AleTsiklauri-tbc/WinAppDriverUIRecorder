using CommunityToolkit.Mvvm.ComponentModel;

namespace Recorder.App.ViewModels;

public enum FirstRunStep
{
    ChooseTarget,
    CaptureOrRecord,
}

/// <summary>
/// The three-step guide shown until the first element is captured or the first step is recorded.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep), nameof(IsTargetHighlighted))]
    public partial bool HasTarget { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsTargetHighlighted))]
    public partial bool HasWork { get; set; }

    public FirstRunStep CurrentStep => HasTarget ? FirstRunStep.CaptureOrRecord : FirstRunStep.ChooseTarget;

    public bool IsVisible => !HasWork;

    /// <summary>The target selector pulses while choosing an app is the next thing to do.</summary>
    public bool IsTargetHighlighted => IsVisible && CurrentStep == FirstRunStep.ChooseTarget;
}
