namespace Loom.Models.Nodes;

/// <summary>Fallback for JSON-deserialized API nodes (runtime execution uses DI-created instances).</summary>
internal sealed class NullHttpClientFactory : IHttpClientFactory
{
    public static readonly NullHttpClientFactory Instance = new();

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient();
        ApiHttp.ApplyDefaultHeaders(client);
        return client;
    }
}
