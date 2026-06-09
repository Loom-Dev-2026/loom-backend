namespace Loom.Models.Nodes;

internal static class ApiHttp
{
    public const string ClientName = "LoomApi";

    public const string UserAgent = "LOOM-VisualWorkflow/1.0 (https://github.com/loom)";

    public static void ApplyDefaultHeaders(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(25);
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }
}
