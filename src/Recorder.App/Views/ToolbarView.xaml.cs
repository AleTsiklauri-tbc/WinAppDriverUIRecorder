using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Recorder.App.Views;

public partial class ToolbarView : UserControl
{
    public ToolbarView() => InitializeComponent();

    // Export ▾ opens its menu below the button on a click (view concern only).
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }
}
