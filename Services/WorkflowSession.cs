using Loom.Models;

namespace Loom.Services;

/// <summary>
/// Server-side session: authoritative workflow plus stable canvas client identifiers.
/// </summary>
public sealed class WorkflowSession
{
    /// <summary>True after the demo starter graph was applied once for this session (prevents re-seed after clear).</summary>
    public bool StarterGraphApplied { get; set; }

    public Workflow Workflow { get; set; } = new("Untitled Workflow");

    public Dictionary<string, Guid> NodeClientToId { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, string> NodeIdToClient { get; } = new();
    public Dictionary<string, Guid> EdgeClientToConnection { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, string> ConnectionToEdgeClient { get; } = new();

    public int NextNodeClientNumber { get; set; } = 1;
    public int NextEdgeClientNumber { get; set; } = 1;

    public string AllocateNodeClientId()
    {
        var id = NextNodeClientNumber.ToString();
        NextNodeClientNumber++;
        return id;
    }

    public string AllocateEdgeClientId()
    {
        var id = "e" + NextEdgeClientNumber;
        NextEdgeClientNumber++;
        return id;
    }
}
