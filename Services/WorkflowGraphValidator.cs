using Loom.Models;
using Loom.Models.Nodes;

namespace Loom.Services;

/// <summary>Validates workflow graphs before execution or export.</summary>
public static class WorkflowGraphValidator
{
    public static (bool IsValid, string? Error) Validate(Workflow workflow, NodeManager nodeManager)
    {
        if (workflow.Nodes.Count == 0)
            return (false, "Workflow has no nodes.");

        if (nodeManager.DetectCycles(workflow))
            return (false, "Workflow contains a cycle.");

        foreach (var node in workflow.Nodes)
        {
            if (!node.Validate())
                return (false, $"Node '{node.Label}' failed validation.");
        }

        try
        {
            _ = nodeManager.BuildTopologicalOrder(workflow);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }

        return (true, null);
    }
}
