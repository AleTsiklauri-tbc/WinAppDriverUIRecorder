using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

/// <summary>
/// The Steps tab (spec 11.2): a numbered list mirroring <see cref="StepList"/>. Deleting, duplicating, moving and editing
/// go through the shared undo history (spec 11.6). UI thread only.
/// </summary>
public sealed partial class StepsViewModel : ObservableObject
{
    private readonly StepList _steps;
    private readonly ElementCatalog _catalog;
    private readonly EditHistory _history;
    private readonly ToastService _toasts;

    public StepsViewModel(StepList steps, ElementCatalog catalog, EditHistory history, ToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(catalog);
        (_steps, _catalog, _history, _toasts) = (steps, catalog, history, toasts);
        steps.Changed += OnStepsChanged;
        catalog.Reset += (_, _) => Rebuild();
    }

    /// <summary>The selected step's element, for the Inspector; null when the step has none.</summary>
    public event EventHandler<PageElement?>? SelectionChanged;

    /// <summary>F2: the view moves focus to the Inspector's name box.</summary>
    public event EventHandler? RenameRequested;

    public ObservableCollection<StepItemViewModel> Items { get; } = [];

    /// <summary>All selected rows (Shift/Ctrl multi-select); kept up to date by the view.</summary>
    public IList<StepItemViewModel> SelectedItems { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(DuplicateCommand))]
    public partial StepItemViewModel? Selected { get; set; }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Selects the last step (after recording stops).</summary>
    public void SelectLast() => Selected = Items.Count > 0 ? Items[^1] : null;

    /// <summary>Drag and drop: moves a step, undoably.</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _steps.Count || to >= _steps.Count)
        {
            return;
        }

        var id = _steps[from].Id;
        _history.Execute(() => _steps.Move(from, to), () => _steps.Move(to, from));
        SelectById(id);
    }

    /// <summary>The view tells which rows are selected.</summary>
    public void SetSelection(IEnumerable<StepItemViewModel> items)
    {
        SelectedItems.Clear();
        foreach (var item in items)
        {
            SelectedItems.Add(item);
        }

        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var removed = Selection().Select(item => (Index: _steps.IndexOf(item.Step.Id), item.Step)).Where(x => x.Index >= 0).OrderBy(x => x.Index).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        var next = removed[0].Index;
        _history.Execute(
            () =>
            {
                foreach (var (index, _) in Enumerable.Reverse(removed))
                {
                    _steps.RemoveAt(index);
                }
            },
            () =>
            {
                foreach (var (index, step) in removed)
                {
                    _steps.Insert(index, step);
                }
            });
        Selected = Items.Count == 0 ? null : Items[Math.Min(next, Items.Count - 1)];
        _toasts.Show(removed.Count == 1 ? Strings.Toast_StepDeleted : Text.Format(Strings.Toast_StepsDeleted, removed.Count), Strings.Toast_Undo, _history.Undo);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var copies = Selection().OrderBy(item => _steps.IndexOf(item.Step.Id)).Select(item => item.Step with { Id = Guid.NewGuid() }).ToList();
        var at = Selection().Max(item => _steps.IndexOf(item.Step.Id)) + 1;
        _history.Execute(
            () =>
            {
                for (var i = 0; i < copies.Count; i++)
                {
                    _steps.Insert(at + i, copies[i]);
                }
            },
            () =>
            {
                for (var i = copies.Count - 1; i >= 0; i--)
                {
                    _steps.RemoveAt(at + i);
                }
            });
        SelectById(copies[^1].Id);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveSelected(+1);

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Edit() => Selected?.BeginEdit();

    /// <summary>The Edit button shown when hovering a row.</summary>
    [RelayCommand]
    private void EditItem(StepItemViewModel? item)
    {
        if (item is not null)
        {
            Selected = item;
            item.BeginEdit();
        }
    }

    /// <summary>The Delete button shown when hovering a row: deletes that row only.</summary>
    [RelayCommand]
    private void DeleteItem(StepItemViewModel? item)
    {
        if (item is not null)
        {
            SetSelection([item]);
            Selected = item;
            Delete();
        }
    }

    [RelayCommand]
    private void CommitEdit(StepItemViewModel? item)
    {
        item ??= Selected;
        if (item is null || !item.IsEditing)
        {
            return;
        }

        var before = item.Step;
        var after = item.Edited();
        item.IsEditing = false;
        var index = _steps.IndexOf(before.Id);
        if (after is null || index < 0)
        {
            return;
        }

        _history.Execute(() => Replace(before.Id, after), () => Replace(after.Id, before));
        SelectById(before.Id);
    }

    [RelayCommand]
    private void CancelEdit(StepItemViewModel? item)
    {
        if ((item ?? Selected) is { } editing)
        {
            editing.IsEditing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Rename()
    {
        if (Selected?.Element is not null)
        {
            RenameRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedChanged(StepItemViewModel? value) => SelectionChanged?.Invoke(this, value?.Element);

    private bool HasSelected() => Selected is not null;

    private bool HasSelection() => Selected is not null || SelectedItems.Count > 0;

    private bool CanMoveUp() => Selected is not null && Items.IndexOf(Selected) > 0;

    private bool CanMoveDown() => Selected is not null && Items.IndexOf(Selected) < Items.Count - 1;

    private IReadOnlyList<StepItemViewModel> Selection() =>
        SelectedItems.Count > 0 ? [.. SelectedItems] : Selected is null ? [] : [Selected];

    private void MoveSelected(int offset)
    {
        if (Selected is null)
        {
            return;
        }

        var from = Items.IndexOf(Selected);
        Move(from, from + offset);
    }

    private void Replace(Guid id, RecordedStep step)
    {
        var index = _steps.IndexOf(id);
        if (index >= 0)
        {
            _steps.Replace(index, step);
        }
    }

    private void SelectById(Guid id) => Selected = Items.FirstOrDefault(item => item.Step.Id == id);

    private PageElement? FindElement(Guid? id) => id is { } value ? _catalog.FindById(value) : null;

    private void OnStepsChanged(object? sender, StepListChangedEventArgs e)
    {
        switch (e.Change)
        {
            case StepListChange.Inserted:
                Items.Insert(e.Index, new StepItemViewModel(e.Step!, FindElement));
                break;
            case StepListChange.Removed:
                Items[e.Index].Detach();
                Items.RemoveAt(e.Index);
                break;
            case StepListChange.Replaced:
                Items[e.Index].Update(e.Step!);
                if (ReferenceEquals(Items[e.Index], Selected))
                {
                    SelectionChanged?.Invoke(this, Selected.Element);
                }

                break;
            case StepListChange.Moved:
                Items.Move(e.OldIndex, e.Index);
                break;
            default:
                Rebuild();
                return;
        }

        Renumber();
    }

    private void Rebuild()
    {
        foreach (var item in Items)
        {
            item.Detach();
        }

        Items.Clear();
        foreach (var step in _steps.Items)
        {
            Items.Add(new StepItemViewModel(step, FindElement));
        }

        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }

        OnPropertyChanged(nameof(IsEmpty));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
