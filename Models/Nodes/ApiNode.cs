using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Calls an external REST endpoint and returns the parsed response body.
/// Supports GET, POST, PUT, DELETE, PATCH with custom headers, JSON body,
/// timeout control, and full CancellationToken propagation.
///
/// Registered as a transient node — relies on IHttpClientFactory (DI).
/// </summary>
public class ApiNode : Node
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>HTTP method to use. Defaults to GET.</summary>
    public ApiHttpMethod Method { get; set; } = ApiHttpMethod.GET;

    /// <summary>
    /// Static headers baked into the node definition (e.g. Content-Type).
    /// These are merged with—and overridden by—headers arriving via the Headers port.
    /// </summary>
    public Dictionary<string, string> StaticHeaders { get; set; } = new();

    /// <summary>Default request timeout in seconds. Overridable per-instance.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    // ── State ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpClientFactory;
    private object? _lastOutput;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    // ── Constructor ──────────────────────────────────────────────────────────
    // ── Constructor ──────────────────────────────────────────────────────────

    // ✅ ADD THIS LINE (for JSON deserialization when loading saved workflows)
    public ApiNode() { }

    public ApiNode(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        Type = NodeType.Api;
        Label = "API Request";

        // Input ports
        AddInputPort("Url", "string");
        AddInputPort("Body", "object");
        AddInputPort("Headers", "object");  // accepts Dictionary<string,string>

        // Output ports
        AddOutputPort("Response", "object");
        AddOutputPort("StatusCode", "int");
        AddOutputPort("RawBody", "string");
        AddOutputPort("IsSuccess", "bool");
    }

    // ── Execution ────────────────────────────────────────────────────────────

    public override async Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        // Resolve URL from port
        var url = GetInputValue<string>("Url")?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("ApiNode: 'Url' input port has no value.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException($"ApiNode: '{url}' is not a valid absolute URI.");

        // Merge headers: static (design-time) + dynamic (runtime port)
        var headers = new Dictionary<string, string>(
            StaticHeaders, StringComparer.OrdinalIgnoreCase);

        var portHeaders = GetInputPort("Headers")?.GetValue();
        if (portHeaders is Dictionary<string, string> dynHeaders)
            foreach (var (k, v) in dynHeaders)
                headers[k] = v;

        // Build HttpContent from Body port
        HttpContent? content = BuildContent(GetInputPort("Body")?.GetValue(), headers);

        // Create client + apply timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var httpClient = _httpClientFactory.CreateClient("ApiNode");

        // Build request
        using var request = new HttpRequestMessage(ToHttpMethod(Method), url);
        foreach (var (k, v) in headers)
        {
            // Content-Type belongs on the content object, not the request headers
            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(k, v);
        }
        request.Content = content;

        // Execute
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"ApiNode: request to '{url}' timed out after {TimeoutSeconds} s.");
        }

        // Read body
        var rawBody = await httpResponse.Content.ReadAsStringAsync(cts.Token);
        var statusCode = (int)httpResponse.StatusCode;
        var isSuccess = httpResponse.IsSuccessStatusCode;

        // Attempt to parse body as JSON; fall back to raw string
        object? parsedBody;
        try
        {
            parsedBody = JsonSerializer.Deserialize<JsonElement>(rawBody, JsonOptions);
        }
        catch
        {
            parsedBody = rawBody;
        }

        // Write outputs
        SetOutputValue("Response", parsedBody);
        SetOutputValue("StatusCode", statusCode);
        SetOutputValue("RawBody", rawBody);
        SetOutputValue("IsSuccess", isSuccess);

        if (!isSuccess)
        {
            // Surface non-2xx as an error result so the execution engine marks failure
            throw new HttpRequestException(
                $"ApiNode: {Method} {url} returned HTTP {statusCode}. Body: {Truncate(rawBody, 500)}");
        }

        _lastOutput = parsedBody;
        return _lastOutput;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    public override bool Validate()
    {
        var url = GetInputValue<string>("Url")?.Trim();
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    public override object? GetOutput() => _lastOutput;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpMethod ToHttpMethod(ApiHttpMethod method) => method switch
    {
        ApiHttpMethod.GET => HttpMethod.Get,
        ApiHttpMethod.POST => HttpMethod.Post,
        ApiHttpMethod.PUT => HttpMethod.Put,
        ApiHttpMethod.DELETE => HttpMethod.Delete,
        ApiHttpMethod.PATCH => HttpMethod.Patch,
        _ => HttpMethod.Get,
    };

    private static HttpContent? BuildContent(
        object? bodyValue,
        IDictionary<string, string> headers)
    {
        if (bodyValue is null) return null;

        // Detect explicit content-type from headers
        headers.TryGetValue("Content-Type", out var contentType);

        // Already a string (e.g. raw XML or form data)?
        if (bodyValue is string rawString)
        {
            var mediaType = contentType ?? "text/plain";
            return new StringContent(rawString, Encoding.UTF8, mediaType);
        }

        // Default: serialize to JSON
        var json = JsonSerializer.Serialize(bodyValue, JsonOptions);
        return new StringContent(json, Encoding.UTF8, contentType ?? "application/json");
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";
}
