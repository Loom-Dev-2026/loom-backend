using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Estimates location from the server's public IP using ip-api.com (free tier, no key).
/// Docs: https://ip-api.com/docs/api:json
/// </summary>
public class IpLocationNode : Node
{
    public int TimeoutSeconds { get; set; } = 15;

    private readonly IHttpClientFactory _httpClientFactory;
    private object? _lastOutput;

    public IpLocationNode(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? NullHttpClientFactory.Instance;
        Type = NodeType.Api;
        Label = "ApiLocation";

        AddOutputPort("Result", "string");
        AddOutputPort("Latitude", "double");
        AddOutputPort("Longitude", "double");
    }

    public override async Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        const string url = "http://ip-api.com/json/?fields=status,message,country,regionName,city,lat,lon";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var client = _httpClientFactory.CreateClient(ApiHttp.ClientName);
        var data = await client.GetFromJsonAsync<IpApiResponse>(url, cts.Token)
            ?? throw new InvalidOperationException("Location: empty response.");

        if (!string.Equals(data.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Location lookup failed: {data.Message ?? data.Status ?? "unknown error"}");
        }

        var summary =
            $"📍 {data.City}, {data.RegionName}, {data.Country} ({data.Lat:F4}, {data.Lon:F4})";
        SetOutputValue("Result", summary);
        SetOutputValue("Latitude", data.Lat);
        SetOutputValue("Longitude", data.Lon);

        _lastOutput = summary;
        return _lastOutput;
    }

    public override bool Validate() => true;

    public override object? GetOutput() => _lastOutput;

    private sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("regionName")]
        public string? RegionName { get; init; }

        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("lat")]
        public double Lat { get; init; }

        [JsonPropertyName("lon")]
        public double Lon { get; init; }
    }
}

public sealed class IpLocationResult
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string City { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}
