using Loom.Models;

namespace Loom.Models.Nodes;

public class StringOpNode : Node
{
    public string Operation { get; set; } = "ToUpper";
    public object? Result { get; private set; }

    public StringOpNode()
    {
        Type = NodeType.StringOp;
        Label = "StringOp";
        AddInputPort("Value", "string");
        AddOutputPort("Result", "object");
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        var input = GetInputPort("Value")?.GetValue()?.ToString() ?? string.Empty;
        Result = Operation switch
        {
            "ToUpper" => input.ToUpperInvariant(),
            "ToLower" => input.ToLowerInvariant(),
            "Trim" => input.Trim(),
            "Length" => (double)input.Length,
                "Reverse" => new string(input.Reverse().ToArray()),
            _ => input
        };
        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public override bool Validate() => true;
    public override object? GetOutput() => Result;
}
