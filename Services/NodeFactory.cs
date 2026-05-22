using Loom.Models;
using Loom.Models.Nodes;

namespace Loom.Services;

/// <summary>
/// Factory that instantiates concrete Node subclasses from a NodeType enum value.
/// Keeps NodeManager decoupled from every concrete subclass (Factory Method pattern).
/// </summary>
public interface INodeFactory
{
    Node Create(NodeType type, LoomPoint? position = null);
    IReadOnlyList<NodeType> GetAvailableTypes();
}

public class NodeFactory : INodeFactory
{
    private static readonly IReadOnlyList<NodeType> _available =
        Enum.GetValues<NodeType>().ToList();

    public Node Create(NodeType type, LoomPoint? position = null)
    {
        Node node = type switch
        {
            NodeType.Input => new InputNode(),
            NodeType.Output => new OutputNode(),
            NodeType.Arithmetic => new ArithmeticNode(),
            NodeType.Logic => new LogicNode(),
            NodeType.UserDefined => new UserDefinedNode(),
            NodeType.Api => new ApiNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(type),
                     $"Unknown NodeType: {type}")
        };

        if (position is not null)
            node.Position = position;

        return node;
    }

    public IReadOnlyList<NodeType> GetAvailableTypes() => _available;
}