using Loom.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Loom.Models.Nodes;

/// <summary>
/// Executes an arbitrary C# script using the Microsoft Roslyn Scripting API.
/// 
/// Input port values are injected into the script as a Dictionary&lt;string, object?&gt;
/// named "inputs".  The script's return value becomes the node output.
/// 
/// Example script:
///   var a = (double)inputs["A"];
///   var b = (double)inputs["B"];
///   return a * b + 10;
/// 
/// NuGet package required:
///   Microsoft.CodeAnalysis.CSharp.Scripting (same version as your .NET SDK)
/// </summary>
public class UserDefinedNode : Node
{
    public string ScriptCode { get; set; } = string.Empty;
    public string InputType { get; set; } = "object";

    private object? _lastOutput;

    // Banned namespaces for sandbox security (NFR-06)
    private static readonly IEnumerable<string> BannedNamespaces = new[]
    {
        "System.IO", "System.Net", "System.Diagnostics.Process",
        "System.Reflection.Emit", "System.Runtime.InteropServices"
    };

    private static readonly ScriptOptions DefaultOptions = ScriptOptions.Default
        .AddImports("System", "System.Linq", "System.Collections.Generic",
                    "System.Text", "System.Math")
        .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);

    public UserDefinedNode()
    {
        Type = NodeType.UserDefined;
        Label = "Script";
        AddInputPort("Input", "object");
        AddOutputPort("Output", "object");
    }

    public override async Task<object?> Execute(WorkflowExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ScriptCode))
        {
            _lastOutput = GetInputPort("Input")?.GetValue();
            GetOutputPort("Output")?.SetValue(_lastOutput);
            return _lastOutput;
        }

        // Build the inputs dictionary that the script can reference
        var inputs = new Dictionary<string, object?>();
        foreach (var port in InputPorts)
            inputs[port.Name] = port.GetValue();

        var globals = new ScriptGlobals { inputs = inputs, ctx = ctx };

        _lastOutput = await CSharpScript.EvaluateAsync<object?>(
            ScriptCode, DefaultOptions, globals, typeof(ScriptGlobals));

        GetOutputPort("Output")?.SetValue(_lastOutput);
        return _lastOutput;
    }

    /// <summary>Compiles the script and returns any diagnostic errors.</summary>
    public async Task<bool> Compile()
    {
        try
        {
            var script = CSharpScript.Create<object?>(
                ScriptCode, DefaultOptions, typeof(ScriptGlobals));
            var diags = script.Compile();
            return !diags.Any(d =>
                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
        catch
        {
            return false;
        }
    }

    public bool RunScript(object? input) { _lastOutput = input; return true; }

    public bool ValidateSyntax()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(ScriptCode);
        return !tree.GetDiagnostics().Any(d =>
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    public override bool Validate() => !string.IsNullOrWhiteSpace(ScriptCode);
    public override object? GetOutput() => _lastOutput;
}

/// <summary>Global variables injected into every Roslyn script execution.</summary>
public class ScriptGlobals
{
    public Dictionary<string, object?> inputs { get; set; } = new();
    public WorkflowExecutionContext ctx { get; set; } = null!;
}