using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Recorder.App.ViewModels;
using Recorder.Core.Persistence;

namespace Recorder.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FilterElementsCommand = new(nameof(FilterElementsCommand), typeof(MainWindow));

    private MiniBarWindow? _miniBar;
    private IInputElement? _focusBeforeCheatSheet;
    private WindowState _stateBeforeRecording = WindowState.Normal;

    public MainWindow(MainWindowViewModel viewModel, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        DataContext = viewModel;
        MainWindowLayout.Attach(this, settings);
        viewModel.Elements.RenameRequested += (_, _) => InspectorPane.FocusName();
        viewModel.Steps.RenameRequested += (_, _) => InspectorPane.FocusName();
        viewModel.Recording.Started += (_, _) => OnRecordingStarted(viewModel.Recording);
        viewModel.Recording.Stopped += (_, _) => OnRecordingStopped();
        viewModel.CheatSheet.PropertyChanged += OnCheatSheetChanged;
        StateChanged += (_, _) => viewModel.Code.IsShown = WindowState != WindowState.Minimized;
        Closed += (_, _) => _miniBar?.Close();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    // Ctrl+F: show the Elements tab and focus its filter box (view concern only).
    private void OnFilterElements(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel.SelectedLeftTab = 1;
        Dispatcher.BeginInvoke(ElementsPane.FocusFilter, DispatcherPriority.Input);
    }

    // The cheat sheet takes the focus while it is open and gives it back when it closes.
    private void OnCheatSheetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CheatSheetViewModel.IsOpen))
        {
            return;
        }

        if (ViewModel.CheatSheet.IsOpen)
        {
            _focusBeforeCheatSheet = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(() => CheatSheetClose.Focus(), DispatcherPriority.Input);
        }
        else
        {
            _focusBeforeCheatSheet?.Focus();
            _focusBeforeCheatSheet = null;
        }
    }

    // A click beside the card closes the cheat sheet; a click on the card does not.
    private void OnCheatSheetBackdropClick(object sender, MouseButtonEventArgs e) => ViewModel.CheatSheet.IsOpen = false;

    private void OnCheatSheetCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // Spec 11.4: while recording, the main window steps aside (a setting) and the mini-bar stays on top.
    private void OnRecordingStarted(RecordingViewModel recording)
    {
        ViewModel.CheatSheet.IsOpen = false;
        _miniBar?.Close();
        _miniBar = new MiniBarWindow(recording, ViewModel.Toasts);
        _miniBar.Show();
        if (recording.MinimizeOnRecord)
        {
            _stateBeforeRecording = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            WindowState = WindowState.Minimized;
        }
    }

    // Stop brings the main window back as it was (maximized or not) with the Steps tab focused and the last step selected.
    private void OnRecordingStopped()
    {
        _miniBar?.Close();
        _miniBar = null;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _stateBeforeRecording;
        }

        Activate();
        Dispatcher.BeginInvoke(StepsPane.FocusSelected, DispatcherPriority.Input);
    }
}
