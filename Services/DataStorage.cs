using Loom.Models;
using Loom.Models.Nodes;
using Newtonsoft.Json;

namespace Loom.Services;

/// <summary>
/// Handles all file-system persistence for LOOM:
///   • Workflow save / load (.loom JSON files)
///   • Execution context / result storage (sidecar .exec.json files)
///   • Auto-save recovery files
/// </summary>
public class DataStorage
{
    private readonly string _baseDirectory;

    // Shared serializer settings — used by Workflow and DataStorage alike
    public static readonly JsonSerializerSettings SerializerSettings =
        new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new NodeJsonConverter() }
        };

    public DataStorage(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LOOM");
        Directory.CreateDirectory(_baseDirectory);
    }

    // ── Workflow persistence ─────────────────────────────────────────────────

    public async Task<bool> SaveAsync(Workflow workflow, string path)
    {
        try
        {
            var json = JsonConvert.SerializeObject(workflow, SerializerSettings);
            await File.WriteAllTextAsync(path, json);
            workflow.Touch();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Workflow?> LoadAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var wf = JsonConvert.DeserializeObject<Workflow>(json, SerializerSettings);
            return wf;
        }
        catch
        {
            return null;
        }
    }

    public bool SaveData(Workflow workflow)
    {
        var path = Path.Combine(_baseDirectory, $"{workflow.SessionId}.loom");
        var json = JsonConvert.SerializeObject(workflow, SerializerSettings);
        File.WriteAllText(path, json);
        workflow.Touch();
        return true;
    }

    public Workflow? LoadData(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<Workflow>(json, SerializerSettings);
    }

    public bool UpdateData(Workflow workflow) => SaveData(workflow);

    public bool DeleteData(string path)
    {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    // ── Execution history ────────────────────────────────────────────────────

    public async Task<bool> SaveExecutionAsync(Workflow workflow,
        WorkflowExecutionContext ctx)
    {
        try
        {
            var dir = Path.Combine(_baseDirectory, "executions",
                workflow.SessionId.ToString());
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{ctx.ExecutionId}.exec.json");
            var json = JsonConvert.SerializeObject(ctx, SerializerSettings);
            await File.WriteAllTextAsync(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SaveExecution(WorkflowExecutionContext ctx)
    {
        try
        {
            var dir = Path.Combine(_baseDirectory, "executions",
                ctx.WorkflowId.ToString());
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{ctx.ExecutionId}.exec.json");
            var json = JsonConvert.SerializeObject(ctx, SerializerSettings);
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<WorkflowExecutionContext> LoadExecutionHistory(Guid workflowId)
    {
        var dir = Path.Combine(_baseDirectory, "executions", workflowId.ToString());
        if (!Directory.Exists(dir)) return new List<WorkflowExecutionContext>();

        var result = new List<WorkflowExecutionContext>();
        foreach (var file in Directory.GetFiles(dir, "*.exec.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var ctx = JsonConvert.DeserializeObject<WorkflowExecutionContext>(
                    json, SerializerSettings);
                if (ctx is not null)
                {
                    ctx.RebuildIndex();
                    result.Add(ctx);
                }
            }
            catch { /* skip corrupt execution files */ }
        }

        return result.OrderByDescending(c => c.StartTime).ToList();
    }

    // ── Auto-save / recovery ─────────────────────────────────────────────────

    public async Task<bool> AutoSaveAsync(Workflow workflow)
    {
        var path = Path.Combine(_baseDirectory, "recovery",
            $"{workflow.SessionId}.recovery.loom");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return await SaveAsync(workflow, path);
    }

    public Workflow? LoadRecovery(Guid sessionId)
    {
        var path = Path.Combine(_baseDirectory, "recovery",
            $"{sessionId}.recovery.loom");
        return File.Exists(path) ? LoadData(path) : null;
    }

    public bool HasRecovery(Guid sessionId)
    {
        var path = Path.Combine(_baseDirectory, "recovery",
            $"{sessionId}.recovery.loom");
        return File.Exists(path);
    }

    public void DeleteRecovery(Guid sessionId)
    {
        var path = Path.Combine(_baseDirectory, "recovery",
            $"{sessionId}.recovery.loom");
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    public IEnumerable<string> ListWorkflowFiles()
        => Directory.GetFiles(_baseDirectory, "*.loom");
}