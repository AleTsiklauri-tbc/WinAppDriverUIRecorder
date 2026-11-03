using CommunityToolkit.Mvvm.Input;

namespace Recorder.App.ViewModels;

public sealed partial class ToastViewModel
{
    private readonly Action<ToastViewModel> _dismiss;
    private readonly Action<ToastViewModel> _copyDetails;

    private readonly Action? _action;

    public ToastViewModel(string message, string? details, bool isError, Action<ToastViewModel> dismiss, Action<ToastViewModel> copyDetails, string? actionText = null, Action? action = null)
    {
        Message = message;
        Details = details;
        IsError = isError;
        _dismiss = dismiss;
        _copyDetails = copyDetails;
        ActionText = actionText;
        _action = action;
    }

    public string Message { get; }

    /// <summary>A button such as "Undo" next to the message; null for none.</summary>
    public string? ActionText { get; }

    public bool HasAction => ActionText is not null && _action is not null;

    /// <summary>Technical details for "Copy details"; never displayed.</summary>
    public string? Details { get; }

    public bool IsError { get; }

    public bool HasDetails => Details is not null;

    [RelayCommand]
    private void Dismiss() => _dismiss(this);

    [RelayCommand]
    private void CopyDetails() => _copyDetails(this);

    [RelayCommand]
    private void RunAction()
    {
        _action?.Invoke();
        _dismiss(this);
    }
}
