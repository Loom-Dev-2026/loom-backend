using Loom.Api;
using Loom.Models;
using Loom.Models.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Services;

public class NodeFactory
{
    private readonly IServiceProvider _services;

    public NodeFactory(IServiceProvider services) => _services = services;

    public Node Create(NodeType nodeType, string? canvasType = null, params object[] additionalArgs)
    {
        var clrType = ResolveClrType(nodeType, canvasType);
        var node = (Node)ActivatorUtilities.CreateInstance(_services, clrType, additionalArgs);

        if (node is LogicNode logic)
            ConfigureLogicNode(logic, canvasType);
        if (node is InputNode input)
            ConfigureInputNode(input, canvasType);

        return node;
    }

    private static Type ResolveClrType(NodeType nodeType, string? canvasType) => nodeType switch
    {
        NodeType.Arithmetic when CanvasNodeCatalog.IsMultiMath(canvasType ?? "") =>
            typeof(MultiArithmeticNode),
        NodeType.Arithmetic => typeof(ArithmeticNode),
        NodeType.Input => typeof(InputNode),
        NodeType.Output => typeof(OutputNode),
        NodeType.Logic => typeof(LogicNode),
        NodeType.StringOp => typeof(StringOpNode),
        NodeType.StringTransform => typeof(StringTransformNode),
        NodeType.UnaryMath => typeof(UnaryMathNode),
        NodeType.UserDefined => typeof(UserDefinedNode),
        NodeType.Weather => typeof(WeatherNode),
        NodeType.Api => ResolveApiClrType(canvasType),
        _ => throw new ArgumentOutOfRangeException(
            nameof(nodeType), nodeType,
            $"Unsupported node type: {nodeType}.")
    };

    private static Type ResolveApiClrType(string? canvasType) => canvasType switch
    {
        "ApiLocation" => typeof(IpLocationNode),
        _ => typeof(GeocodeNode),
    };

    private static void ConfigureInputNode(InputNode input, string? canvasType)
    {
        if (!string.Equals(canvasType, "StringInput", StringComparison.OrdinalIgnoreCase))
            return;

        input.InputType = "string";
        var valuePort = input.GetOutputPort("Value");
        if (valuePort is not null)
            valuePort.DataType = "string";
    }

    private static void ConfigureLogicNode(LogicNode logic, string? canvasType)
    {
        if (string.IsNullOrWhiteSpace(canvasType)) return;

        logic.Predicate = canvasType switch
        {
            "CompareEq" or "Equal" or "Compare" => "==",
            "CompareNe" => "!=",
            "CompareGt" => ">",
            "CompareGte" => ">=",
            "CompareLt" => "<",
            "CompareLte" => "<=",
            _ => logic.Predicate
        };
    }
}
