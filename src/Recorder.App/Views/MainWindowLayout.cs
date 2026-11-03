using System.Windows;
using System.Windows.Controls;
using Recorder.Core.Persistence;

namespace Recorder.App.Views;

/// <summary>
/// Spec 11.2: the main window comes back where it was, with its pane splits, and the code pane sits right of the
/// Inspector or below it (a setting that applies at once). Each position keeps its own size. UI thread only.
/// </summary>
internal sealed class MainWindowLayout
{
    private const double MinCodeWidth = 240;
    // The code pane's header, tabs, class picker and buttons take about 220 px of its height.
    private const double MinCodeHeight = 220;
    private const double SplitterSize = 6;

    // The Inspector's minimum width plus the splitters and margins.
    private const double CenterRoom = 400;

    private readonly MainWindow _window;
    private readonly SettingsStore _settings;
    private CodePanePosition _position = CodePanePosition.Right;
    private double _codeWidth = 360;
    private double _codeHeight = 360;
    private bool _wasMaximized;

    private MainWindowLayout(MainWindow window, SettingsStore settings)
    {
        (_window, _settings) = (window, settings);
        Restore(settings.Current.Window);
        Apply(settings.Current.CodePanePosition, force: true);
        settings.Changed += (_, current) => window.Dispatcher.BeginInvoke(() => Apply(current.CodePanePosition, force: false));
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized)
            {
                _wasMaximized = window.WindowState == WindowState.Maximized;
            }
        };
        window.Closing += (_, _) => Save();
    }

    /// <summary>Before the window shows: restores the saved layout and keeps it up to date.</summary>
    public static void Attach(MainWindow window, SettingsStore settings) => _ = new MainWindowLayout(window, settings);

    private void Restore(WindowLayout? saved)
    {
        var layout = saved?.FitTo(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (layout is null)
        {
            return;
        }

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        (_window.Left, _window.Top, _window.Width, _window.Height) = (layout.Left, layout.Top, layout.Width, layout.Height);
        if (layout.Maximized)
        {
            _window.WindowState = WindowState.Maximized;
            _wasMaximized = true;
        }

        // Splits from a larger window are cut down so the Inspector keeps its room.
        var width = layout.Maximized ? Math.Max(layout.Width, SystemParameters.WorkArea.Width) : layout.Width;
        var left = Math.Clamp(layout.LeftPaneWidth, _window.LeftColumn.MinWidth, Math.Max(_window.LeftColumn.MinWidth, width * 0.4));
        _window.LeftColumn.Width = new GridLength(left);
        _codeWidth = Math.Clamp(layout.CodePaneWidth, MinCodeWidth, Math.Max(MinCodeWidth, width - left - CenterRoom));
        _codeHeight = Math.Clamp(layout.CodePaneHeight, MinCodeHeight, Math.Max(MinCodeHeight, layout.Height * 0.6));
    }

    private void Apply(CodePanePosition position, bool force)
    {
        if (position == _position && !force)
        {
            return;
        }

        Remember();
        _position = position;
        var right = position == CodePanePosition.Right;
        var window = _window;
        window.CodeSplitterColumn.Width = right ? GridLength.Auto : new GridLength(0);
        window.CodeColumn.MinWidth = right ? MinCodeWidth : 0;
        window.CodeColumn.Width = new GridLength(right ? _codeWidth : 0);
        window.CodeSplitterRow.Height = right ? new GridLength(0) : GridLength.Auto;
        window.CodeRow.MinHeight = right ? 0 : MinCodeHeight;
        window.CodeRow.Height = new GridLength(right ? 0 : _codeHeight);

        Place(window.CodeSplitter, column: right ? 3 : 2, row: right ? 0 : 1, rowSpan: right ? 3 : 1);
        window.CodeSplitter.ResizeDirection = right ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        window.CodeSplitter.Width = right ? SplitterSize : double.NaN;
        window.CodeSplitter.Height = right ? double.NaN : SplitterSize;

        Place(window.CodePane, column: right ? 4 : 2, row: right ? 0 : 2, rowSpan: right ? 3 : 1);
        window.CodePane.Margin = right ? new Thickness(0, 8, 8, 8) : new Thickness(0, 0, 8, 8);
    }

    // The size the user gave the code pane where it is now, so switching back finds it again.
    private void Remember()
    {
        if (_position == CodePanePosition.Right && _window.CodeColumn.ActualWidth > 0)
        {
            _codeWidth = _window.CodeColumn.ActualWidth;
        }
        else if (_position == CodePanePosition.Bottom && _window.CodeRow.ActualHeight > 0)
        {
            _codeHeight = _window.CodeRow.ActualHeight;
        }
    }

    private void Save()
    {
        var bounds = _window.RestoreBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        Remember();
        var layout = new WindowLayout(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, _wasMaximized, _window.LeftColumn.ActualWidth, _codeWidth, _codeHeight);
        _settings.Update(settings => settings with { Window = layout });
    }

    private static void Place(FrameworkElement element, int column, int row, int rowSpan)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, rowSpan);
    }
}
