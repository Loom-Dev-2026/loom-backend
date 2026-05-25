using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Terminal node — receives its upstream value and exposes it for display.
/// One input port named "Value"; no output ports.
/// </summary>
public class OutputNode : Node
{
    public object? NodeOutput { get; private set; }
    public string DisplayFormat { get; set; } = "ToString";

    public OutputNode()
    {
        Type = NodeType.Output;
        Label = "Output";
        AddInputPort("Value", "object");
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        NodeOutput = GetInputPort("Value")?.GetValue();
        return Task.FromResult(NodeOutput);
    }

    public override bool Validate() => true;

    public override object? GetOutput() => NodeOutput;

    public void Display() { /* Blazor components read NodeOutput directly */ }

    public string Format(object? value)
    {
        if (value is null) return "(null)";
        return DisplayFormat switch
        {
            "Json" => System.Text.Json.JsonSerializer.Serialize(value),
            _ => value.ToString() ?? "(null)"
        };
    }

    public string ExportValue() => Format(NodeOutput);
}
