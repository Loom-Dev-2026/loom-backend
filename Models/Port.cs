using Newtonsoft.Json;

namespace Loom.Models;

public class Port
{
    public Guid PortId { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PortDirection Direction { get; set; }
    public string DataType { get; set; } = "object";
    public bool IsConnected { get; set; }

    [JsonIgnore]
    public object? Value { get; private set; }

    public Port() { }

    public Port(string name, PortDirection direction, string dataType = "object")
    {
        Name = name;
        Direction = direction;
        DataType = dataType;
    }

    public void SetValue(object? value) => Value = value;
    public object? GetValue() => Value;

    public bool IsCompatibleWith(Port other)
    {
        if (DataType == "object" || other.DataType == "object") return true;
        if (string.Equals(DataType, other.DataType, StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsNumeric(DataType) && IsNumeric(other.DataType))
            return true;
        if (IsBool(DataType) && IsBool(other.DataType))
            return true;
        if (IsBool(DataType) && IsNumeric(other.DataType))
            return true;
        if (IsNumeric(DataType) && IsBool(other.DataType))
            return true;
        return false;
    }

    private static bool IsNumeric(string type) =>
        type.Equals("double", StringComparison.OrdinalIgnoreCase)
        || type.Equals("float", StringComparison.OrdinalIgnoreCase)
        || type.Equals("int", StringComparison.OrdinalIgnoreCase)
        || type.Equals("number", StringComparison.OrdinalIgnoreCase);

    private static bool IsBool(string type) =>
        type.Equals("bool", StringComparison.OrdinalIgnoreCase)
        || type.Equals("boolean", StringComparison.OrdinalIgnoreCase);
}