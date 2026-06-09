using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Geocodes a place name to coordinates via OpenStreetMap Nominatim (free, no API key).
/// Policy: https://operations.osmfoundation.org/policies/nominatim/
/// </summary>
public class GeocodeNode : Node
{
    public string PlaceQuery { get; set; } = "London, UK";
    public int TimeoutSeconds { get; set; } = 15;

    private readonly IHttpClientFactory _httpClientFactory;
    private object? _lastOutput;

    public GeocodeNode(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? NullHttpClientFactory.Instance;
        Type = NodeType.Api;
        Label = "ApiGeocode";

        AddInputPort("Place", "string");
        AddOutputPort("Result", "string");
        AddOutputPort("Latitude", "double");
        AddOutputPort("Longitude", "double");
    }

    public override async Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        var query = GetInputValue<string>("Place")?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            query = PlaceQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Geocode: enter a city or address.");

        var url =
            "https://nominatim.openstreetmap.org/search?" +
            $"q={Uri.EscapeDataString(query)}&format=json&limit=1";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var client = _httpClientFactory.CreateClient(ApiHttp.ClientName);
        var results = await client.GetFromJsonAsync<List<NominatimResult>>(url, cts.Token)
            ?? throw new InvalidOperationException("Geocode: empty response.");

        if (results.Count == 0)
            throw new InvalidOperationException($"Geocode: no results for \"{query}\".");

        var hit = results[0];
        if (!double.TryParse(hit.Lat, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(hit.Lon, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            throw new InvalidOperationException("Geocode: could not parse coordinates.");
        }

        var name = hit.DisplayName ?? query;
        var summary = $"{name} ({lat:F4}, {lon:F4})";
        SetOutputValue("Result", summary);
        SetOutputValue("Latitude", lat);
        SetOutputValue("Longitude", lon);

        _lastOutput = summary;
        return _lastOutput;
    }

    public override bool Validate() =>
        !string.IsNullOrWhiteSpace(PlaceQuery)
        || !string.IsNullOrWhiteSpace(GetInputValue<string>("Place"));

    public override object? GetOutput() => _lastOutput;

    private sealed class NominatimResult
    {
        [JsonPropertyName("lat")]
        public string Lat { get; init; } = "0";

        [JsonPropertyName("lon")]
        public string Lon { get; init; } = "0";

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }
    }
}

public sealed class GeocodeResult
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string PlaceName { get; init; } = string.Empty;
}
