using System.Windows.Controls;
using System.Windows.Input;

namespace Recorder.App.Views;

public partial class ElementsView : UserControl
{
    public ElementsView() => InitializeComponent();

    /// <summary>Ctrl+F: puts the cursor in the filter box.</summary>
    public void FocusFilter()
    {
        FilterBox.Focus();
        Keyboard.Focus(FilterBox);
        FilterBox.SelectAll();
    }
}
