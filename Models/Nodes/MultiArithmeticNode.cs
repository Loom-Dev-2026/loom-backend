using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Aggregates up to six numeric inputs (only wired ports are used).
/// </summary>
public class MultiArithmeticNode : Node
{
    public const int InputCount = 6;

    public OpType Operation { get; set; } = OpType.Add;
    public double Result { get; private set; }

    public MultiArithmeticNode()
    {
        Type = NodeType.Arithmetic;
        Label = "MathN";
        for (var i = 1; i <= InputCount; i++)
            AddInputPort($"In{i}", "double");
        AddOutputPort("Result", "double");
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        var values = InputPorts
            .Where(p => p.IsConnected)
            .Select(p => ToDouble(p.GetValue()))
            .ToList();

        if (values.Count == 0)
            values.Add(0);

        Result = Operation switch
        {
            OpType.Add => values.Sum(),
            OpType.Multiply => values.Aggregate(1.0, (a, b) => a * b),
            OpType.Subtract => values.Count == 0 ? 0 : values[0] - values.Skip(1).Sum(),
            OpType.Divide => DivideSequence(values),
            _ => values.Sum()
        };

        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public override bool Validate()
    {
        // Divide-by-zero is checked in Execute after inputs are propagated.
        return true;
    }

    public override object? GetOutput() => Result;

    private static double DivideSequence(List<double> values)
    {
        if (values.Count == 0) return 0;
        var acc = values[0];
        foreach (var v in values.Skip(1))
        {
            if (v == 0) throw new DivideByZeroException("Divisor cannot be zero.");
            acc /= v;
        }
        return acc;
    }

    private static double ToDouble(object? value)
    {
        if (value is null) return 0;
        return value is double d ? d : Convert.ToDouble(value);
    }
}
