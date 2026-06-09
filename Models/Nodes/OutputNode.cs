using Loom.Models;



namespace Loom.Models.Nodes;



/// <summary>

/// Displays the incoming value and exposes the same value on an output port for chaining.

/// </summary>

public class OutputNode : Node

{

    public object? NodeOutput { get; private set; }

    public string DisplayLabel { get; set; } = "Answer";

    public string DisplayFormat { get; set; } = "ToString";



    public OutputNode()

    {

        Type = NodeType.Output;

        Label = "Output";

        AddInputPort("Value", "object");

        AddOutputPort("Value", "object");

    }



    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)

    {

        NodeOutput = CoerceDisplayValue(GetInputPort("Value")?.GetValue());

        GetOutputPort("Value")?.SetValue(NodeOutput);

        return Task.FromResult(NodeOutput);

    }

    private static object? CoerceDisplayValue(object? value)

    {

        if (value is null) return null;

        if (value is double or float or int or long or decimal or bool) return value;

        if (double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,

            System.Globalization.CultureInfo.InvariantCulture, out var n))

            return n;

        return value;

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

