using Loom.Models.Nodes;
using Newtonsoft.Json;
using System.Xml.Linq;

namespace Loom.Models;

/// <summary>
/// The root aggregate for a LOOM graph.
/// Owns all Nodes, Connections, and past ExecutionContexts for one workflow.
/// </summary>
public class Workflow
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string WfName { get; set; } = "Untitled Workflow";
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [JsonConverter(typeof(NodeListConverter))]
    public List<Node> Nodes { get; set; } = new();

    public List<Connection> Connections { get; set; } = new();

    // Persisted execution history (summary only — full results in DataStorage)
    public List<WorkflowExecutionContext> ExecutionHistory { get; set; } = new();

    // ── Constructors ─────────────────────────────────────────────────────────

    public Workflow() { }

    public Workflow(string name)
    {
        WfName = name;
    }

    // ── Save / Load ──────────────────────────────────────────────────────────

    /// <summary>Serializes this workflow to a .loom JSON file.</summary>
    public bool SaveWorkflow(string path)
    {
        try
        {
            var settings = DataStorage.SerializerSettings;
            var json = JsonConvert.SerializeObject(this, settings);
            File.WriteAllText(path, json);
            LastModified = DateTime.UtcNow;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Deserializes a .loom file into a new Workflow instance.</summary>
    public static Workflow? LoadWorkflow(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var settings = DataStorage.SerializerSettings;
            var wf = JsonConvert.DeserializeObject<Workflow>(json, settings);
            return wf;
        }
        catch
        {
            return null;
        }
    }

    // ── Execution kick-off (delegates to ExecutionEngine via DI) ─────────────

    /// <summary>
    /// Creates and returns a new ExecutionContext — callers pass it to
    /// ExecutionEngine.RunAsync(workflow, context).
    /// </summary>
    public WorkflowExecutionContext CreateExecutionContext()
        => new(SessionId);

    // ── Export (Phase 4 stub) ────────────────────────────────────────────────

    public string ExportToCSharp()
        => "// Code-generation (Phase 4) not yet implemented.";

    // ── Helpers ──────────────────────────────────────────────────────────────

    public void Touch() => LastModified = DateTime.UtcNow;
}

/// <summary>
/// Custom Newtonsoft converter for List&lt;Node&gt; that delegates each element
/// to NodeJsonConverter, enabling polymorphic (de)serialization.
/// </summary>
public class NodeListConverter : JsonConverter<List<Node>>
{
    private static readonly NodeJsonConverter _nodeConverter = new();

    public override List<Node> ReadJson(JsonReader reader, Type objectType,
        List<Node>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var result = new List<Node>();
        if (reader.TokenType != JsonToken.StartArray) return result;

        var ja = Newtonsoft.Json.Linq.JArray.Load(reader);
        foreach (var item in ja)
        {
            using var sub = item.CreateReader();
            var node = _nodeConverter.ReadJson(sub, typeof(Node), null, false, serializer);
            if (node is not null) result.Add(node);
        }
        return result;
    }

    public override void WriteJson(JsonWriter writer, List<Node>? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var node in value ?? Enumerable.Empty<Node>())
            _nodeConverter.WriteJson(writer, node, serializer);
        writer.WriteEndArray();
    }
}