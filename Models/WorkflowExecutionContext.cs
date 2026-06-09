namespace Loom.Models;

/// <summary>
/// Holds all state for a single execution run of a Workflow.
/// Nodes read upstream outputs from here and write their own result back here.
/// </summary>
public class WorkflowExecutionContext
{
    public Guid ExecutionId { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public ExecStatus Status { get; set; } = ExecStatus.Pending;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public List<ExecutionResult> Results { get; set; } = new();
    public Dictionary<string, object?> Variables { get; set; } = new();

    // Fast lookup by NodeId — rebuilt from Results on load
    private readonly Dictionary<Guid, ExecutionResult> _index = new();

    public WorkflowExecutionContext() { }

    public WorkflowExecutionContext(Guid workflowId)
    {
        WorkflowId = workflowId;
    }

    // ── Results ──────────────────────────────────────────────────────────────

    public void AddResult(ExecutionResult result)
    {
        Results.Add(result);
        _index[result.NodeId] = result;
    }

    public ExecutionResult? GetResult(Guid nodeId)
        => _index.TryGetValue(nodeId, out var r) ? r : null;

    public object? GetNodeOutput(Guid nodeId)
        => GetResult(nodeId)?.OutputValue;

    public bool HasResult(Guid nodeId) => _index.ContainsKey(nodeId);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Complete()
    {
        Status = ExecStatus.Completed;
        EndTime = DateTime.UtcNow;
    }

    public void Fail()
    {
        Status = ExecStatus.Failed;
        EndTime = DateTime.UtcNow;
    }

    public void Rollback()
    {
        Status = ExecStatus.RolledBack;
        Results.Clear();
        _index.Clear();
        EndTime = DateTime.UtcNow;
    }

    // ── Shared variables (used by UserDefinedNode scripts) ───────────────────

    public void SetVariable(string key, object? value) => Variables[key] = value;

    public object? GetVariable(string key)
        => Variables.TryGetValue(key, out var v) ? v : null;

    // ── Rebuild the index after JSON deserialisation ─────────────────────────

    public void RebuildIndex()
    {
        _index.Clear();
        foreach (var r in Results)
            _index[r.NodeId] = r;
    }
}