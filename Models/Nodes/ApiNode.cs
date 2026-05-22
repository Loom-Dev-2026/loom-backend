using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Calls an external REST endpoint and returns the parsed JSON body.
/// HTTP implementation left for Phase 2 — stub is here so the graph
/// can still include API_Nodes, load/save them, and connect them.
/// </summary>
public class ApiNode : Node
{
    public string Url { get; set; } = string.Empty;
    public ApiHttpMethod Method { get; set; } = ApiHttpMethod.GET;
    public Dictionary<string, string> Headers { get; set; } = new();

    private object? _lastOutput;

    public ApiNode()
    {
        Type = NodeType.Api;
        Label = "API";
        AddInputPort("Body", "object");
        AddOutputPort("Response", "object");
    }

    public ApiNode(string url, ApiHttpMethod method) : this()
    {
        Url = url;
        Method = method;
        Label = $"{method} {url}";
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx)
    {
        // TODO (Phase 2): Implement HTTP call via HttpClient.
        // For now, return a placeholder to keep the execution graph valid.
        _lastOutput = new { status = "stub", url = Url, method = Method.ToString() };
        GetOutputPort("Response")?.SetValue(_lastOutput);
        return Task.FromResult(_lastOutput);
    }

    public override bool Validate()
        => !string.IsNullOrWhiteSpace(Url) &&
           Uri.TryCreate(Url, UriKind.Absolute, out _);

    public override object? GetOutput() => _lastOutput;
}