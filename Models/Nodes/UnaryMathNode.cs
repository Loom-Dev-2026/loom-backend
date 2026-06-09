using Loom.Models;

namespace Loom.Models.Nodes;

public class UnaryMathNode : Node
{
    public string Operation { get; set; } = "Sqrt";
    public double Result { get; private set; }

    public UnaryMathNode()
    {
        Type = NodeType.UnaryMath;
        Label = "UnaryMath";
        AddInputPort("Value", "double");
        AddOutputPort("Result", "double");
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        var raw = GetInputPort("Value")?.GetValue();
        double v = raw is double d ? d : Convert.ToDouble(raw ?? 0);

        Result = Operation switch
        {
            "Sqrt" => Math.Sqrt(v),
            "Abs" => Math.Abs(v),
            "Ceiling" => Math.Ceiling(v),
            "Floor" => Math.Floor(v),
            "Round" => Math.Round(v),
            "Log" => v > 0 ? Math.Log(v) : double.NaN,
            "Log10" => v > 0 ? Math.Log10(v) : double.NaN,
            "Exp" => Math.Exp(v),
            "Sin" => Math.Sin(v),
            "Cos" => Math.Cos(v),
            "Tan" => Math.Tan(v),
            "Asin" => Math.Asin(v),
            "Acos" => Math.Acos(v),
            "Atan" => Math.Atan(v),
            "Square" => v * v,
            "Cube" => v * v * v,
            _ => v
        };

        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public override bool Validate() => true;
    public override object? GetOutput() => Result;
}
