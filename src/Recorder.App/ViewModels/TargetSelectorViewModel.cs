using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Automation;

namespace Recorder.App.ViewModels;

public sealed record TargetWindowItem(AppWindow Window, ImageSource? Icon)
{
    public string Title => Window.Title;

    public string ProcessName => Window.ProcessName;
}

/// <summary>The toolbar's target selector: the current app, and a searchable list of app windows grouped by process.</summary>
public sealed partial class TargetSelectorViewModel : ObservableObject
{
    private readonly TargetTracker _tracker;

    public TargetSelectorViewModel(TargetTracker tracker)
    {
        _tracker = tracker;
        View = CollectionViewSource.GetDefaultView(Windows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TargetWindowItem.ProcessName)));
        View.Filter = item => Matches((TargetWindowItem)item);
    }

    public ObservableCollection<TargetWindowItem> Windows { get; } = [];

    public ICollectionView View { get; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasNoMatch { get; set; }

    [ObservableProperty]
    public partial string Caption { get; set; } = Strings.Toolbar_Target;

    /// <summary>The target's window title, shown next to its process name. Never logged.</summary>
    [ObservableProperty]
    public partial string? CurrentTitle { get; set; }

    [ObservableProperty]
    public partial ImageSource? CurrentIcon { get; set; }

    [ObservableProperty]
    public partial bool HasTarget { get; set; }

    /// <summary>Shows the tracker's state. UI thread.</summary>
    public void Apply(TargetState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        HasTarget = state.Status != TargetStatus.None;
        Caption = state.Status switch
        {
            TargetStatus.Attached => state.ProcessName!,
            TargetStatus.Waiting => Text.Format(Strings.Target_Waiting, state.ProcessName),
            TargetStatus.Elevated => Text.Format(Strings.Target_NeedsAdmin, state.ProcessName),
            _ => Strings.Toolbar_Target,
        };
        CurrentTitle = state.IsAttached ? state.WindowTitle : null;
        if (!state.IsAttached)
        {
            CurrentIcon = null;
            return;
        }

        var handle = state.WindowHandle;
        _ = Task.Run(() => WindowIconLoader.Load(handle)).ContinueWith(
            task => CurrentIcon = task.Result,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var items = await Task.Run(() => WindowEnumerator.GetAppWindows()
                .Select(window => new TargetWindowItem(window, WindowIconLoader.Load(window.Handle)))
                .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList()).ConfigureAwait(true);
            Windows.Clear();
            foreach (var item in items)
            {
                Windows.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
            UpdateEmptyStates();
        }
    }

    [RelayCommand]
    private void Choose(TargetWindowItem? item)
    {
        if (item is null)
        {
            return;
        }

        _tracker.Select(item.Window);
        IsOpen = false;
    }

    [RelayCommand]
    private void Forget()
    {
        _tracker.Clear();
        IsOpen = false;
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            SearchText = string.Empty;
            RefreshCommand.Execute(null);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        View.Refresh();
        UpdateEmptyStates();
    }

    private bool Matches(TargetWindowItem item) =>
        string.IsNullOrWhiteSpace(SearchText)
        || item.Title.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || item.ProcessName.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void UpdateEmptyStates()
    {
        IsEmpty = !IsLoading && Windows.Count == 0;
        HasNoMatch = !IsEmpty && Windows.Count > 0 && View.IsEmpty;
    }
}
