using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Loom.Models.Nodes;

/// <summary>
/// Allows Newtonsoft.Json to correctly serialize and deserialize the
/// abstract Node hierarchy by reading/writing a "Type" discriminator.
/// </summary>
public class NodeJsonConverter : JsonConverter<Node>
{
    private static readonly JsonSerializer _cachedSerializer = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    public override Node ReadJson(JsonReader reader, Type objectType,
        Node? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var typeStr = jo["Type"]?.Value<string>();

        if (!Enum.TryParse<NodeType>(typeStr, out var nodeType))
            throw new JsonException($"Unknown NodeType discriminator: '{typeStr}'");

        var label = jo["Label"]?.Value<string>();
        Node node = nodeType switch
        {
            NodeType.Input => new InputNode(),
            NodeType.Output => new OutputNode(),
            NodeType.Arithmetic when string.Equals(label, "MathN", StringComparison.OrdinalIgnoreCase)
                => new MultiArithmeticNode(),
            NodeType.Arithmetic => new ArithmeticNode(),
            NodeType.Logic => new LogicNode(),
            NodeType.StringOp => new StringOpNode(),
            NodeType.StringTransform => new StringTransformNode(),
            NodeType.UnaryMath => new UnaryMathNode(),
            NodeType.UserDefined => new UserDefinedNode(),
            NodeType.Weather => new WeatherNode(NullHttpClientFactory.Instance),
            NodeType.Api => CreateApiNodeForLabel(label),
            _ => throw new JsonException($"Node type '{nodeType}' is not supported.")
        };

        // Populate the concrete type WITHOUT triggering the converter again
        using var subReader = jo.CreateReader();
        _cachedSerializer.Populate(subReader, node);

        if (node is LogicNode logicNode && !string.IsNullOrWhiteSpace(logicNode.Label))
            logicNode.Predicate = PredicateForCanvasType(logicNode.Label) ?? logicNode.Predicate;

        return node;
    }

    public override void WriteJson(JsonWriter writer, Node? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }

        // Serialize as the concrete type so all subclass-specific properties are included
        var jo = JObject.FromObject(value, new JsonSerializer
        {
            ContractResolver = serializer.ContractResolver,
            NullValueHandling = serializer.NullValueHandling,
            // Intentionally no NodeJsonConverter here — value is already concrete
        });

        jo.WriteTo(writer);
    }

    private static Node CreateApiNodeForLabel(string? label) => label switch
    {
        "ApiLocation" => new IpLocationNode(NullHttpClientFactory.Instance),
        _ => new GeocodeNode(NullHttpClientFactory.Instance),
    };

    private static string? PredicateForCanvasType(string canvasType) => canvasType switch
    {
        "CompareEq" or "Equal" => "==",
        "CompareNe" => "!=",
        "CompareGt" => ">",
        "CompareGte" => ">=",
        "CompareLt" => "<",
        "CompareLte" => "<=",
        _ => null
    };
}