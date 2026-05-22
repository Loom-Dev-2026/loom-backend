using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Evaluates a boolean predicate.  The predicate string is one of the
/// supported operators applied to the A and B input ports.
/// 
/// Supported predicates: "==", "!=", ">", ">=", "<", "<="
/// For free-form C# boolean expressions use UserDefinedNode instead.
/// </summary>
public class LogicNode : Node
{
    /// <summary>Operator string: "==", "!=", ">", ">=", "<", "<="</summary>
    public string Predicate { get; set; } = "==";
    public bool Result { get; private set; }

    public LogicNode()
    {
        Type = NodeType.Logic;
        Label = "Logic";
        AddInputPort("A", "object");
        AddInputPort("B", "object");
        AddOutputPort("Result", "bool");
    }

    public LogicNode(string predicate) : this()
    {
        Predicate = predicate;
        Label = $"Logic [{predicate}]";
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx)
    {
        object? a = GetInputPort("A")?.GetValue();
        object? b = GetInputPort("B")?.GetValue();
        Result = Evaluate(a, b);
        GetOutputPort("Result")?.SetValue(Result);
        return Task.FromResult<object?>(Result);
    }

    public bool Evaluate(object? a, object? b)
    {
        // Numeric comparison path
        if (TryToDouble(a, out double da) && TryToDouble(b, out double db))
        {
            return Predicate switch
            {
                "==" => da == db,
                "!=" => da != db,
                ">" => da > db,
                ">=" => da >= db,
                "<" => da < db,
                "<=" => da <= db,
                _ => throw new InvalidOperationException($"Unknown predicate: '{Predicate}'")
            };
        }

        // String / object comparison path
        string sa = a?.ToString() ?? string.Empty;
        string sb = b?.ToString() ?? string.Empty;
        return Predicate switch
        {
            "==" => string.Equals(sa, sb, StringComparison.Ordinal),
            "!=" => !string.Equals(sa, sb, StringComparison.Ordinal),
            _ => throw new InvalidOperationException(
                $"Predicate '{Predicate}' requires numeric operands.")
        };
    }

    public override bool Validate()
    {
        var valid = new[] { "==", "!=", ">", ">=", "<", "<=" };
        return valid.Contains(Predicate);
    }

    public override object? GetOutput() => Result;

    public void SetPredicate(string p) => Predicate = p;

    private static bool TryToDouble(object? value, out double result)
    {
        result = 0;
        if (value is null) return false;
        try { result = Convert.ToDouble(value); return true; }
        catch { return false; }
    }
}