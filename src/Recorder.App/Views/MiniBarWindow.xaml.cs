using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Recorder.App.Overlay;
using Recorder.App.Services;
using Recorder.App.ViewModels;
using Recorder.Core.Model;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Views;

/// <summary>
/// The recording mini-bar (spec 11.4): an always-on-top pill at the top center of the target's monitor. It never
/// activates, so keyboard focus stays in the app being recorded, and clicks on it are not recorded (another process).
/// </summary>
public partial class MiniBarWindow : Window
{
    private readonly RecordingViewModel _viewModel;

    public MiniBarWindow(RecordingViewModel viewModel, ToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ToastHost.DataContext = toasts;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(handle, GWL_EXSTYLE);
            SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };
        ContentRendered += (_, _) => Place();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        if (_viewModel.MiniBarPosition is { } saved)
        {
            // Kept on a monitor that still exists.
            var area = MonitorArea.For(new PixelRect(saved.Left, saved.Top, width, 1)).WorkArea;
            SetWindowPos(handle, HWND_TOPMOST, Math.Clamp(saved.Left, area.X, Math.Max(area.X, area.Right - width)), Math.Clamp(saved.Top, area.Y, area.Bottom - 1), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            return;
        }

        var target = WindowBounds(_viewModel.TargetWindow);
        var work = MonitorArea.For(target ?? new PixelRect(0, 0, 1, 1)).WorkArea;
        SetWindowPos(handle, HWND_TOPMOST, work.X + ((work.Width - width) / 2), work.Y + (int)(8 * scale), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static unsafe PixelRect? WindowBounds(nint window)
    {
        RECT rect;
        return window != 0 && GetWindowRect(window, &rect) ? PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }

    // Draggable anywhere on the pill except its buttons; the new position is remembered.
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || IsInButton(source))
        {
            return;
        }

        DragMove();
        var handle = new WindowInteropHelper(this).Handle;
        if (WindowBounds(handle) is { } bounds)
        {
            _viewModel.RememberMiniBarPosition(bounds.X, bounds.Y);
        }
    }

    private static bool IsInButton(DependencyObject element)
    {
        for (var current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }
}
