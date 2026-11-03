namespace Recorder.Core.Model;

public enum StepListChange
{
    Inserted,
    Removed,
    Replaced,
    Moved,
    Reset,
}

public sealed class StepListChangedEventArgs(StepListChange change, int index, int oldIndex, RecordedStep? step) : EventArgs
{
    public StepListChange Change { get; } = change;

    /// <summary>Where the change happened (the new position for a move).</summary>
    public int Index { get; } = index;

    /// <summary>For a move: where the step was.</summary>
    public int OldIndex { get; } = oldIndex;

    public RecordedStep? Step { get; } = step;
}

/// <summary>The recorded steps, in order. Not thread-safe: owned by the UI thread, like <see cref="ElementCatalog"/>.</summary>
public sealed class StepList
{
    private readonly List<RecordedStep> _items = [];

    public event EventHandler<StepListChangedEventArgs>? Changed;

    public IReadOnlyList<RecordedStep> Items => _items;

    public int Count => _items.Count;

    public RecordedStep this[int index] => _items[index];

    public int IndexOf(Guid id) => _items.FindIndex(step => step.Id == id);

    public void Add(RecordedStep step) => Insert(_items.Count, step);

    public void Insert(int index, RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _items.Insert(index, step);
        Changed?.Invoke(this, new StepListChangedEventArgs(StepListChange.Inserted, index, -1, step));
    }

    public RecordedStep RemoveAt(int index)
    {
        var step = _items[index];
        _items.RemoveAt(index);
        Changed?.Invoke(this, new StepListChangedEventArgs(StepListChange.Removed, index, -1, step));
        return step;
    }

    public void Replace(int index, RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _items[index] = step;
        Changed?.Invoke(this, new StepListChangedEventArgs(StepListChange.Replaced, index, -1, step));
    }

    public void Move(int from, int to)
    {
        if (from == to)
        {
            return;
        }

        var step = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, step);
        Changed?.Invoke(this, new StepListChangedEventArgs(StepListChange.Moved, to, from, step));
    }

    /// <summary>Replaces everything (opening a recording, starting over).</summary>
    public void Reset(IEnumerable<RecordedStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _items.Clear();
        _items.AddRange(steps);
        Changed?.Invoke(this, new StepListChangedEventArgs(StepListChange.Reset, -1, -1, null));
    }

    /// <summary>Points steps that target <paramref name="from"/> at <paramref name="to"/> (an element merged into another).</summary>
    public void Retarget(Guid from, Guid to)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Target == from)
            {
                Replace(i, _items[i] with { Target = to });
            }
        }
    }
}
