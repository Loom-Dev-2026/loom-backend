using System.Diagnostics;
using Loom.Api;
using Loom.Api.Models;
using Loom.Models;
using Loom.Models.Nodes;

namespace Loom.Services;

/// <summary>
/// Authoritative graph operations for canvas sessions (single source of truth).
/// </summary>
public sealed class WorkflowGraphService
{
    private readonly WorkflowSessionStore _sessions;
    private readonly NodeFactory _factory;
    private readonly NodeManager _nodeManager;
    private readonly ExecutionEngine _engine;
    private readonly DataStorage _storage;

    public WorkflowGraphService(
        WorkflowSessionStore sessions,
        NodeFactory factory,
        NodeManager nodeManager,
        ExecutionEngine engine,
        DataStorage storage)
    {
        _sessions = sessions;
        _factory = factory;
        _nodeManager = nodeManager;
        _engine = engine;
        _storage = storage;
    }

    public CanvasWorkflowDto GetWorkflow(Guid sessionId)
    {
        var session = EnsureSession(sessionId);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<CanvasWorkflowDto> ReplaceWorkflowAsync(Guid sessionId, CanvasWorkflowDto dto)
    {
        var session = BuildSessionFromDto(sessionId, dto);
        session.StarterGraphApplied = true;
        _sessions.Replace(sessionId, session);
        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<CanvasWorkflowDto> AddNodeAsync(
        Guid sessionId, string type, double x, double y)
    {
        var session = EnsureSession(sessionId);
        if (!CanvasNodeCatalog.TryMapType(type, out var nodeType))
            throw new InvalidOperationException($"Unknown node type: {type}");

        var clientId = session.AllocateNodeClientId();
        var node = _nodeManager.CreateNode(session.Workflow, nodeType, new LoomPoint(x, y), canvasType: type);
        node.Label = type;

        var def = CanvasNodeCatalog.GetDefinitions().FirstOrDefault(d => d.Type == type);
        if (def is not null)
        {
            var fields = def.Fields.ToDictionary(f => f.Key, f => f.Default);
            CanvasWorkflowMapper.ApplyFieldsToNode(node, fields);
        }

        session.NodeClientToId[clientId] = node.NodeId;
        session.NodeIdToClient[node.NodeId] = clientId;

        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<CanvasWorkflowDto> UpdateNodeAsync(
        Guid sessionId,
        string clientNodeId,
        double? x,
        double? y,
        Dictionary<string, string>? fields)
    {
        var session = EnsureSession(sessionId);
        if (!session.NodeClientToId.TryGetValue(clientNodeId, out var nodeId))
            throw new InvalidOperationException("Node not found.");

        var node = _nodeManager.ReadNode(session.Workflow, nodeId);
        if (node is null)
            throw new InvalidOperationException("Node not found.");

        if (x.HasValue || y.HasValue)
        {
            node.Position = new LoomPoint(
                x ?? node.Position.X,
                y ?? node.Position.Y);
            session.Workflow.Touch();
        }

        if (fields is not null)
            CanvasWorkflowMapper.ApplyFieldsToNode(node, fields);

        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<CanvasWorkflowDto> DeleteNodeAsync(Guid sessionId, string clientNodeId)
    {
        var session = EnsureSession(sessionId);
        if (!session.NodeClientToId.TryGetValue(clientNodeId, out var nodeId))
            throw new InvalidOperationException("Node not found.");

        _nodeManager.DeleteNode(session.Workflow, nodeId);
        session.NodeClientToId.Remove(clientNodeId);
        session.NodeIdToClient.Remove(nodeId);

        foreach (var conn in session.Workflow.Connections
                     .Where(c => c.SourceNodeId == nodeId || c.TargetNodeId == nodeId)
                     .ToList())
        {
            if (session.ConnectionToEdgeClient.TryGetValue(conn.ConnectionId, out var edgeClient))
            {
                session.EdgeClientToConnection.Remove(edgeClient);
                session.ConnectionToEdgeClient.Remove(conn.ConnectionId);
            }
        }

        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public ConnectionValidationResultDto ValidateConnection(
        Guid sessionId,
        string fromClientId,
        string toClientId,
        string fromPort,
        string toPort)
    {
        var session = EnsureSession(sessionId);
        if (!TryResolvePorts(session, fromClientId, toClientId, fromPort, toPort,
                out var srcId, out var tgtId, out var srcPort, out var tgtPort, out var error))
        {
            return new ConnectionValidationResultDto { Valid = false, Error = error };
        }

        var (valid, validateError) = _nodeManager.TryValidateConnection(
            session.Workflow, srcId, srcPort!.PortId, tgtId, tgtPort!.PortId);

        return new ConnectionValidationResultDto
        {
            Valid = valid,
            Error = validateError
        };
    }

    public async Task<CanvasWorkflowDto> ConnectNodesAsync(
        Guid sessionId,
        string fromClientId,
        string toClientId,
        string fromPort,
        string toPort)
    {
        var session = EnsureSession(sessionId);
        if (!TryResolvePorts(session, fromClientId, toClientId, fromPort, toPort,
                out var srcId, out var tgtId, out var srcPort, out var tgtPort, out var error))
            throw new InvalidOperationException(error);

        var (conn, connectError) = _nodeManager.ConnectNodes(
            session.Workflow, srcId, srcPort!.PortId, tgtId, tgtPort!.PortId);

        if (conn is null)
            throw new InvalidOperationException(connectError ?? "Connection failed.");

        var edgeClient = session.AllocateEdgeClientId();
        session.EdgeClientToConnection[edgeClient] = conn.ConnectionId;
        session.ConnectionToEdgeClient[conn.ConnectionId] = edgeClient;

        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<CanvasWorkflowDto> DisconnectEdgeAsync(Guid sessionId, string edgeClientId)
    {
        var session = EnsureSession(sessionId);
        if (!session.EdgeClientToConnection.TryGetValue(edgeClientId, out var connId))
            throw new InvalidOperationException("Edge not found.");

        _nodeManager.DisconnectNodes(session.Workflow, connId);
        session.EdgeClientToConnection.Remove(edgeClientId);
        session.ConnectionToEdgeClient.Remove(connId);

        await _sessions.PersistAsync(session);
        return CanvasWorkflowMapper.ToCanvasDto(session);
    }

    public async Task<WorkflowExecutionResponseDto> ExecuteAsync(Guid sessionId)
    {
        var session = EnsureSession(sessionId);
        var graphCheck = WorkflowGraphValidator.Validate(session.Workflow, _nodeManager);
        if (!graphCheck.IsValid)
        {
            return new WorkflowExecutionResponseDto
            {
                Success = false,
                Error = graphCheck.Error,
                Status = ExecStatus.Failed.ToString()
            };
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var ctx = await _engine.RunAsync(session.Workflow);
            sw.Stop();

            await _sessions.PersistAsync(session);
            var dto = CanvasWorkflowMapper.ToCanvasDto(session);
            return CanvasWorkflowMapper.ToExecutionResponse(
                dto, session.Workflow, ctx, session.NodeClientToId, sw.ElapsedMilliseconds);
        }
        catch (InvalidOperationException ex)
        {
            return new WorkflowExecutionResponseDto
            {
                Success = false,
                Error = ex.Message,
                Status = ExecStatus.Failed.ToString()
            };
        }
    }

    public ExportCSharpResponseDto ExportCSharp(Guid sessionId)
    {
        var session = EnsureSession(sessionId);
        var graphCheck = WorkflowGraphValidator.Validate(session.Workflow, _nodeManager);
        if (!graphCheck.IsValid)
            throw new InvalidOperationException(graphCheck.Error ?? "Graph validation failed.");

        var code = CSharpWorkflowExporter.Export(session.Workflow, session);
        var safeName = SanitizeFileName(session.Workflow.WfName);
        return new ExportCSharpResponseDto
        {
            FileName = $"{safeName}Runner.cs",
            SourceCode = code
        };
    }

    public async Task<bool> SaveToPathAsync(Guid sessionId, string path, string? username = null)
    {
        var session = EnsureSession(sessionId);
        var resolved = _storage.ResolveWorkflowPath(path, username);
        session.Workflow.WfName = Path.GetFileName(resolved);
        return await _storage.SaveAsync(session.Workflow, resolved);
    }

    public IReadOnlyList<string> ListSavedWorkflows(string? username = null)
        => _storage.ListWorkflowFiles(username)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool DeleteSavedWorkflow(string path, string? username = null)
        => _storage.DeleteWorkflowFile(path, username);

    public async Task<CanvasWorkflowDto> LoadFromPathAsync(Guid sessionId, string path, string? username = null)
    {
        var resolved = _storage.ResolveWorkflowPath(path, username);
        var workflow = await _storage.LoadAsync(resolved);
        if (workflow is null)
            throw new InvalidOperationException($"Workflow not found: {Path.GetFileName(resolved)}");

        workflow.SessionId = sessionId;
        workflow.WfName = Path.GetFileName(resolved);
        var dto = WorkflowToCanvasDto(workflow);
        return await ReplaceWorkflowAsync(sessionId, dto);
    }

    private WorkflowSession EnsureSession(Guid sessionId)
    {
        var session = _sessions.GetOrCreate(sessionId);
        if (!session.StarterGraphApplied)
        {
            if (string.IsNullOrWhiteSpace(session.Workflow.WfName)
                || session.Workflow.WfName.Equals("Untitled Workflow", StringComparison.OrdinalIgnoreCase))
            {
                session.Workflow.WfName = "untitled.loom";
            }

            session.StarterGraphApplied = true;
        }

        CanvasWorkflowMapper.NormalizeLegacyNodeLabels(session.Workflow);
        return session;
    }

    private WorkflowSession BuildSessionFromDto(Guid sessionId, CanvasWorkflowDto dto)
    {
        var (workflow, idMap) = CanvasWorkflowMapper.ToWorkflow(dto, _factory, _nodeManager);
        workflow.SessionId = sessionId;
        if (!string.IsNullOrWhiteSpace(dto.Name))
            workflow.WfName = dto.Name;

        var session = new WorkflowSession { Workflow = workflow };
        foreach (var (clientId, nodeId) in idMap)
        {
            session.NodeClientToId[clientId] = nodeId;
            session.NodeIdToClient[nodeId] = clientId;
            if (int.TryParse(clientId, out var n) && n >= session.NextNodeClientNumber)
                session.NextNodeClientNumber = n + 1;
        }

        foreach (var edge in dto.Edges)
        {
            if (!idMap.TryGetValue(edge.From, out var src) ||
                !idMap.TryGetValue(edge.To, out var tgt))
                continue;

            var srcNode = workflow.Nodes.First(n => n.NodeId == src);
            var tgtNode = workflow.Nodes.First(n => n.NodeId == tgt);
            var fromPortName = string.IsNullOrWhiteSpace(edge.FromPort)
                ? CanvasPortResolver.DefaultOutputPort(CanvasNodeCatalog.ResolveCanvasType(srcNode))
                : edge.FromPort;
            var toPortName = string.IsNullOrWhiteSpace(edge.ToPort)
                ? CanvasPortResolver.DefaultInputPort(CanvasNodeCatalog.ResolveCanvasType(tgtNode))
                : edge.ToPort;
            var srcPort = CanvasPortResolver.FindOutputPort(srcNode, fromPortName);
            var tgtPort = CanvasPortResolver.FindInputPort(tgtNode, toPortName);
            if (srcPort is null || tgtPort is null) continue;

            var conn = workflow.Connections.FirstOrDefault(c =>
                c.SourceNodeId == src && c.TargetNodeId == tgt &&
                c.SourcePortId == srcPort.PortId && c.TargetPortId == tgtPort.PortId);
            if (conn is null) continue;

            session.EdgeClientToConnection[edge.Id] = conn.ConnectionId;
            session.ConnectionToEdgeClient[conn.ConnectionId] = edge.Id;
            if (edge.Id.StartsWith('e') && int.TryParse(edge.Id[1..], out var e) &&
                e >= session.NextEdgeClientNumber)
                session.NextEdgeClientNumber = e + 1;
        }

        return session;
    }

    private static CanvasWorkflowDto WorkflowToCanvasDto(Workflow workflow)
    {
        var nodeIdToClient = new Dictionary<Guid, string>();
        var nodes = new List<CanvasNodeDto>();
        var nodeIndex = 1;
        foreach (var node in workflow.Nodes)
        {
            var clientId = nodeIndex.ToString();
            nodeIndex++;
            nodeIdToClient[node.NodeId] = clientId;
            nodes.Add(new CanvasNodeDto
            {
                Id = clientId,
                Type = CanvasNodeCatalog.ResolveCanvasType(node),
                X = node.Position.X,
                Y = node.Position.Y,
                Fields = CanvasWorkflowMapper.ExtractFields(node)
            });
        }

        var nodeLookup = workflow.Nodes.ToDictionary(n => n.NodeId);
        var edges = new List<CanvasEdgeDto>();
        var edgeIndex = 1;
        foreach (var conn in workflow.Connections)
        {
            if (!nodeIdToClient.TryGetValue(conn.SourceNodeId, out var from)
                || !nodeIdToClient.TryGetValue(conn.TargetNodeId, out var to))
                continue;

            string fromPort = string.Empty;
            string toPort = string.Empty;
            if (nodeLookup.TryGetValue(conn.SourceNodeId, out var srcNode))
            {
                var srcPort = srcNode.OutputPorts.FirstOrDefault(p => p.PortId == conn.SourcePortId);
                fromPort = srcPort?.Name ?? string.Empty;
            }

            if (nodeLookup.TryGetValue(conn.TargetNodeId, out var tgtNode))
            {
                var tgtPort = tgtNode.InputPorts.FirstOrDefault(p => p.PortId == conn.TargetPortId);
                toPort = tgtPort?.Name ?? string.Empty;
            }

            edges.Add(new CanvasEdgeDto
            {
                Id = "e" + edgeIndex,
                From = from,
                To = to,
                FromPort = fromPort,
                ToPort = toPort
            });
            edgeIndex++;
        }

        return new CanvasWorkflowDto
        {
            SessionId = workflow.SessionId,
            Name = workflow.WfName,
            Nodes = nodes,
            Edges = edges
        };
    }

    private static bool TryResolvePorts(
        WorkflowSession session,
        string fromClientId,
        string toClientId,
        string fromPort,
        string toPort,
        out Guid srcId,
        out Guid tgtId,
        out Port? srcPort,
        out Port? tgtPort,
        out string error)
    {
        srcId = Guid.Empty;
        tgtId = Guid.Empty;
        srcPort = null;
        tgtPort = null;
        error = string.Empty;

        if (!session.NodeClientToId.TryGetValue(fromClientId, out srcId) ||
            !session.NodeClientToId.TryGetValue(toClientId, out tgtId))
        {
            error = "Node not found.";
            return false;
        }

        var sourceId = srcId;
        var targetId = tgtId;
        var srcNode = session.Workflow.Nodes.FirstOrDefault(n => n.NodeId == sourceId);
        var tgtNode = session.Workflow.Nodes.FirstOrDefault(n => n.NodeId == targetId);
        if (srcNode is null || tgtNode is null)
        {
            error = "Node not found.";
            return false;
        }

        srcPort = CanvasPortResolver.FindOutputPort(srcNode, fromPort);
        tgtPort = CanvasPortResolver.FindInputPort(tgtNode, toPort);

        if (srcPort is null)
        {
            error = $"Output port '{fromPort}' not found on source node.";
            return false;
        }

        if (tgtPort is null)
        {
            error = $"Input port '{toPort}' not found on target node.";
            return false;
        }

        return true;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "workflow" : cleaned.Replace(".loom", "", StringComparison.OrdinalIgnoreCase);
    }
}
