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
        return string.Equals(DataType, other.DataType, StringComparison.OrdinalIgnoreCase);
    }
}