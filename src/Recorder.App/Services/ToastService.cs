using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Recorder.App.Resources;
using Recorder.App.ViewModels;
using Recorder.Core.Diagnostics;

namespace Recorder.App.Services;

/// <summary>
/// Non-blocking notifications in the bottom-right corner. Safe to call from any thread.
/// </summary>
public sealed class ToastService
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(2.5);

    // Errors stay longer so there is time to press "Copy details".
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(8);

    private readonly IClipboardService _clipboard;
    private readonly Dispatcher _dispatcher;

    public ToastService(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public ObservableCollection<ToastViewModel> Items { get; } = [];

    public void Show(string message) =>
        Post(new ToastViewModel(message, details: null, isError: false, Dismiss, CopyDetails), InfoDuration);

    public void ShowError(ErrorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Post(new ToastViewModel(report.UserMessage, report.Details, isError: true, Dismiss, CopyDetails), ErrorDuration);
    }

    private void Post(ToastViewModel toast, TimeSpan duration)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Post(toast, duration));
            return;
        }

        Items.Add(toast);
        while (Items.Count > MaxVisible)
        {
            Items.RemoveAt(0);
        }

        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(toast);
        };
        timer.Start();
    }

    private void Dismiss(ToastViewModel toast) => Items.Remove(toast);

    private void CopyDetails(ToastViewModel toast)
    {
        if (toast.Details is null)
        {
            return;
        }

        Show(_clipboard.TrySetText(toast.Details) ? Strings.Toast_Copied : Strings.Error_ClipboardBusy);
    }
}
