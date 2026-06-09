using Loom.Models;
using Loom.Models.Nodes;
using System.Diagnostics;

namespace Loom.Services;

/// <summary>
/// Orchestrates a single execution run of a Workflow.
/// Steps:
///   1. Build topological order (detects cycles).
///   2. For each node: propagate upstream port values → Execute → collect result.
///   3. Mark context Complete (or Failed) and return it.
/// </summary>
public class ExecutionEngine
{
    private readonly NodeManager _nodeManager;
    private readonly DataStorage _storage;

    public ExecutionEngine(NodeManager nodeManager, DataStorage storage)
    {
        _nodeManager = nodeManager;
        _storage = storage;
    }

    // ── Main entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full workflow. Returns the completed (or failed) context.
    /// Pass a <paramref name="cancellationToken"/> to allow the caller to abort mid-run.
    /// </summary>
    public async Task<WorkflowExecutionContext> RunAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default)
    {
        var ctx = workflow.CreateExecutionContext();
        ctx.Status = ExecStatus.Running;

        // 1. Topological order (also detects cycles)
        List<Node> ordered;
        try
        {
            ordered = _nodeManager.BuildTopologicalOrder(workflow);
        }
        catch (InvalidOperationException ex)
        {
            ctx.Fail();
            throw new InvalidOperationException(ex.Message, ex);
        }

        // Track which node IDs have errored so we can skip their dependents
        var erroredNodes = new HashSet<Guid>();

        // 2. Execute each node in order
        foreach (var node in ordered)
        {
            // Respect cancellation between nodes (not in the middle of one)
            cancellationToken.ThrowIfCancellationRequested();

            if (!node.IsEnabled)
            {
                node.MarkSkipped();
                ctx.AddResult(ExecutionResult.CreateSkipped(node.NodeId, ctx.ExecutionId));
                continue;
            }

            if (!node.Validate())
            {
                erroredNodes.Add(node.NodeId);
                node.MarkError();
                ctx.AddResult(ExecutionResult.CreateError(
                    node.NodeId, ctx.ExecutionId, $"Node '{node.Label}' failed validation."));
                continue;
            }

            // Check if any upstream node errored
            bool upstreamFailed = workflow.Connections
                .Where(c => c.TargetNodeId == node.NodeId)
                .Any(c => erroredNodes.Contains(c.SourceNodeId));

            if (upstreamFailed)
            {
                node.MarkSkipped();
                ctx.AddResult(ExecutionResult.CreateSkipped(node.NodeId, ctx.ExecutionId));
                erroredNodes.Add(node.NodeId);
                continue;
            }

            // Propagate upstream output port values → this node's input ports
            PropagateInputs(workflow, node, ctx);

            node.MarkRunning();
            var sw = Stopwatch.StartNew();
            try
            {
                var output = await node.Execute(ctx, cancellationToken);
                sw.Stop();
                node.MarkSuccess();

                var result = ExecutionResult.CreateSuccess(
                    node.NodeId, ctx.ExecutionId, output, sw.ElapsedMilliseconds);
                ctx.AddResult(result);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                node.MarkError();
                ctx.Fail();

                var result = ExecutionResult.CreateError(
                    node.NodeId, ctx.ExecutionId,
                    "Execution was cancelled by the caller.");
                ctx.AddResult(result);

                // Re-throw so the caller knows the run was cancelled
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                node.MarkError();
                erroredNodes.Add(node.NodeId);

                var result = ExecutionResult.CreateError(
                    node.NodeId, ctx.ExecutionId,
                    ex.Message, ex.StackTrace);
                ctx.AddResult(result);
            }
        }

        // 3. Finalise — only mark complete if not already failed/cancelled
        if (ctx.Status == ExecStatus.Running)
            ctx.Complete();

        // 4. Persist the run (best-effort)
        try { await _storage.SaveExecutionAsync(workflow, ctx); }
        catch { /* persistence failure must not abort the in-memory results */ }

        return ctx;
    }

    // ── Propagate values from upstream output ports into this node's inputs ──

    private static void PropagateInputs(
        Workflow workflow,
        Node node,
        WorkflowExecutionContext ctx)
    {
        foreach (var conn in workflow.Connections
                     .Where(c => c.TargetNodeId == node.NodeId))
        {
            var srcNode = workflow.Nodes.FirstOrDefault(n => n.NodeId == conn.SourceNodeId);
            if (srcNode is null) continue;

            var srcPort = ResolveOutputPort(srcNode, conn.SourcePortId);
            var tgtPort = ResolveInputPort(node, conn.TargetPortId, srcPort?.Name);
            if (srcPort is null || tgtPort is null) continue;

            // Use the port's live value if the node was just executed;
            // fall back to the stored execution result output
            var value = srcPort.GetValue() ?? ctx.GetNodeOutput(conn.SourceNodeId);
            tgtPort.SetValue(value);
        }
    }

    private static Port? ResolveOutputPort(Node node, Guid portId)
    {
        var ports = node.OutputPorts;
        var match = ports.FirstOrDefault(p => p.PortId == portId);
        if (match is not null) return match;

        return ports.FirstOrDefault(p => p.Name.Equals("Result", StringComparison.OrdinalIgnoreCase))
            ?? ports.FirstOrDefault(p => p.Name.Equals("Value", StringComparison.OrdinalIgnoreCase))
            ?? (ports.Count == 1 ? ports[0] : null);
    }

    private static Port? ResolveInputPort(Node node, Guid portId, string? upstreamPortName = null)
    {
        var ports = node.InputPorts;
        var match = ports.FirstOrDefault(p => p.PortId == portId);
        if (match is not null) return match;

        if (!string.IsNullOrWhiteSpace(upstreamPortName))
        {
            match = ports.FirstOrDefault(p =>
                p.Name.Equals(upstreamPortName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return ports.Count == 1 ? ports[0] : null;
    }
}