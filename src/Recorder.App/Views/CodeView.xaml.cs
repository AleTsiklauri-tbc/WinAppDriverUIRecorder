using System.Windows.Controls;
using System.Windows.Input;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

public partial class CodeView : UserControl
{
    public CodeView() => InitializeComponent();

    // Leaving the class name box keeps what was typed, like Enter (view concern only).
    private void OnClassNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CodeViewModel { CommitClassNameCommand: var commit } && commit.CanExecute(null))
        {
            commit.Execute(null);
        }
    }
}
