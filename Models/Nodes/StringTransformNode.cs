using Loom.Models;

namespace Loom.Models.Nodes;

public class StringTransformNode : Node
{
    public string Operation { get; set; } = "Concat";
    public object? Result { get; private set; }

    public StringTransformNode()
    {
        Type = NodeType.StringTransform;
        Label = "StringTransform";
        AddInputPort("A", "string");
        AddInputPort("B", "string");
        AddOutputPort("Result", "object");
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        var a = GetInputPort("A")?.GetValue()?.ToString() ?? string.Empty;
        var b = GetInputPort("B")?.GetValue()?.ToString() ?? string.Empty;
        Result = Operation switch
        {
            "Concat" => a + b,
            "Replace" => a.Replace(b, GetInputPort("A")?.GetValue()?.ToString() ?? ""),
            "Contains" => a.Contains(b, StringComparison.OrdinalIgnoreCase),
            "StartsWith" => a.StartsWith(b, StringComparison.OrdinalIgnoreCase),
            "EndsWith" => a.EndsWith(b, StringComparison.OrdinalIgnoreCase),
            "IndexOf" => (double)a.IndexOf(b, StringComparison.OrdinalIgnoreCase),
            _ => a + b
        };
        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public override bool Validate() => true;
    public override object? GetOutput() => Result;
}
