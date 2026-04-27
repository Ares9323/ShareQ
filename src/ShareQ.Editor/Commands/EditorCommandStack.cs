using System.Collections.ObjectModel;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Commands;

public sealed class EditorCommandStack
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IEditorCommand command, ObservableCollection<Shape> shapes)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(shapes);
        _undo.Push(command);
        _redo.Clear();
    }

    public bool Undo(ObservableCollection<Shape> shapes)
    {
        if (_undo.Count == 0) return false;
        var cmd = _undo.Pop();
        cmd.Undo(shapes);
        _redo.Push(cmd);
        return true;
    }

    public bool Redo(ObservableCollection<Shape> shapes)
    {
        if (_redo.Count == 0) return false;
        var cmd = _redo.Pop();
        cmd.Apply(shapes);
        _undo.Push(cmd);
        return true;
    }

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Record a replacement that has already been applied to the collection.
    /// Pushes onto undo so Ctrl+Z reverts to <paramref name="oldShape"/>; clears redo (linear history).</summary>
    public void RecordCommittedReplacement(Shape oldShape, Shape newShape)
    {
        ArgumentNullException.ThrowIfNull(oldShape);
        ArgumentNullException.ThrowIfNull(newShape);
        _undo.Push(new ReplaceShapeCommand(oldShape, newShape));
        _redo.Clear();
    }
}
