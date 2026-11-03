using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

/// <summary>View concerns of the Steps list: multi-selection, drag to reorder, focusing the inline editor.</summary>
public partial class StepsView : UserControl
{
    private Point _dragStart;
    private StepItemViewModel? _dragItem;

    public StepsView() => InitializeComponent();

    private StepsViewModel ViewModel => (StepsViewModel)DataContext;

    /// <summary>Moves keyboard focus to the selected row (after recording stops).</summary>
    public void FocusSelected()
    {
        if (StepsList.SelectedItem is { } item && StepsList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            StepsList.ScrollIntoView(item);
            container.Focus();
        }
        else
        {
            StepsList.Focus();
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SetSelection(StepsList.SelectedItems.Cast<StepItemViewModel>());

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(StepsList);
        _dragItem = ItemAt(e.OriginalSource as DependencyObject);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _dragItem.IsEditing)
        {
            return;
        }

        var offset = e.GetPosition(StepsList) - _dragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(StepsList, item, DragDropEffects.Move);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(StepItemViewModel)) is not StepItemViewModel dragged)
        {
            return;
        }

        var target = ItemAt(e.OriginalSource as DependencyObject);
        var to = target is null ? ViewModel.Items.Count - 1 : ViewModel.Items.IndexOf(target);
        ViewModel.Move(ViewModel.Items.IndexOf(dragged), to);
    }

    // The inline editor takes focus with its text selected as soon as it appears.
    private void OnEditorVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private static StepItemViewModel? ItemAt(DependencyObject? element)
    {
        for (var current = element; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is ListBoxItem { DataContext: StepItemViewModel item })
            {
                return item;
            }
        }

        return null;
    }
}
