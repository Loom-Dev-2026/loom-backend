using System.Collections.Concurrent;
using Loom.Models;

namespace Loom.Services;

public sealed class WorkflowSessionStore
{
    private readonly ConcurrentDictionary<Guid, WorkflowSession> _sessions = new();
    private readonly DataStorage _storage;
    private readonly TimeSpan _idleTimeout = TimeSpan.FromHours(1);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10);
    private DateTime _lastCleanup = DateTime.UtcNow;

    public WorkflowSessionStore(DataStorage storage)
    {
        _storage = storage;
    }

    public WorkflowSession GetOrCreate(Guid sessionId)
    {
        EvictStaleSessions();
        return _sessions.GetOrAdd(sessionId, id =>
        {
            var session = new WorkflowSession
            {
                Workflow = new Workflow("untitled.loom") { SessionId = id }
            };

            var recovered = _storage.LoadRecovery(id);
            if (recovered is not null)
            {
                session.Workflow = recovered;
                RebuildMapsFromWorkflow(session);
                session.StarterGraphApplied = session.Workflow.Nodes.Count > 0;
            }

            return session;
        });
    }

    public void Replace(Guid sessionId, WorkflowSession session)
    {
        EvictStaleSessions();
        session.Workflow.SessionId = sessionId;
        _sessions[sessionId] = session;
    }

    public bool Remove(Guid sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    public async Task PersistAsync(WorkflowSession session)
    {
        await _storage.AutoSaveAsync(session.Workflow);
    }

    private void EvictStaleSessions()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanup) < _cleanupInterval) return;
        _lastCleanup = now;

        var cutoff = now - _idleTimeout;
        var stale = _sessions
            .Where(kv => kv.Value.Workflow.LastModified < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in stale)
        {
            if (_sessions.TryRemove(id, out var session))
                _storage.DeleteRecovery(id);
        }
    }

    private static void RebuildMapsFromWorkflow(WorkflowSession session)
    {
        session.NodeClientToId.Clear();
        session.NodeIdToClient.Clear();
        session.EdgeClientToConnection.Clear();
        session.ConnectionToEdgeClient.Clear();

        var nodeIndex = 1;
        foreach (var node in session.Workflow.Nodes)
        {
            var clientId = nodeIndex.ToString();
            nodeIndex++;
            session.NodeClientToId[clientId] = node.NodeId;
            session.NodeIdToClient[node.NodeId] = clientId;
        }

        session.NextNodeClientNumber = nodeIndex;

        var edgeIndex = 1;
        foreach (var conn in session.Workflow.Connections)
        {
            var clientId = "e" + edgeIndex;
            edgeIndex++;
            session.EdgeClientToConnection[clientId] = conn.ConnectionId;
            session.ConnectionToEdgeClient[conn.ConnectionId] = clientId;
        }

        session.NextEdgeClientNumber = edgeIndex;
    }
}
