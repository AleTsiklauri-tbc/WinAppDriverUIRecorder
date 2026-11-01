using CommunityToolkit.Mvvm.Input;

namespace Recorder.App.ViewModels;

public sealed partial class ToastViewModel
{
    private readonly Action<ToastViewModel> _dismiss;
    private readonly Action<ToastViewModel> _copyDetails;

    public ToastViewModel(string message, string? details, bool isError, Action<ToastViewModel> dismiss, Action<ToastViewModel> copyDetails)
    {
        Message = message;
        Details = details;
        IsError = isError;
        _dismiss = dismiss;
        _copyDetails = copyDetails;
    }

    public string Message { get; }

    /// <summary>Technical details for "Copy details"; never displayed.</summary>
    public string? Details { get; }

    public bool IsError { get; }

    public bool HasDetails => Details is not null;

    [RelayCommand]
    private void Dismiss() => _dismiss(this);

    [RelayCommand]
    private void CopyDetails() => _copyDetails(this);
}
