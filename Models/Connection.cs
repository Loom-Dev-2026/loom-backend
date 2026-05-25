namespace Loom.Models;

public class Connection
{
    public Guid ConnectionId { get; set; } = Guid.NewGuid();
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public Guid SourcePortId { get; set; }
    public Guid TargetPortId { get; set; }
    public string DataType { get; set; } = "object";
    public bool IsValid { get; set; } = true;

    public Connection() { }

    public Connection(Guid sourceNodeId, Guid sourcePortId,
                      Guid targetNodeId, Guid targetPortId)
    {
        SourceNodeId = sourceNodeId;
        SourcePortId = sourcePortId;
        TargetNodeId = targetNodeId;
        TargetPortId = targetPortId;
    }

    /// <summary>Validates direction and type compatibility between two ports.</summary>
    public bool Validate(Port source, Port target)
    {
        if (source.Direction != PortDirection.Output) { IsValid = false; return false; }
        if (target.Direction != PortDirection.Input) { IsValid = false; return false; }
        IsValid = source.IsCompatibleWith(target);
        return IsValid;
    }
}