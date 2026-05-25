using Loom.Models;
using static System.Net.Mime.MediaTypeNames;

namespace Loom.Models.Nodes;

/// <summary>
/// Abstract base for every node type in the graph.
/// Concrete subclasses implement Execute(), Validate(), and GetOutput().
/// </summary>
public abstract class Node
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
    public NodeType Type { get; protected set; }
    public LoomPoint Position { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public ExecState ExecutionState { get; set; } = ExecState.Idle;
    public List<Port> InputPorts { get; set; } = new();
    public List<Port> OutputPorts { get; set; } = new();
    public string Label { get; set; } = string.Empty;

    // ── Abstract contract ────────────────────────────────────────────────────

    /// <summary>
    /// Executes the node's logic using the shared execution context.
    /// Implementations should respect the cancellation token for long-running operations.
    /// </summary>
    public abstract Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true if the node's current configuration is valid.</summary>
    public abstract bool Validate();

    /// <summary>Returns the last computed output value (null before first execution).</summary>
    public abstract object? GetOutput();

    // ── Port helpers ─────────────────────────────────────────────────────────

    protected Port AddInputPort(string name, string dataType = "object")
    {
        var port = new Port(name, PortDirection.Input, dataType) { NodeId = NodeId };
        InputPorts.Add(port);
        return port;
    }

    protected Port AddOutputPort(string name, string dataType = "object")
    {
        var port = new Port(name, PortDirection.Output, dataType) { NodeId = NodeId };
        OutputPorts.Add(port);
        return port;
    }

    public Port? GetInputPort(string name)
        => InputPorts.FirstOrDefault(p => p.Name == name);

    public Port? GetOutputPort(string name)
        => OutputPorts.FirstOrDefault(p => p.Name == name);

    // ── State helpers ────────────────────────────────────────────────────────

    public void MarkDirty() => ExecutionState = ExecState.Dirty;
    public void MarkIdle() => ExecutionState = ExecState.Idle;
    public void MarkRunning() => ExecutionState = ExecState.Running;
    public void MarkSuccess() => ExecutionState = ExecState.Success;
    public void MarkError() => ExecutionState = ExecState.Error;
    public void MarkSkipped() => ExecutionState = ExecState.Skipped;

    // ── Utility helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Safely reads a typed value from an input port, returning the default if
    /// the port is missing or the value cannot be cast.
    /// </summary>
    protected T? GetInputValue<T>(string portName)
    {
        var raw = GetInputPort(portName)?.GetValue();
        if (raw is null) return default;
        if (raw is T typed) return typed;

        try { return (T)Convert.ChangeType(raw, typeof(T)); }
        catch { return default; }
    }

    /// <summary>Writes a value to a named output port (no-op if port not found).</summary>
    protected void SetOutputValue(string portName, object? value)
        => GetOutputPort(portName)?.SetValue(value);
}
