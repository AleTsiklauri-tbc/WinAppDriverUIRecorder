using System.Windows;
using System.Windows.Input;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FilterElementsCommand = new(nameof(FilterElementsCommand), typeof(MainWindow));

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Elements.RenameRequested += (_, _) => InspectorPane.FocusName();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    // Ctrl+F: show the Elements tab and focus its filter box (view concern only).
    private void OnFilterElements(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel.SelectedLeftTab = 1;
        Dispatcher.BeginInvoke(ElementsPane.FocusFilter, System.Windows.Threading.DispatcherPriority.Input);
    }
}
