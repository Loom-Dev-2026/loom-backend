using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Loom.Models.Nodes;

/// <summary>
/// Allows Newtonsoft.Json to correctly serialize and deserialize the
/// abstract Node hierarchy by reading/writing a "Type" discriminator.
/// </summary>
public class NodeJsonConverter : JsonConverter<Node>
{
    public override Node ReadJson(JsonReader reader, Type objectType,
        Node? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var typeStr = jo["Type"]?.Value<string>();

        if (!Enum.TryParse<NodeType>(typeStr, out var nodeType))
            throw new JsonException($"Unknown NodeType discriminator: '{typeStr}'");

        Node node = nodeType switch
        {
            NodeType.Input => new InputNode(),
            NodeType.Output => new OutputNode(),
            NodeType.Arithmetic => new ArithmeticNode(),
            NodeType.Logic => new LogicNode(),
            NodeType.UserDefined => new UserDefinedNode(),
            NodeType.Api => new ApiNode(),
            _ => throw new JsonException($"Unhandled NodeType: {nodeType}")
        };

        // Populate the concrete type WITHOUT triggering the converter again
        using var subReader = jo.CreateReader();
        var tempSerializer = new JsonSerializer
        {
            ContractResolver = serializer.ContractResolver,
            NullValueHandling = serializer.NullValueHandling
        };
        // Copy converters except this one to avoid infinite recursion
        foreach (var c in serializer.Converters)
            if (c is not NodeJsonConverter)
                tempSerializer.Converters.Add(c);

        tempSerializer.Populate(subReader, node);
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
}