using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

/// <summary>Keyboard and focus handling only: Down moves from the search box into the list, Enter chooses the first match.</summary>
public partial class TargetSelectorView : UserControl
{
    public TargetSelectorView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() => SearchBox.Focus());
            }
        };
    }

    private TargetSelectorViewModel? ViewModel => DataContext as TargetSelectorViewModel;

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && FirstDescendant<Button>(WindowList) is { } first)
        {
            first.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && WindowList.Items.Count > 0)
        {
            ViewModel?.ChooseCommand.Execute(WindowList.Items[0]);
            e.Handled = true;
        }
    }

    private static T? FirstDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Queue<DependencyObject>([root]);
        while (pending.TryDequeue(out var current))
        {
            if (current is T match && !ReferenceEquals(current, root))
            {
                return match;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
    }
}
