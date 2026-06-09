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
    private readonly SemaphoreSlim _lock = new(1, 1);

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
        await _lock.WaitAsync();
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
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Workflow?> LoadAsync(string path)
    {
        await _lock.WaitAsync();
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
        finally
        {
            _lock.Release();
        }
    }

    public bool SaveData(Workflow workflow)
    {
        _lock.Wait();
        try
        {
            var path = Path.Combine(_baseDirectory, $"{workflow.SessionId}.loom");
            var json = JsonConvert.SerializeObject(workflow, SerializerSettings);
            File.WriteAllText(path, json);
            workflow.Touch();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Workflow? LoadData(string path)
    {
        _lock.Wait();
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Workflow>(json, SerializerSettings);
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool UpdateData(Workflow workflow) => SaveData(workflow);

    public bool DeleteData(string path)
    {
        _lock.Wait();
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Execution history ────────────────────────────────────────────────────

    public async Task<bool> SaveExecutionAsync(Workflow workflow,
        WorkflowExecutionContext ctx)
    {
        await _lock.WaitAsync();
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
        finally
        {
            _lock.Release();
        }
    }

    public bool SaveExecution(WorkflowExecutionContext ctx)
    {
        _lock.Wait();
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
        finally
        {
            _lock.Release();
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
        _lock.Wait();
        try
        {
            var path = Path.Combine(_baseDirectory, "recovery",
                $"{sessionId}.recovery.loom");
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── User‑scoped paths ────────────────────────────────────────────────────

    private static string SanitizeUsername(string username)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(username.Where(c => !invalid.Contains(c)).ToArray());
    }

    public string GetUserDirectory(string username)
    {
        var dir = Path.Combine(_baseDirectory, SanitizeUsername(username));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    public IEnumerable<string> ListWorkflowFiles(string? username = null)
    {
        var dir = username is not null
            ? GetUserDirectory(username)
            : _baseDirectory;

        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.loom");
    }

    public bool DeleteWorkflowFile(string path, string? username = null)
    {
        var resolved = ResolveWorkflowPath(path, username);
        if (!File.Exists(resolved))
            return false;

        _lock.Wait();
        try
        {
            File.Delete(resolved);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string ResolveWorkflowPath(string path, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
            return trimmed.EndsWith(".loom", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".loom";

        var fileName = Path.GetFileName(trimmed);
        if (!fileName.EndsWith(".loom", StringComparison.OrdinalIgnoreCase))
            fileName += ".loom";

        var baseDir = username is not null
            ? GetUserDirectory(username)
            : _baseDirectory;

        return Path.Combine(baseDir, fileName);
    }
}