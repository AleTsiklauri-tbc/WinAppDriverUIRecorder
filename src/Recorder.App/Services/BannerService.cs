using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Recorder.App.Services;

/// <summary>A persistent condition shown at the top of the main window until it ends or is dismissed.</summary>
public sealed partial class BannerViewModel
{
    private readonly Action<BannerViewModel> _dismiss;
    private readonly Action? _action;

    public BannerViewModel(string id, string message, bool isWarning, string? actionText, Action? action, Action<BannerViewModel> dismiss)
    {
        Id = id;
        Message = message;
        IsWarning = isWarning;
        ActionText = actionText;
        _action = action;
        _dismiss = dismiss;
    }

    public string Id { get; }

    public string Message { get; }

    public bool IsWarning { get; }

    public string? ActionText { get; }

    public bool HasAction => ActionText is not null && _action is not null;

    [RelayCommand]
    private void RunAction() => _action?.Invoke();

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

/// <summary>Banners for persistent conditions (target closed, elevation, shortcut taken). UI thread only.</summary>
public sealed class BannerService
{
    public ObservableCollection<BannerViewModel> Items { get; } = [];

    /// <summary>Shows a banner, replacing any banner with the same id.</summary>
    public void Show(string id, string message, bool isWarning = false, string? actionText = null, Action? action = null)
    {
        var banner = new BannerViewModel(id, message, isWarning, actionText, action, b => Items.Remove(b));
        var index = IndexOf(id);
        if (index >= 0)
        {
            Items[index] = banner;
        }
        else
        {
            Items.Add(banner);
        }
    }

    public void Remove(string id)
    {
        var index = IndexOf(id);
        if (index >= 0)
        {
            Items.RemoveAt(index);
        }
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
