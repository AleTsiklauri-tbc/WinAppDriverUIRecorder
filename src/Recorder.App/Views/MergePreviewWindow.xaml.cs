using System.Windows;

namespace Recorder.App.Views;

public partial class MergePreviewWindow : Window
{
    public MergePreviewWindow() => InitializeComponent();

    // Apply closes the dialog with a yes; CodeFiles then writes the .bak and the merged file.
    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
