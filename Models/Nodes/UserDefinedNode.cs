using Loom.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Loom.Models.Nodes;

/// <summary>
/// Runs a short C# snippet. Port values are exposed as <c>inputs["A"]</c>, <c>inputs["B"]</c>.
/// Return a <c>bool</c> to drive If/Compare flows, or a number for math chains.
/// </summary>
public class UserDefinedNode : Node
{
    public const string DefaultScript =
        "var a = (double)inputs[\"A\"];\n" +
        "var b = (double)inputs[\"B\"];\n" +
        "return a + b;";

    public string ScriptCode { get; set; } = DefaultScript;

    private object? _lastOutput;

    public UserDefinedNode()
    {
        Type = NodeType.UserDefined;
        Label = "CustomScript";
        AddInputPort("A", "double");
        AddInputPort("B", "double");
        AddOutputPort("Value", "object");
        AddOutputPort("Result", "object");
    }

    public override async Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ScriptCode))
        {
            _lastOutput = ToDouble(GetInputPort("A")?.GetValue() ?? GetInputPort("Input")?.GetValue());
            SetResult(_lastOutput);
            return _lastOutput;
        }

        var inputs = BuildScriptInputs();

        var globals = new ScriptGlobals { inputs = inputs, ctx = ctx };

        try
        {
            _lastOutput = await CSharpScript.EvaluateAsync<object?>(
                ScriptCode, ScriptHostOptions.Default, globals, typeof(ScriptGlobals), cancellationToken);
        }
        catch (CompilationErrorException ex)
        {
            var detail = string.Join("; ", ex.Diagnostics.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Custom script compile error: {detail}", ex);
        }

        SetResult(_lastOutput);
        return _lastOutput;
    }

    public override bool Validate() => true;

    public override object? GetOutput() => _lastOutput;

    private Dictionary<string, object?> BuildScriptInputs()
    {
        var inputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var port in InputPorts)
            inputs[port.Name] = CoerceForPort(port);

        if (!inputs.ContainsKey("A") && inputs.TryGetValue("Input", out var legacyIn))
            inputs["A"] = legacyIn;

        return inputs;
    }

    private static object? CoerceForPort(Port port)
    {
        var raw = port.GetValue();
        if (raw is null)
            return IsNumeric(port.DataType) ? 0.0 : null;

        if (IsNumeric(port.DataType))
            return ToDouble(raw);

        if (IsBool(port.DataType))
            return ToBool(raw);

        return raw;
    }

    private void SetResult(object? value)
    {
        foreach (var port in OutputPorts)
            port.SetValue(value);
    }

    internal static double ToDouble(object? value)
    {
        if (value is null) return 0;
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            bool b => b ? 1.0 : 0.0,
            _ => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static bool ToBool(object? value) =>
        value switch
        {
            bool b => b,
            null => false,
            double d => Math.Abs(d) > double.Epsilon,
            float f => Math.Abs(f) > float.Epsilon,
            int i => i != 0,
            long l => l != 0,
            string s => bool.TryParse(s, out var b) ? b : !string.IsNullOrEmpty(s),
            _ => Convert.ToBoolean(value)
        };

    private static bool IsNumeric(string type) =>
        type.Equals("double", StringComparison.OrdinalIgnoreCase)
        || type.Equals("float", StringComparison.OrdinalIgnoreCase)
        || type.Equals("int", StringComparison.OrdinalIgnoreCase)
        || type.Equals("number", StringComparison.OrdinalIgnoreCase);

    private static bool IsBool(string type) =>
        type.Equals("bool", StringComparison.OrdinalIgnoreCase)
        || type.Equals("boolean", StringComparison.OrdinalIgnoreCase);
}

public class ScriptGlobals
{
    public Dictionary<string, object?> inputs { get; set; } = new();
    public WorkflowExecutionContext ctx { get; set; } = null!;
}
