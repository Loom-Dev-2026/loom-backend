using Loom.Commands;
using Loom.Models;
using Loom.Models.Nodes;

namespace Loom.Services;

/// <summary>
/// High-level service consumed by Blazor components.
/// Provides a single entry point that coordinates the active Workflow,
/// NodeManager, ExecutionEngine, HistoryManager, and DataStorage.
/// </summary>
public class WorkflowService
{
    private readonly NodeManager _nodeManager;
    private readonly ExecutionEngine _engine;
    private readonly DataStorage _storage;
    private readonly HistoryManager _history;

    public Workflow? ActiveWorkflow { get; private set; }
    public WorkflowExecutionContext? LastExecution { get; private set; }

    // Blazor components subscribe to this to trigger StateHasChanged
    public event Action? WorkflowChanged;
    public event Action? ExecutionCompleted;

    public WorkflowService(NodeManager nodeManager, ExecutionEngine engine,
        DataStorage storage, HistoryManager history)
    {
        _nodeManager = nodeManager;
        _engine = engine;
        _storage = storage;
        _history = history;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void NewWorkflow(string name = "Untitled Workflow")
    {
        ActiveWorkflow = new Workflow(name);
        _history.Clear();
        LastExecution = null;
        WorkflowChanged?.Invoke();
    }

    public async Task<bool> SaveWorkflowAsync(string path)
    {
        if (ActiveWorkflow is null) return false;
        var ok = await _storage.SaveAsync(ActiveWorkflow, path);
        if (ok) WorkflowChanged?.Invoke();
        return ok;
    }

    public async Task<bool> LoadWorkflowAsync(string path)
    {
        var wf = await _storage.LoadAsync(path);
        if (wf is null) return false;
        ActiveWorkflow = wf;
        _history.Clear();
        LastExecution = null;
        WorkflowChanged?.Invoke();
        return true;
    }

    // ── Node CRUD (via HistoryManager) ───────────────────────────────────────

    public async Task<Node?> AddNodeAsync(NodeType type, LoomPoint? position = null)
    {
        if (ActiveWorkflow is null) return null;
        var cmd = new AddNodeCommand(ActiveWorkflow, _nodeManager, type, position);
        await _history.ExecuteAsync(cmd);
        WorkflowChanged?.Invoke();
        return cmd.CreatedNode;
    }

    public async Task<bool> DeleteNodeAsync(Guid nodeId)
    {
        if (ActiveWorkflow is null) return false;
        var cmd = new DeleteNodeCommand(ActiveWorkflow, _nodeManager, nodeId);
        await _history.ExecuteAsync(cmd);
        WorkflowChanged?.Invoke();
        return true;
    }

    public async Task<bool> UpdateNodeAsync(Guid nodeId, Dictionary<string, object?> config)
    {
        if (ActiveWorkflow is null) return false;
        var cmd = new UpdateNodeCommand(ActiveWorkflow, _nodeManager, nodeId, config);
        await _history.ExecuteAsync(cmd);
        WorkflowChanged?.Invoke();
        return true;
    }

    public Node? GetNode(Guid nodeId)
        => ActiveWorkflow is null ? null : _nodeManager.ReadNode(ActiveWorkflow, nodeId);

    // ── Connections ──────────────────────────────────────────────────────────

    public async Task<(Connection? conn, string? error)> ConnectNodesAsync(
        Guid srcNodeId, Guid srcPortId, Guid tgtNodeId, Guid tgtPortId)
    {
        if (ActiveWorkflow is null) return (null, "No active workflow.");
        var cmd = new ConnectNodesCommand(ActiveWorkflow, _nodeManager,
            srcNodeId, srcPortId, tgtNodeId, tgtPortId);
        try
        {
            await _history.ExecuteAsync(cmd);
            WorkflowChanged?.Invoke();
            return (cmd.CreatedConnection, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<bool> DisconnectAsync(Guid connectionId)
    {
        if (ActiveWorkflow is null) return false;
        var cmd = new DisconnectNodesCommand(ActiveWorkflow, _nodeManager, connectionId);
        await _history.ExecuteAsync(cmd);
        WorkflowChanged?.Invoke();
        return true;
    }

    // ── Execution ────────────────────────────────────────────────────────────

    public async Task<WorkflowExecutionContext?> ExecuteWorkflowAsync()
    {
        if (ActiveWorkflow is null) return null;
        LastExecution = await _engine.RunAsync(ActiveWorkflow);
        ExecutionCompleted?.Invoke();
        WorkflowChanged?.Invoke();
        return LastExecution;
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────────

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public async Task UndoAsync()
    {
        await _history.UndoAsync();
        WorkflowChanged?.Invoke();
    }

    public async Task RedoAsync()
    {
        await _history.RedoAsync();
        WorkflowChanged?.Invoke();
    }

    // ── Auto-save ────────────────────────────────────────────────────────────

    public async Task AutoSaveAsync()
    {
        if (ActiveWorkflow is not null)
            await _storage.AutoSaveAsync(ActiveWorkflow);
    }

    // ── Execution history ─────────────────────────────────────────────────────

    public List<WorkflowExecutionContext> GetExecutionHistory()
    {
        if (ActiveWorkflow is null) return new();
        return _storage.LoadExecutionHistory(ActiveWorkflow.SessionId);
    }

    // ── Node library ─────────────────────────────────────────────────────────

    public IReadOnlyList<NodeType> GetAvailableNodeTypes()
        => Enum.GetValues<NodeType>().ToList();
}