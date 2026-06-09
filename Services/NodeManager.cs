using Loom.Models;
using Loom.Models.Nodes;

namespace Loom.Services;

/// <summary>
/// Mediates all CRUD operations on Nodes and Connections.
/// Maintains topological order and detects cycles using DFS.
/// </summary>
public class NodeManager
{
    private readonly NodeFactory _factory;

    public NodeManager(NodeFactory factory)
    {
        _factory = factory;
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public Node CreateNode(Workflow workflow, NodeType type,
        LoomPoint? position = null, Dictionary<string, object?>? config = null,
        string? canvasType = null)
    {
        var node = _factory.Create(type, canvasType);
        node.Position = position ?? new LoomPoint(100, 100);

        if (config is not null)
            ApplyConfig(node, config);

        // Stamp all port NodeIds (they're generated before the node was registered)
        foreach (var p in node.InputPorts.Concat(node.OutputPorts))
            p.NodeId = node.NodeId;

        workflow.Nodes.Add(node);
        workflow.Touch();
        return node;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public Node? ReadNode(Workflow workflow, Guid nodeId)
        => workflow.Nodes.FirstOrDefault(n => n.NodeId == nodeId);

    // ── Update ───────────────────────────────────────────────────────────────

    public bool UpdateNode(Workflow workflow, Guid nodeId,
        Dictionary<string, object?> config)
    {
        var node = ReadNode(workflow, nodeId);
        if (node is null) return false;

        ApplyConfig(node, config);

        // Invalidate downstream nodes
        MarkDownstreamDirty(workflow, nodeId);
        workflow.Touch();
        return true;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public bool DeleteNode(Workflow workflow, Guid nodeId)
    {
        var node = ReadNode(workflow, nodeId);
        if (node is null) return false;

        // Remove all connections touching this node
        workflow.Connections.RemoveAll(c =>
            c.SourceNodeId == nodeId || c.TargetNodeId == nodeId);

        workflow.Nodes.Remove(node);
        workflow.Touch();
        return true;
    }

    // ── Connect ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a typed edge from an output port to an input port.
    /// Validates type compatibility and acyclicity before adding.
    /// </summary>
    public (Connection? connection, string? error) ConnectNodes(
        Workflow workflow,
        Guid sourceNodeId, Guid sourcePortId,
        Guid targetNodeId, Guid targetPortId)
    {
        // Retrieve nodes
        var srcNode = ReadNode(workflow, sourceNodeId);
        var tgtNode = ReadNode(workflow, targetNodeId);
        if (srcNode is null) return (null, "Source node not found.");
        if (tgtNode is null) return (null, "Target node not found.");

        // Retrieve ports
        var srcPort = srcNode.OutputPorts.FirstOrDefault(p => p.PortId == sourcePortId);
        var tgtPort = tgtNode.InputPorts.FirstOrDefault(p => p.PortId == targetPortId);
        if (srcPort is null) return (null, "Source output port not found.");
        if (tgtPort is null) return (null, "Target input port not found.");

        if (sourceNodeId == targetNodeId)
            return (null, "Cannot connect a node to itself.");

        // Type compatibility
        if (!srcPort.IsCompatibleWith(tgtPort))
            return (null, $"Type mismatch: '{srcPort.DataType}' → '{tgtPort.DataType}'.");

        if (workflow.Connections.Any(c =>
                c.SourceNodeId == sourceNodeId && c.SourcePortId == sourcePortId &&
                c.TargetNodeId == targetNodeId && c.TargetPortId == targetPortId))
            return (null, "Connection already exists.");

        if (workflow.Connections.Any(c =>
                c.TargetNodeId == targetNodeId && c.TargetPortId == targetPortId))
            return (null, $"Input port '{tgtPort.Name}' already has a connection.");

        // Build tentative connection and check for cycles
        var conn = new Connection(sourceNodeId, sourcePortId, targetNodeId, targetPortId)
        {
            DataType = srcPort.DataType
        };
        conn.Validate(srcPort, tgtPort);

        workflow.Connections.Add(conn);

        if (DetectCycles(workflow))
        {
            workflow.Connections.Remove(conn);
            return (null, "Connection would introduce a cycle.");
        }

        srcPort.IsConnected = true;
        tgtPort.IsConnected = true;
        workflow.Touch();
        return (conn, null);
    }

    /// <summary>Dry-run connection validation without mutating the workflow.</summary>
    public (bool valid, string? error) TryValidateConnection(
        Workflow workflow,
        Guid sourceNodeId, Guid sourcePortId,
        Guid targetNodeId, Guid targetPortId)
    {
        var (conn, error) = ConnectNodes(workflow, sourceNodeId, sourcePortId, targetNodeId, targetPortId);
        if (conn is null)
            return (false, error);

        workflow.Connections.Remove(conn);
        UpdatePortConnectedState(workflow, conn.SourceNodeId, conn.SourcePortId, isOutput: true);
        UpdatePortConnectedState(workflow, conn.TargetNodeId, conn.TargetPortId, isOutput: false);
        return (true, null);
    }

    public bool DisconnectNodes(Workflow workflow, Guid connectionId)
    {
        var conn = workflow.Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (conn is null) return false;

        // Clear IsConnected flags if no other connections share the port
        UpdatePortConnectedState(workflow, conn.SourceNodeId, conn.SourcePortId, isOutput: true);
        UpdatePortConnectedState(workflow, conn.TargetNodeId, conn.TargetPortId, isOutput: false);

        workflow.Connections.Remove(conn);
        workflow.Touch();
        return true;
    }

    // ── Topological order (Kahn's algorithm) ─────────────────────────────────

    /// <summary>Returns nodes in topological order, or throws if a cycle exists.</summary>
    public List<Node> BuildTopologicalOrder(Workflow workflow)
    {
        var inDegree = workflow.Nodes.ToDictionary(n => n.NodeId, _ => 0);

        foreach (var c in workflow.Connections)
            if (inDegree.ContainsKey(c.TargetNodeId))
                inDegree[c.TargetNodeId]++;

        var queue = new Queue<Node>(
            workflow.Nodes.Where(n => inDegree[n.NodeId] == 0));

        var result = new List<Node>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            foreach (var conn in workflow.Connections
                         .Where(c => c.SourceNodeId == node.NodeId))
            {
                if (!inDegree.ContainsKey(conn.TargetNodeId)) continue;
                if (--inDegree[conn.TargetNodeId] == 0)
                {
                    var tgt = ReadNode(workflow, conn.TargetNodeId);
                    if (tgt is not null) queue.Enqueue(tgt);
                }
            }
        }

        if (result.Count != workflow.Nodes.Count)
            throw new InvalidOperationException(
                "Workflow contains a cycle. Execute aborted.");

        return result;
    }

    // ── Cycle detection (DFS coloring) ───────────────────────────────────────

    /// <summary>Returns true if the workflow's connection graph contains a cycle.</summary>
    public bool DetectCycles(Workflow workflow)
    {
        // 0 = white (unvisited), 1 = gray (in stack), 2 = black (done)
        var color = workflow.Nodes.ToDictionary(n => n.NodeId, _ => 0);

        bool Dfs(Guid id)
        {
            color[id] = 1;
            foreach (var conn in workflow.Connections.Where(c => c.SourceNodeId == id))
            {
                if (!color.ContainsKey(conn.TargetNodeId)) continue;
                if (color[conn.TargetNodeId] == 1) return true;   // back edge → cycle
                if (color[conn.TargetNodeId] == 0 && Dfs(conn.TargetNodeId)) return true;
            }
            color[id] = 2;
            return false;
        }

        return workflow.Nodes
            .Where(n => color[n.NodeId] == 0)
            .Any(n => Dfs(n.NodeId));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void ApplyConfig(Node node, Dictionary<string, object?> config)
    {
        foreach (var (key, value) in config)
        {
            var prop = node.GetType().GetProperty(key);
            if (prop is null || !prop.CanWrite) continue;
            try
            {
                var converted = value is null
                    ? null
                    : Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(node, converted);
            }
            catch { /* silently skip type-incompatible configs */ }
        }
        node.MarkDirty();
    }

    private static void MarkDownstreamDirty(Workflow workflow, Guid nodeId)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(nodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var conn in workflow.Connections
                         .Where(c => c.SourceNodeId == current))
            {
                if (visited.Add(conn.TargetNodeId))
                {
                    var tgt = workflow.Nodes.FirstOrDefault(n => n.NodeId == conn.TargetNodeId);
                    tgt?.MarkDirty();
                    queue.Enqueue(conn.TargetNodeId);
                }
            }
        }
    }

    private static void UpdatePortConnectedState(Workflow workflow,
        Guid nodeId, Guid portId, bool isOutput)
    {
        var node = workflow.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is null) return;

        var ports = isOutput ? node.OutputPorts : node.InputPorts;
        var port = ports.FirstOrDefault(p => p.PortId == portId);
        if (port is null) return;

        bool stillConnected = workflow.Connections.Any(c =>
            isOutput
                ? c.SourceNodeId == nodeId && c.SourcePortId == portId
                : c.TargetNodeId == nodeId && c.TargetPortId == portId);

        port.IsConnected = stillConnected;
    }
}