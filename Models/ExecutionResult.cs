using System.Text.Json;

namespace Loom.Models;

public class ExecutionResult
{
    public Guid ResultId { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public Guid ExecutionId { get; set; }
    public object? OutputValue { get; set; }
    public long ExecutionTimeMs { get; set; }
    public ResultStatus Status { get; set; } = ResultStatus.Success;
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }

    public object? GetResult() => OutputValue;

    public string ToJson() => JsonSerializer.Serialize(new
    {
        ResultId,
        NodeId,
        ExecutionId,
        OutputValue = OutputValue?.ToString(),
        ExecutionTimeMs,
        Status = Status.ToString(),
        ErrorMessage
    }, new JsonSerializerOptions { WriteIndented = true });

    // ── Static factory helpers ───────────────────────────────────────────────

    public static ExecutionResult CreateSuccess(Guid nodeId, Guid executionId,
        object? value, long elapsedMs) => new()
        {
            NodeId = nodeId,
            ExecutionId = executionId,
            Status = ResultStatus.Success,
            OutputValue = value,
            ExecutionTimeMs = elapsedMs
        };

    public static ExecutionResult CreateError(Guid nodeId, Guid executionId,
        string message, string? stackTrace = null) => new()
        {
            NodeId = nodeId,
            ExecutionId = executionId,
            Status = ResultStatus.Error,
            ErrorMessage = message,
            StackTrace = stackTrace
        };

    public static ExecutionResult CreateSkipped(Guid nodeId, Guid executionId) => new()
    {
        NodeId = nodeId,
        ExecutionId = executionId,
        Status = ResultStatus.Skipped
    };
}