using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Recorder.App.Overlay;
using Recorder.App.ViewModels;
using Recorder.Core.Model;
using static Recorder.App.Interop.NativeMethods;

namespace Recorder.App.Views;

public partial class AssertPickerWindow : Window
{
    private readonly PixelPoint _anchor;
    private bool _closing;

    public AssertPickerWindow(AssertPickerViewModel viewModel, PixelPoint anchor)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _anchor = anchor;
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, _) => _closing = true;
        viewModel.CloseRequested += (_, _) =>
        {
            if (!_closing)
            {
                Close();
            }
        };
        Deactivated += (_, _) =>
        {
            // Clicking anywhere else cancels, like a menu.
            if (!_closing)
            {
                viewModel.CancelCommand.Execute(null);
            }
        };
        ContentRendered += (_, _) => PlaceNextToCursor();
        Loaded += (_, _) =>
        {
            OptionsList.Focus();
            if (OptionsList.ItemContainerGenerator.ContainerFromIndex(0) is UIElement first)
            {
                first.Focus();
            }
        };
    }

    // Placed in physical pixels just below and right of the click, kept on the click's monitor (view concern).
    private void PlaceNextToCursor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var work = MonitorArea.For(new PixelRect(_anchor.X, _anchor.Y, 1, 1)).WorkArea;
        var x = Math.Clamp(_anchor.X + 12, work.X, Math.Max(work.X, work.Right - width));
        var y = _anchor.Y + 16 + height <= work.Bottom ? _anchor.Y + 16 : Math.Max(work.Y, _anchor.Y - 8 - height);
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE);
    }

    private void OnOptionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssertOption option } && DataContext is AssertPickerViewModel viewModel)
        {
            viewModel.ConfirmCommand.Execute(option);
        }
    }
}
