using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Holds a user-provided constant value and exposes it as graph input.
/// No input ports; one output port named "Value".
/// </summary>
public class InputNode : Node
{
    public object? UserInput { get; set; }
    public string InputType { get; set; } = "object";

    private object? _lastOutput;

    public InputNode()
    {
        Type = NodeType.Input;
        Label = "Input";
        AddOutputPort("Value", "object");
    }

    public InputNode(object? value) : this()
    {
        UserInput = value;
    }

    public override Task<object?> Execute(WorkflowExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        _lastOutput = UserInput;
        GetOutputPort("Value")?.SetValue(_lastOutput);
        return Task.FromResult(_lastOutput);
    }

    public override bool Validate() => true; // input can legally be null

    public override object? GetOutput() => _lastOutput;

    public void SetValue(object? value) => UserInput = value;
}
