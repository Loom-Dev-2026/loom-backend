using Loom.Models;
using Loom.Models.Nodes;
using Loom.Services;

namespace Loom.Commands;

// ── Command contract ─────────────────────────────────────────────────────────

public interface ICommand
{
    string Description { get; }
    Task ExecuteAsync();
    Task UndoAsync();
}

// ── Add Node ─────────────────────────────────────────────────────────────────

public class AddNodeCommand : ICommand
{
    private readonly Workflow _workflow;
    private readonly NodeManager _manager;
    private readonly NodeType _type;
    private readonly LoomPoint? _position;
    private Node? _created;

    public string Description => $"Add {_type} node";

    public AddNodeCommand(Workflow workflow, NodeManager manager,
        NodeType type, LoomPoint? position = null)
    {
        _workflow = workflow;
        _manager = manager;
        _type = type;
        _position = position;
    }

    public Task ExecuteAsync()
    {
        _created = _manager.CreateNode(_workflow, _type, _position);
        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        if (_created is not null)
            _manager.DeleteNode(_workflow, _created.NodeId);
        return Task.CompletedTask;
    }

    public Node? CreatedNode => _created;
}

// ── Delete Node ──────────────────────────────────────────────────────────────

public class DeleteNodeCommand : ICommand
{
    private readonly Workflow _workflow;
    private readonly NodeManager _manager;
    private readonly Guid _nodeId;

    private Node? _snapshot;
    private List<Connection> _removedConnections = new();

    public string Description => "Delete node";

    public DeleteNodeCommand(Workflow workflow, NodeManager manager, Guid nodeId)
    {
        _workflow = workflow;
        _manager = manager;
        _nodeId = nodeId;
    }

    public Task ExecuteAsync()
    {
        _snapshot = _workflow.Nodes.FirstOrDefault(n => n.NodeId == _nodeId);
        _removedConnections = _workflow.Connections
            .Where(c => c.SourceNodeId == _nodeId || c.TargetNodeId == _nodeId)
            .ToList();

        _manager.DeleteNode(_workflow, _nodeId);
        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        if (_snapshot is not null)
        {
            _workflow.Nodes.Add(_snapshot);
            _workflow.Connections.AddRange(_removedConnections);
            _workflow.Touch();
        }
        return Task.CompletedTask;
    }
}

// ── Connect Nodes ────────────────────────────────────────────────────────────

public class ConnectNodesCommand : ICommand
{
    private readonly Workflow _workflow;
    private readonly NodeManager _manager;
    private readonly Guid _srcNodeId, _srcPortId, _tgtNodeId, _tgtPortId;
    private Connection? _created;

    public string Description => "Connect nodes";

    public ConnectNodesCommand(Workflow workflow, NodeManager manager,
        Guid srcNodeId, Guid srcPortId, Guid tgtNodeId, Guid tgtPortId)
    {
        _workflow = workflow;
        _manager = manager;
        _srcNodeId = srcNodeId; _srcPortId = srcPortId;
        _tgtNodeId = tgtNodeId; _tgtPortId = tgtPortId;
    }

    public Task ExecuteAsync()
    {
        var (conn, error) = _manager.ConnectNodes(
            _workflow, _srcNodeId, _srcPortId, _tgtNodeId, _tgtPortId);

        if (error is not null)
            throw new InvalidOperationException(error);

        _created = conn;
        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        if (_created is not null)
            _manager.DisconnectNodes(_workflow, _created.ConnectionId);
        return Task.CompletedTask;
    }

    public Connection? CreatedConnection => _created;
}

// ── Disconnect Nodes ─────────────────────────────────────────────────────────

public class DisconnectNodesCommand : ICommand
{
    private readonly Workflow _workflow;
    private readonly NodeManager _manager;
    private readonly Guid _connectionId;
    private Connection? _snapshot;

    public string Description => "Disconnect nodes";

    public DisconnectNodesCommand(Workflow workflow, NodeManager manager, Guid connectionId)
    {
        _workflow = workflow;
        _manager = manager;
        _connectionId = connectionId;
    }

    public Task ExecuteAsync()
    {
        _snapshot = _workflow.Connections.FirstOrDefault(c => c.ConnectionId == _connectionId);
        _manager.DisconnectNodes(_workflow, _connectionId);
        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        if (_snapshot is not null)
        {
            _workflow.Connections.Add(_snapshot);
            _workflow.Touch();
        }
        return Task.CompletedTask;
    }
}

// ── Update Node Config ───────────────────────────────────────────────────────

public class UpdateNodeCommand : ICommand
{
    private readonly Workflow _workflow;
    private readonly NodeManager _manager;
    private readonly Guid _nodeId;
    private readonly Dictionary<string, object?> _newConfig;
    private Dictionary<string, object?> _oldConfig = new();

    public string Description => "Update node config";

    public UpdateNodeCommand(Workflow workflow, NodeManager manager,
        Guid nodeId, Dictionary<string, object?> config)
    {
        _workflow = workflow;
        _manager = manager;
        _nodeId = nodeId;
        _newConfig = config;
    }

    public Task ExecuteAsync()
    {
        var node = _workflow.Nodes.FirstOrDefault(n => n.NodeId == _nodeId);
        if (node is null) return Task.CompletedTask;

        // Snapshot current values for the same keys
        _oldConfig = new Dictionary<string, object?>();
        foreach (var key in _newConfig.Keys)
        {
            var prop = node.GetType().GetProperty(key);
            if (prop?.CanRead == true)
                _oldConfig[key] = prop.GetValue(node);
        }

        _manager.UpdateNode(_workflow, _nodeId, _newConfig);
        return Task.CompletedTask;
    }

    public Task UndoAsync()
    {
        if (_oldConfig.Count > 0)
            _manager.UpdateNode(_workflow, _nodeId, _oldConfig);
        return Task.CompletedTask;
    }
}