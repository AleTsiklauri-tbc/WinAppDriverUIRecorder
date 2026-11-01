using System.Windows.Controls;
using System.Windows.Input;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    /// <summary>F2 in the Elements list: edit the selected element's variable name.</summary>
    public void FocusName()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    // Leaving the box keeps the typed name, like Enter.
    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (DataContext as InspectorViewModel)?.CommitNameCommand.Execute(null);
}
