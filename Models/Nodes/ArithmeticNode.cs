using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Performs a binary arithmetic operation (Add / Subtract / Multiply / Divide)
/// on two double-typed input ports and produces a double output.
/// </summary>
public class ArithmeticNode : Node
{
    public OpType Operation { get; set; } = OpType.Add;
    public double Result { get; private set; }

    public ArithmeticNode()
    {
        Type = NodeType.Arithmetic;
        Label = "Arithmetic";
        AddInputPort("A", "double");
        AddInputPort("B", "double");
        AddOutputPort("Result", "double");
    }

    public ArithmeticNode(OpType op) : this()
    {
        Operation = op;
        Label = op.ToString();
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        double a = ToDouble(GetInputPort("A")?.GetValue());
        double b = ToDouble(GetInputPort("B")?.GetValue());

        Result = Operation switch
        {
            OpType.Add => Add(a, b),
            OpType.Subtract => Subtract(a, b),
            OpType.Multiply => Multiply(a, b),
            OpType.Divide => Divide(a, b),
            _ => throw new InvalidOperationException($"Unknown operation: {Operation}")
        };

        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public override bool Validate()
    {
        if (Operation == OpType.Divide)
        {
            double b = ToDouble(GetInputPort("B")?.GetValue());
            return b != 0;
        }
        return true;
    }

    public override object? GetOutput() => Result;

    // ── Arithmetic helpers ───────────────────────────────────────────────────

    public double Add(double a, double b) => a + b;
    public double Subtract(double a, double b) => a - b;
    public double Multiply(double a, double b) => a * b;
    public double Divide(double a, double b)
    {
        if (b == 0) throw new DivideByZeroException("Divisor (B) cannot be zero.");
        return a / b;
    }

    private static double ToDouble(object? value)
    {
        if (value is null) return 0;
        return value is double d ? d : Convert.ToDouble(value);
    }
}
