namespace Recorder.Core.Recording;

/// <summary>Shared undo stack for steps, element names and locator choices. Owned by the UI thread.</summary>
public sealed class EditHistory
{
    private readonly Stack<(Action Apply, Action Revert)> _undo = [];
    private readonly Stack<(Action Apply, Action Revert)> _redo = [];

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Execute(Action apply, Action revert)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(revert);
        apply();
        _undo.Push((apply, revert));
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.TryPop(out var edit))
        {
            edit.Revert();
            _redo.Push(edit);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Redo()
    {
        if (_redo.TryPop(out var edit))
        {
            edit.Apply();
            _undo.Push(edit);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
