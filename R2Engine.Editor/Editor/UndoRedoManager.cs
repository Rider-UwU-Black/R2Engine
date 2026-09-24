namespace R2Engine.Editor;

public class UndoRedoManager
{
    private readonly Stack<string> _undoStack =
        new();

    private readonly Stack<string> _redoStack =
        new();

    public bool CanUndo =>
        _undoStack.Count > 0;

    public bool CanRedo =>
        _redoStack.Count > 0;

    // =========================================================
    // Record
    // =========================================================

    public void RecordUndo(
        string sceneSnapshot)
    {
        if (_undoStack.Count > 0 &&
            _undoStack.Peek() ==
            sceneSnapshot)
        {
            return;
        }

        _undoStack.Push(
            sceneSnapshot);

        // Any new edit invalidates redo history.
        _redoStack.Clear();
    }

    // =========================================================
    // Undo
    // =========================================================

    public string? Undo(
        string currentSnapshot)
    {
        if (_undoStack.Count == 0)
            return null;

        string previousSnapshot =
            _undoStack.Pop();

        _redoStack.Push(
            currentSnapshot);

        return previousSnapshot;
    }

    // =========================================================
    // Redo
    // =========================================================

    public string? Redo(
        string currentSnapshot)
    {
        if (_redoStack.Count == 0)
            return null;

        string nextSnapshot =
            _redoStack.Pop();

        _undoStack.Push(
            currentSnapshot);

        return nextSnapshot;
    }

    // =========================================================
    // Reset
    // =========================================================

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}