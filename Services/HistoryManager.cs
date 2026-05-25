using Loom.Commands;

namespace Loom.Services;

/// <summary>
/// Maintains an undo / redo stack of ICommand objects.
/// Every CRUD action on a workflow should go through ExecuteAsync()
/// so it is recorded and reversible.
/// </summary>
public class HistoryManager
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public string? NextUndoDescription
        => _undoStack.TryPeek(out var cmd) ? cmd.Description : null;

    public string? NextRedoDescription
        => _redoStack.TryPeek(out var cmd) ? cmd.Description : null;

    // ── Execute (records for undo) ───────────────────────────────────────────

    public async Task ExecuteAsync(ICommand command)
    {
        await command.ExecuteAsync();
        _undoStack.Push(command);
        _redoStack.Clear(); // new action clears redo history
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    public async Task UndoAsync()
    {
        if (!CanUndo) return;
        var command = _undoStack.Pop();
        await command.UndoAsync();
        _redoStack.Push(command);
    }

    // ── Redo ─────────────────────────────────────────────────────────────────

    public async Task RedoAsync()
    {
        if (!CanRedo) return;
        var command = _redoStack.Pop();
        await command.ExecuteAsync();
        _undoStack.Push(command);
    }

    // ── Management ───────────────────────────────────────────────────────────

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public IEnumerable<string> GetUndoHistory()
        => _undoStack.Select(c => c.Description);

    public IEnumerable<string> GetRedoHistory()
        => _redoStack.Select(c => c.Description);
}